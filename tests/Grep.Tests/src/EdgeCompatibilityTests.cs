namespace Icod.Grep.Tests;

using Icod.CommandFramework.Diagnostics;
using Xunit;

/// <summary>Tests T5 edge-compatibility behavior for GNU grep 3.12 parity.</summary>
public sealed class EdgeCompatibilityTests {
	/// <summary>Verifies Windows text mode removes CR from CRLF records before anchored matching.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task WindowsTextModeNormalizesCrLfForAnchorsAndLineRegexp() {
		var path = System.IO.Path.GetTempFileName();
		try {
			await File.WriteAllBytesAsync( path, "alpha\r\nbeta\r\n"u8.ToArray() );
			using var platformMode = PlatformIoContext.EnterWindowsTextModeForTesting();

			var anchored = await RunAsync( [ "^alpha$", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, anchored.Status );
			Assert.Equal( "alpha\n"u8.ToArray(), anchored.Output );

			var wholeLine = await RunAsync( [ "-x", "alpha", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, wholeLine.Status );
			Assert.Equal( "alpha\n"u8.ToArray(), wholeLine.Output );
		} finally {
			File.Delete( path );
		}
	}

	/// <summary>Verifies fixed strings and PCRE observe the same translated Windows record contents.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task WindowsTextModeIsSharedByFixedAndPcreMatchers() {
		var path = System.IO.Path.GetTempFileName();
		try {
			await File.WriteAllBytesAsync( path, "alpha\r\nbeta\r\n"u8.ToArray() );
			using var platformMode = PlatformIoContext.EnterWindowsTextModeForTesting();

			var fixedString = await RunAsync( [ "-Fx", "alpha", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, fixedString.Status );
			Assert.Equal( "alpha\n"u8.ToArray(), fixedString.Output );

			var pcre = await RunAsync( [ "-P", "^alpha$", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, pcre.Status );
			Assert.Equal( "alpha\n"u8.ToArray(), pcre.Output );
		} finally {
			File.Delete( path );
		}
	}

	/// <summary>Verifies Windows default text-mode byte offsets are measured after CRLF translation.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task WindowsTextModeUsesTranslatedByteOffsets() {
		var path = System.IO.Path.GetTempFileName();
		try {
			await File.WriteAllBytesAsync( path, "alpha\r\nbeta\r\n"u8.ToArray() );
			using var platformMode = PlatformIoContext.EnterWindowsTextModeForTesting();
			var result = await RunAsync( [ "-bo", "beta", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, result.Status );
			Assert.Equal( "6:beta\n"u8.ToArray(), result.Output );
		} finally {
			File.Delete( path );
		}
	}

	/// <summary>Verifies Windows text mode handles mixed LF and CRLF input without disturbing lone LF records.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task WindowsTextModeHandlesMixedLineEndings() {
		var path = System.IO.Path.GetTempFileName();
		try {
			await File.WriteAllBytesAsync( path, "a\r\nb\nc\r\n"u8.ToArray() );
			using var platformMode = PlatformIoContext.EnterWindowsTextModeForTesting();
			var result = await RunAsync( [ "-b", "^[bc]$", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, result.Status );
			Assert.Equal( "2:b\n4:c\n"u8.ToArray(), result.Output );
		} finally {
			File.Delete( path );
		}
	}

	/// <summary>Verifies context records are selected and emitted from translated Windows text input.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task WindowsTextModePreservesContextSemantics() {
		var path = System.IO.Path.GetTempFileName();
		try {
			await File.WriteAllBytesAsync( path, "before\r\nhit\r\nafter\r\n"u8.ToArray() );
			using var platformMode = PlatformIoContext.EnterWindowsTextModeForTesting();
			var result = await RunAsync( [ "-C", "1", "hit", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, result.Status );
			Assert.Equal( "before\nhit\nafter\n"u8.ToArray(), result.Output );
		} finally {
			File.Delete( path );
		}
	}

	/// <summary>Verifies text translation still applies to embedded CRLF when NUL is the grep record separator.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task WindowsTextModeTranslatesCrLfInsideNullDataRecords() {
		var path = System.IO.Path.GetTempFileName();
		try {
			await File.WriteAllBytesAsync( path, "a\r\nb\0other\0"u8.ToArray() );
			using var platformMode = PlatformIoContext.EnterWindowsTextModeForTesting();
			var result = await RunAsync( [ "-zP", "(?s)^a\\nb$", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, result.Status );
			Assert.Equal( "a\nb\0"u8.ToArray(), result.Output );
		} finally {
			File.Delete( path );
		}
	}

	/// <summary>Verifies Windows text input treats Control-Z as end-of-file.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task WindowsTextModeHonorsControlZEndOfFile() {
		var path = System.IO.Path.GetTempFileName();
		try {
			await File.WriteAllBytesAsync(
				path,
				[ (byte)'b', (byte)'e', (byte)'f', (byte)'o', (byte)'r', (byte)'e', (byte)'\r', (byte)'\n', 0x1A,
					(byte)'a', (byte)'f', (byte)'t', (byte)'e', (byte)'r', (byte)'\r', (byte)'\n' ]
			);
			using var platformMode = PlatformIoContext.EnterWindowsTextModeForTesting();
			var before = await RunAsync( [ "before", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Success, before.Status );
			Assert.Equal( "before\n"u8.ToArray(), before.Output );

			var after = await RunAsync( [ "after", path ], Array.Empty<byte>() );
			Assert.Equal( CommandExitCodes.Failure, after.Status );
			Assert.Empty( after.Output );
		} finally {
			File.Delete( path );
		}
	}

	/// <summary>Verifies Windows text output expands LF to CRLF.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task WindowsTextOutputExpandsLfToCrLf() {
		using var destination = new MemoryStream();
		await using ( var output = new WindowsTextOutputStream( destination, leaveOpen: true ) ) {
			await output.WriteAsync( "alpha\nbeta\n"u8.ToArray() );
		}
		Assert.Equal( "alpha\r\nbeta\r\n"u8.ToArray(), destination.ToArray() );
	}

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

	/// <summary>Verifies <c>-U</c> does not treat Control-Z as end-of-file.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task BinaryPlatformModePreservesControlZAsData() {
		var source = new byte[] {
			(byte)'b', (byte)'e', (byte)'f', (byte)'o', (byte)'r', (byte)'e', 0x1A,
			(byte)'a', (byte)'f', (byte)'t', (byte)'e', (byte)'r', (byte)'\n'
		};
		var result = await RunAsync( [ "-U", "after" ], source );
		Assert.Equal( CommandExitCodes.Success, result.Status );
		Assert.Equal( source, result.Output );
	}

	/// <summary>Verifies <c>-U</c> is behaviorally neutral for ordinary LF data.</summary>
	/// <returns>A task representing the test.</returns>
	[Fact]
	public async Task BinaryPlatformModeIsNeutralForLfData() {
		var source = "alpha\nbeta\n"u8.ToArray();
		var normal = await RunAsync( [ "beta" ], source );
		var binary = await RunAsync( [ "-U", "beta" ], source );
		Assert.Equal( normal.Status, binary.Status );
		Assert.Equal( normal.Output, binary.Output );
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
