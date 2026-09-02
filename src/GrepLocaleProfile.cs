namespace Icod.Grep;

using System.Globalization;
using System.Text;
using Icod.CommandFramework.RegularExpressions;
using Icod.CommandFramework.Text;

/// <summary>Composes GNU grep LC_CTYPE and LC_COLLATE behavior from shared framework providers.</summary>
internal sealed class GrepLocaleProfile {
	private static readonly Encoding StrictUtf8 = new UTF8Encoding(
		encoderShouldEmitUTF8Identifier: false,
		throwOnInvalidBytes: true
	);

	private GrepLocaleProfile(
		string ctypeName,
		string collateName,
		TextDecodingMode decodingMode,
		Encoding patternEncoding,
		IRegularExpressionCharacterClassProvider characterClassProvider
	) {
		CtypeName = ctypeName;
		CollateName = collateName;
		DecodingMode = decodingMode;
		PatternEncoding = patternEncoding;
		CharacterClassProvider = characterClassProvider;
	}

	/// <summary>Gets the resolved LC_CTYPE locale name.</summary>
	public string CtypeName { get; }

	/// <summary>Gets the resolved LC_COLLATE locale name.</summary>
	public string CollateName { get; }

	/// <summary>Gets the byte or UTF-8 input decoding mode.</summary>
	public TextDecodingMode DecodingMode { get; }

	/// <summary>Gets the encoding used for pattern files.</summary>
	public Encoding PatternEncoding { get; }

	/// <summary>Gets the composed regular-expression locale provider.</summary>
	public IRegularExpressionCharacterClassProvider CharacterClassProvider { get; }

	/// <summary>Gets whether malformed UTF-8 output must be suppressed unless text mode is forced.</summary>
	public bool DetectEncodingErrors => TextDecodingMode.Utf8 == DecodingMode;

	/// <summary>Resolves the current process locale categories using GNU/POSIX precedence.</summary>
	public static GrepLocaleProfile ResolveCurrent() => Resolve(
		Environment.GetEnvironmentVariable( "LC_ALL" ),
		Environment.GetEnvironmentVariable( "LC_CTYPE" ),
		Environment.GetEnvironmentVariable( "LC_COLLATE" ),
		Environment.GetEnvironmentVariable( "LANG" )
	);

	/// <summary>Resolves LC_CTYPE and LC_COLLATE independently from supplied environment values.</summary>
	public static GrepLocaleProfile Resolve(
		string? lcAll,
		string? lcCtype,
		string? lcCollate,
		string? lang
	) {
		var ctype = ResolveCategory( FirstNonempty( lcAll, lcCtype, lang ) );
		var collate = ResolveCategory( FirstNonempty( lcAll, lcCollate, lang ) );
		IRegularExpressionCharacterClassProvider ctypeProvider = ctype.IsByteLocale
			? PosixCLocaleRegularExpressionCharacterClassProvider.Instance
			: new UnicodeRegularExpressionCharacterClassProvider( ctype.Culture! );
		IRegularExpressionCharacterClassProvider collateProvider = collate.IsByteLocale
			? PosixCLocaleRegularExpressionCharacterClassProvider.Instance
			: new UnicodeRegularExpressionCharacterClassProvider( collate.Culture! );
		var decodingMode = ctype.IsByteLocale ? TextDecodingMode.Bytes : TextDecodingMode.Utf8;
		return new GrepLocaleProfile(
			ctype.Name,
			collate.Name,
			decodingMode,
			ctype.IsByteLocale ? Encoding.Latin1 : StrictUtf8,
			new CategoryCompositeProvider( ctypeProvider, collateProvider )
		);
	}

	private static LocaleCategory ResolveCategory( string? requestedName ) {
		var name = string.IsNullOrWhiteSpace( requestedName ) ? "C" : requestedName.Trim();
		if (
			string.Equals( name, "C", StringComparison.OrdinalIgnoreCase )
			|| string.Equals( name, "POSIX", StringComparison.OrdinalIgnoreCase )
		) {
			return new LocaleCategory( "C", true, null );
		}

		if (
			string.Equals( name, "C.UTF-8", StringComparison.OrdinalIgnoreCase )
			|| string.Equals( name, "C.utf8", StringComparison.OrdinalIgnoreCase )
		) {
			return new LocaleCategory( name, false, CultureInfo.InvariantCulture );
		}

		var cultureName = NormalizeCultureName( name );
		try {
			return new LocaleCategory(
				name,
				false,
				CultureInfo.GetCultureInfo( cultureName )
			);
		} catch ( CultureNotFoundException ) {
			// GNU grep falls back to the C locale when the requested locale is unavailable.
			return new LocaleCategory( "C", true, null );
		}
	}

	private static string NormalizeCultureName( string localeName ) {
		var end = localeName.Length;
		var dot = localeName.IndexOf( '.' );
		if ( dot >= 0 ) {
			end = Math.Min( end, dot );
		}
		var modifier = localeName.IndexOf( '@' );
		if ( modifier >= 0 ) {
			end = Math.Min( end, modifier );
		}
		return localeName[..end].Replace( '_', '-' );
	}

	private static string? FirstNonempty( params string?[] values ) {
		foreach ( var value in values ) {
			if ( !string.IsNullOrWhiteSpace( value ) ) {
				return value;
			}
		}
		return null;
	}

	private sealed record LocaleCategory( string Name, bool IsByteLocale, CultureInfo? Culture );

	private sealed class CategoryCompositeProvider : IRegularExpressionCharacterClassProvider {
		private readonly IRegularExpressionCharacterClassProvider ctype;
		private readonly IRegularExpressionCharacterClassProvider collate;

		public CategoryCompositeProvider(
			IRegularExpressionCharacterClassProvider ctype,
			IRegularExpressionCharacterClassProvider collate
		) {
			this.ctype = ctype;
			this.collate = collate;
		}

		public bool IsSupportedClass( string className ) => ctype.IsSupportedClass( className );

		public bool IsCharacterClass( Rune value, string className, bool ignoreCase ) =>
			ctype.IsCharacterClass( value, className, ignoreCase );

		public bool IsWordCharacter( Rune value ) => ctype.IsWordCharacter( value );

		public int Compare( Rune left, Rune right, bool ignoreCase ) =>
			collate.Compare( left, right, ignoreCase );

		public bool AreCharactersEqual( Rune left, Rune right, bool ignoreCase ) =>
			ctype.AreCharactersEqual( left, right, ignoreCase );

		public bool AreCollatingElementsEquivalent( Rune left, Rune right, bool ignoreCase ) =>
			collate.AreCollatingElementsEquivalent( left, right, ignoreCase );
	}
}
