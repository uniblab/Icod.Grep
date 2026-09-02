namespace Icod.Grep.Tests;

using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Tests the documented GNU/POSIX collation boundary inherited from the shared regular-expression provider.</summary>
public sealed class CollationCompatibilityTests {
	/// <summary>Verifies supported single-scalar collating symbols and equivalence classes remain matchable.</summary>
	/// <returns>A task representing the test.</returns>
	[Theory]
	[InlineData( "[[.a.]]" )]
	[InlineData( "[[=a=]]" )]
	public async Task SingleScalarCollatingElementsRemainSupported( string pattern ) {
		var result = await RunAsync( [ pattern ], "a\nb\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "a\n"u8.ToArray(), result.Output );
		Assert.Empty( result.Error );
	}

	/// <summary>Verifies unresolved multi-scalar locale elements fail explicitly instead of receiving guessed semantics.</summary>
	/// <returns>A task representing the test.</returns>
	[Theory]
	[InlineData( "[[.ch.]]" )]
	[InlineData( "[[=ch=]]" )]
	public async Task MultiScalarCollatingElementsProduceStableDiagnostic( string pattern ) {
		var result = await RunAsync( [ pattern ], "ch\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.UsageError, result.Status );
		Assert.Empty( result.Output );
		Assert.Contains(
			"multi-scalar collating elements are not supported by the configured provider",
			result.Error,
			StringComparison.Ordinal
		);
	}

	/// <summary>Verifies ERE uses the same controlled multi-scalar collation boundary as BRE.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task ExtendedRegexpSharesMultiScalarCollationDiagnostic() {
		var result = await RunAsync( [ "-E", "[[.ch.]]" ], "ch\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.UsageError, result.Status );
		Assert.Contains(
			"multi-scalar collating elements are not supported by the configured provider",
			result.Error,
			StringComparison.Ordinal
		);
	}

	private static async Task<(int Status, byte[] Output, string Error)> RunAsync(
		string[] args,
		byte[] input
	) {
		using var inputStream = new MemoryStream( input, writable: false );
		using var outputStream = new MemoryStream();
		var error = new StringWriter();
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			new StringWriter(),
			error,
			inputStream,
			outputStream
		);
		var status = await Command.RunAsync( args, context );
		return ( status, outputStream.ToArray(), error.ToString() );
	}
}
