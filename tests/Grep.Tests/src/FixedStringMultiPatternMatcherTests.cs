namespace Icod.Grep.Tests;

using System.Text;
using Xunit;

/// <summary>Tests the immutable fixed-string multi-pattern matcher used by T6.1.</summary>
public sealed class FixedStringMultiPatternMatcherTests {
	/// <summary>Verifies the earliest source location wins regardless of pattern order.</summary>
	[Fact]
	public void SelectsLeftmostMatchAcrossPatternOrder() {
		var matcher = CreateMatcher( "later", "early" );
		var input = Encoding.UTF8.GetBytes( "xxearly---later" );
		var match = matcher.Find( input, 0, CancellationToken.None );
		Assert.True( match.HasValue );
		Assert.Equal( 2, match.Value.Index );
		Assert.Equal( 5, match.Value.Length );
	}

	/// <summary>Verifies the longest match wins when patterns begin at the same byte.</summary>
	[Fact]
	public void SelectsLongestMatchAtSameStart() {
		var matcher = CreateMatcher( "a", "ab", "abc" );
		var input = Encoding.UTF8.GetBytes( "zabc" );
		var match = matcher.Find( input, 0, CancellationToken.None );
		Assert.True( match.HasValue );
		Assert.Equal( 1, match.Value.Index );
		Assert.Equal( 3, match.Value.Length );
	}

	/// <summary>Verifies overlapping patterns are recognized correctly.</summary>
	[Fact]
	public void HandlesOverlappingPatterns() {
		var matcher = CreateMatcher( "aba", "bab" );
		var input = Encoding.UTF8.GetBytes( "xbabax" );
		var match = matcher.Find( input, 0, CancellationToken.None );
		Assert.True( match.HasValue );
		Assert.Equal( 1, match.Value.Index );
		Assert.Equal( 3, match.Value.Length );
	}

	/// <summary>Verifies duplicate patterns do not change the selected span.</summary>
	[Fact]
	public void HandlesDuplicatePatterns() {
		var matcher = CreateMatcher( "needle", "needle", "other" );
		var input = Encoding.UTF8.GetBytes( "xxneedle" );
		var match = matcher.Find( input, 0, CancellationToken.None );
		Assert.True( match.HasValue );
		Assert.Equal( 2, match.Value.Index );
		Assert.Equal( 6, match.Value.Length );
	}

	/// <summary>Verifies a nonzero start offset excludes earlier matches.</summary>
	[Fact]
	public void HonorsStartOffset() {
		var matcher = CreateMatcher( "foo", "bar" );
		var input = Encoding.UTF8.GetBytes( "foo---bar" );
		var match = matcher.Find( input, 3, CancellationToken.None );
		Assert.True( match.HasValue );
		Assert.Equal( 6, match.Value.Index );
		Assert.Equal( 3, match.Value.Length );
	}

	/// <summary>Verifies the final pattern in a large set can be selected.</summary>
	[Fact]
	public void FindsLastPatternInLargeSet() {
		var patterns = Enumerable.Range( 0, 1000 )
			.Select(
				index => Encoding.UTF8.GetBytes(
					string.Concat(
						"NO_MATCH_",
						index.ToString( "D5", System.Globalization.CultureInfo.InvariantCulture )
					)
				)
			)
			.ToList();
		patterns.Add( Encoding.UTF8.GetBytes( "TARGET" ) );
		var matcher = new FixedStringMultiPatternMatcher( patterns );
		var input = Encoding.UTF8.GetBytes( "prefix-TARGET suffix" );
		var match = matcher.Find( input, 0, CancellationToken.None );
		Assert.True( match.HasValue );
		Assert.Equal( 7, match.Value.Index );
		Assert.Equal( 6, match.Value.Length );
	}

	/// <summary>Verifies UTF-8 patterns are matched using exact encoded bytes.</summary>
	[Fact]
	public void MatchesUtf8Patterns() {
		var matcher = CreateMatcher( "世界", "Καλημέρα" );
		var input = Encoding.UTF8.GetBytes( "xxΚαλημέρα 世界" );
		var match = matcher.Find( input, 0, CancellationToken.None );
		Assert.True( match.HasValue );
		Assert.Equal( 2, match.Value.Index );
		Assert.Equal( Encoding.UTF8.GetByteCount( "Καλημέρα" ), match.Value.Length );
	}

	/// <summary>Verifies no-match input returns no span.</summary>
	[Fact]
	public void ReturnsNullWhenNoPatternMatches() {
		var matcher = CreateMatcher( "alpha", "beta" );
		var input = Encoding.UTF8.GetBytes( "gamma" );
		Assert.Null( matcher.Find( input, 0, CancellationToken.None ) );
	}

	/// <summary>Verifies cancellation is honored before a search begins.</summary>
	[Fact]
	public void HonorsCancellation() {
		var matcher = CreateMatcher( "needle" );
		using var source = new CancellationTokenSource();
		source.Cancel();
		Assert.Throws<OperationCanceledException>(
			() => matcher.Find(
				new byte[1024 * 1024],
				0,
				source.Token
			)
		);
	}

	/// <summary>Verifies invalid constructor inputs are rejected.</summary>
	[Fact]
	public void RejectsEmptyPatternSetsAndPatterns() {
		Assert.Throws<ArgumentException>(
			() => new FixedStringMultiPatternMatcher( Array.Empty<byte[]>() )
		);
		Assert.Throws<ArgumentException>(
			() => new FixedStringMultiPatternMatcher(
				new[] { Array.Empty<byte>() }
			)
		);
	}

	private static FixedStringMultiPatternMatcher CreateMatcher( params string[] patterns ) => new(
		patterns.Select( Encoding.UTF8.GetBytes ).ToArray()
	);
}
