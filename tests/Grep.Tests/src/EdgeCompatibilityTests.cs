namespace Icod.Grep.Tests;

using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Tests T5 edge-compatibility behavior for GNU grep 3.12 parity.</summary>
public sealed class EdgeCompatibilityTests {
	/// <summary>Verifies <c>-U</c> preserves CRLF record bytes for anchored matching.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task BinaryPlatformModePreservesCrLfForAnchoredMatching() {
		var source = "alpha\r\nbeta\r\n"u8.ToArray();
		var result = await RunAsync( [ "-U", "^alpha\r$" ], source );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "alpha\r\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies <c>-U</c> byte offsets are measured against the unmodified CRLF byte stream.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task BinaryPlatformModeUsesRawCrLfByteOffsets() {
		var source = "alpha\r\nbeta\r\n"u8.ToArray();
		var result = await RunAsync( [ "-Ubo", "beta" ], source );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "7:beta\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies the current shared-engine boundary for multi-scalar collating elements remains a controlled diagnostic.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task MultiScalarCollatingElementProducesControlledDiagnostic() {
		var result = await RunAsync( [ "[[.ch.]]" ], "ch\n"u8.ToArray() );
		Assert.Equal( CommandExitCodes.UsageError, result.Status );
		Assert.NotEmpty( result.Error );
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
