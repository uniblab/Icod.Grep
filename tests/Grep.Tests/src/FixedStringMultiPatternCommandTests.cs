namespace Icod.Grep.Tests;

using System.Text;
using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Tests command-level semantics for fixed-string multi-pattern matching.</summary>
public sealed class FixedStringMultiPatternCommandTests {
	/// <summary>Verifies the earliest fixed-string match wins regardless of pattern-source order.</summary>
	[Fact]
	public async Task SelectsEarliestFixedStringAcrossPatternOrder() {
		var result = await RunAsync(
			[ "-F", "-o", "-e", "later", "-e", "early" ],
			"xxearly---later\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "early\nlater\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies same-start fixed patterns use GNU leftmost-longest selection.</summary>
	[Fact]
	public async Task SelectsLongestFixedStringAtSameStart() {
		var result = await RunAsync(
			[ "-F", "-o", "-e", "a", "-e", "ab", "-e", "abc" ],
			"zabc\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "abc\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies overlapping fixed patterns are enumerated from successive start offsets.</summary>
	[Fact]
	public async Task EnumeratesOverlappingCandidateSetWithoutOverlappingOutput() {
		var result = await RunAsync(
			[ "-F", "-o", "-e", "aba", "-e", "bab" ],
			"xbabax\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "bab\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies duplicate fixed patterns do not duplicate only-matching output.</summary>
	[Fact]
	public async Task DuplicateFixedPatternsDoNotDuplicateMatches() {
		var result = await RunAsync(
			[ "-F", "-o", "-e", "needle", "-e", "needle" ],
			"needle\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "needle\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies a large fixed pattern set can select the final pattern.</summary>
	[Fact]
	public async Task FindsLastPatternInLargeFixedSet() {
		var arguments = new List<string> { "-F", "-c" };
		for ( var index = 0; 1000 > index; index++ ) {
			arguments.Add( "-e" );
			arguments.Add(
				string.Concat(
					"NO_MATCH_",
					index.ToString( "D5", System.Globalization.CultureInfo.InvariantCulture )
				)
			);
		}
		arguments.Add( "-e" );
		arguments.Add( "TARGET" );
		var result = await RunAsync(
			arguments.ToArray(),
			"prefix-TARGET suffix\nordinary-data\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "1\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies UTF-8 fixed strings preserve exact byte offsets and content.</summary>
	[Fact]
	public async Task MatchesUtf8FixedPatterns() {
		var result = await RunAsync(
			[ "-F", "-bo", "-e", "世界", "-e", "Καλημέρα" ],
			Encoding.UTF8.GetBytes( "xxΚαλημέρα 世界\n" )
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal(
			Encoding.UTF8.GetBytes( "2:Καλημέρα\n19:世界\n" ),
			result.Output
		);
	}

	/// <summary>Verifies empty fixed patterns retain the existing every-record behavior.</summary>
	[Fact]
	public async Task EmptyFixedPatternFallsBackToExistingSemantics() {
		var result = await RunAsync(
			[ "-F", "-e", string.Empty, "-e", "missing" ],
			"alpha\nbeta\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "alpha\nbeta\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies case-insensitive fixed matching remains locale-aware fallback behavior.</summary>
	[Fact]
	public async Task IgnoreCaseFixedPatternsRetainFallbackSemantics() {
		var result = await RunAsync(
			[ "-F", "-i", "-e", "cat", "-e", "dog" ],
			"CAT\nDog\nfox\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "CAT\nDog\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies word and line fixed matching remain on their existing boundary-aware paths.</summary>
	[Fact]
	public async Task WordAndLineFixedPatternsRetainFallbackSemantics() {
		var words = await RunAsync(
			[ "-F", "-w", "-e", "cat", "-e", "dog" ],
			"cat!\nscatter\ndog\n"u8.ToArray()
		);
		var lines = await RunAsync(
			[ "-F", "-x", "-e", "cat", "-e", "dog" ],
			"cat\ncat!\ndog\n"u8.ToArray()
		);
		Assert.Equal( "cat!\ndog\n"u8.ToArray(), words.Output );
		Assert.Equal( "cat\ndog\n"u8.ToArray(), lines.Output );
	}

	private static async Task<(int Status, byte[] Output, string Error)> RunAsync(
		string[] args,
		byte[] input,
		CancellationToken cancellationToken = default
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
			outputStream,
			null,
			cancellationToken
		);
		var status = await Command.RunAsync( args, context );
		return ( status, outputStream.ToArray(), error.ToString() );
	}
}
