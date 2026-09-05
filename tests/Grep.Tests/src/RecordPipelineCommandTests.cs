namespace Icod.Grep.Tests;

using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Tests T6.3 record-pipeline semantics around retained record storage.</summary>
public sealed class RecordPipelineCommandTests {
	/// <summary>Verifies before-context records remain valid after later records have been read.</summary>
	[Fact]
	public async Task RetainsBeforeContextAcrossSubsequentReads() {
		var result = await RunAsync(
			[ "-B", "2", "hit" ],
			"one\ntwo\nhit\nfour\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "one\ntwo\nhit\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies after-context records remain valid while the reader advances.</summary>
	[Fact]
	public async Task RetainsAfterContextAcrossSubsequentReads() {
		var result = await RunAsync(
			[ "-A", "2", "hit" ],
			"one\nhit\nthree\nfour\nfive\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "hit\nthree\nfour\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies count-only matching remains correct for a very large materialized record.</summary>
	[Fact]
	public async Task CountOnlyHandlesVeryLargeRecord() {
		var input = Enumerable.Repeat( (byte)'a', 262_144 ).ToArray();
		"TARGET"u8.CopyTo( input.AsSpan( input.Length - 6 ) );
		var terminated = new byte[input.Length + 1];
		input.CopyTo( terminated, 0 );
		terminated[^1] = (byte)'\n';
		var result = await RunAsync(
			[ "-c", "TARGET" ],
			terminated
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "1\n"u8.ToArray(), result.Output );
	}

	/// <summary>Verifies only-matching output retains exact source bytes and offsets.</summary>
	[Fact]
	public async Task OnlyMatchingPreservesReaderOwnedRecordBytes() {
		var result = await RunAsync(
			[ "-bo", "TARGET" ],
			"prefix-TARGET-suffix\n"u8.ToArray()
		);
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( "7:TARGET\n"u8.ToArray(), result.Output );
	}

	private static async Task<(int Status, byte[] Output, string Error)> RunAsync(
		string[] args,
		byte[] input
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( input );
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
}
