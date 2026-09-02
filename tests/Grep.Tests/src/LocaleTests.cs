namespace Icod.Grep.Tests;

using System.Text;
using Icod.CommandFramework.Diagnostics;
using Icod.CommandFramework.Text;
using Xunit;

/// <summary>Tests GNU grep locale precedence, byte-mode matching, and encoding-error behavior.</summary>
public sealed class LocaleTests {
	[Fact]
	public void ResolvesCtypeAndCollateCategoriesIndependently() {
		var profile = GrepLocaleProfile.Resolve(
			null,
			"C",
			"en_US.UTF-8",
			"en_US.UTF-8"
		);
		Assert.Equal( "C", profile.CtypeName );
		Assert.Equal( "en_US.UTF-8", profile.CollateName );
		Assert.Equal( TextDecodingMode.Bytes, profile.DecodingMode );
	}

	[Fact]
	public void LcAllOverridesCategoryVariables() {
		var profile = GrepLocaleProfile.Resolve(
			"C",
			"en_US.UTF-8",
			"en_US.UTF-8",
			"en_US.UTF-8"
		);
		Assert.Equal( "C", profile.CtypeName );
		Assert.Equal( "C", profile.CollateName );
		Assert.Equal( TextDecodingMode.Bytes, profile.DecodingMode );
	}

	[Fact]
	public async Task CLocaleTreatsEveryByteAsACharacter() {
		using var environment = new LocaleEnvironmentScope( "C", null, null, null );
		var result = await RunAsync( [ "." ], [ 0xE9, (byte)'\n' ] );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( new byte[] { 0xE9, (byte)'\n' }, result.Output );
	}

	[Fact]
	public async Task CLocaleCommandLinePatternsPreserveArgumentBytes() {
		using var environment = new LocaleEnvironmentScope( "C", null, null, null );
		var input = Encoding.UTF8.GetBytes( "é\n" );
		var result = await RunAsync( [ "-F", "é" ], input );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( input, result.Output );
	}

	[Fact]
	public async Task CLocalePatternFilesPreserveNonAsciiBytes() {
		using var environment = new LocaleEnvironmentScope( "C", null, null, null );
		var directory = CreateTemporaryDirectory();
		try {
			var patternFile = System.IO.Path.Combine( directory, "patterns" );
			await File.WriteAllBytesAsync( patternFile, [ 0xE9, (byte)'\n' ] );
			var result = await RunAsync( [ "-F", "-f", patternFile ], [ 0xE9, (byte)'\n' ] );
			Assert.Equal( CommandExitCodes.Success, result.Status );
			Assert.Equal( new byte[] { 0xE9, (byte)'\n' }, result.Output );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	[Fact]
	public async Task Utf8LocaleUsesUnicodeCharacterClasses() {
		using var environment = new LocaleEnvironmentScope( "en_US.UTF-8", null, null, null );
		var input = Encoding.UTF8.GetBytes( "é\n" );
		var result = await RunAsync( [ "[[:alpha:]]" ], input );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( input, result.Output );
	}

	[Fact]
	public async Task CLocaleCharacterClassesRemainAscii() {
		using var environment = new LocaleEnvironmentScope( "C", null, null, null );
		var input = Encoding.UTF8.GetBytes( "é\n" );
		var result = await RunAsync( [ "[[:alpha:]]" ], input );
		Assert.Equal( CommandExitCodes.Failure, result.Status );
		Assert.Empty( result.Output );
	}

	[Fact]
	public async Task Utf8EncodingErrorsSuppressOnlyUnsafeSelectedRecords() {
		using var environment = new LocaleEnvironmentScope( "en_US.UTF-8", null, null, null );
		var input = BuildEncodingErrorInput();
		var result = await RunAsync( [ "." ], input );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "Alfred Jones\nJohn Smith\n"u8.ToArray(), result.Output );
	}

	[Fact]
	public async Task WithoutMatchStillSearchesValidRecordsAroundEncodingErrors() {
		using var environment = new LocaleEnvironmentScope( "en_US.UTF-8", null, null, null );
		var result = await RunAsync( [ "-I", "." ], BuildEncodingErrorInput() );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "Alfred Jones\nJohn Smith\n"u8.ToArray(), result.Output );
		Assert.Empty( result.Error );
	}

	[Fact]
	public async Task TextModeOutputsMalformedUtf8BytesExactly() {
		using var environment = new LocaleEnvironmentScope( "en_US.UTF-8", null, null, null );
		var input = BuildEncodingErrorInput();
		var result = await RunAsync( [ "-a", "." ], input );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( input, result.Output );
	}

	[Fact]
	public async Task MatchOnMalformedRecordAffectsStatusWithoutUnsafeOutput() {
		using var environment = new LocaleEnvironmentScope( "en_US.UTF-8", null, null, null );
		var result = await RunAsync( [ "^Pedro" ], BuildEncodingErrorInput() );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Empty( result.Output );
	}

	private static byte[] BuildEncodingErrorInput() => [
		.. "Alfred Jones\n"u8.ToArray(),
		.. "Pedro P"u8.ToArray(),
		0xE9,
		.. "rez\nJohn Smith\n"u8.ToArray()
	];

	private static async Task<(int Status, byte[] Output, string Error)> RunAsync(
		string[] args,
		byte[] input
	) {
		using var inputStream = new MemoryStream( input, writable: false );
		using var outputStream = new MemoryStream();
		var textOutput = new StringWriter();
		var error = new StringWriter();
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			textOutput,
			error,
			inputStream,
			outputStream
		);
		var status = await Command.RunAsync( args, context );
		return ( status, outputStream.ToArray(), error.ToString() );
	}

	private static string CreateTemporaryDirectory() {
		var path = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			string.Concat( "Icod.Grep.LocaleTests-", Guid.NewGuid().ToString( "N" ) )
		);
		Directory.CreateDirectory( path );
		return path;
	}

	private sealed class LocaleEnvironmentScope : IDisposable {
		private readonly Dictionary<string, string?> previous = new( StringComparer.Ordinal );

		public LocaleEnvironmentScope(
			string? lcAll,
			string? lcCtype,
			string? lcCollate,
			string? lang
		) {
			Set( "LC_ALL", lcAll );
			Set( "LC_CTYPE", lcCtype );
			Set( "LC_COLLATE", lcCollate );
			Set( "LANG", lang );
		}

		public void Dispose() {
			foreach ( var pair in previous ) {
				Environment.SetEnvironmentVariable( pair.Key, pair.Value );
			}
		}

		private void Set( string name, string? value ) {
			previous[name] = Environment.GetEnvironmentVariable( name );
			Environment.SetEnvironmentVariable( name, value );
		}
	}
}
