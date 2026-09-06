namespace Icod.Grep.Benchmarks;

using System.Globalization;
using Icod.CommandFramework.Diagnostics;
using Icod.Grep;

/// <summary>Warms one-time command, matcher, and traversal initialization before T6.8 scaling measurements.</summary>
internal static class StressScalingWarmup {
	internal static void Run() {
		RunAsync().GetAwaiter().GetResult();
	}

	private static async Task RunAsync() {
		foreach ( var matcher in new[] { "-F", "-G", "-E", "-P" } ) {
			using var input = new MemoryStream(
				"ordinary payload\nTARGET payload\n"u8.ToArray(),
				writable: false
			);
			using var output = new MemoryStream();
			var result = await RunCommandAsync(
				[ matcher, "-c", "TARGET" ],
				input,
				output
			).ConfigureAwait( false );
			RequireSuccess(
				string.Concat(
					"matcher warm-up ",
					matcher
				),
				result
			);
		}

		using var fixture = new StressFixtureDirectory(
			"scaling-warmup"
		);
		var root = fixture.CreateDirectory( "tree" );
		fixture.WriteBytes(
			"ordinary payload\n"u8.ToArray(),
			"tree",
			"ordinary.txt"
		);
		fixture.WriteBytes(
			"TARGET payload\n"u8.ToArray(),
			"tree",
			"matching.txt"
		);
		using var traversalInput = new MemoryStream(
			Array.Empty<byte>(),
			writable: false
		);
		using var traversalOutput = new MemoryStream();
		var traversalResult = await RunCommandAsync(
			[ "-F", "-r", "-l", "TARGET", root ],
			traversalInput,
			traversalOutput
		).ConfigureAwait( false );
		RequireSuccess(
			"recursive traversal warm-up",
			traversalResult
		);
	}

	private static async Task<(int Status, string Error)> RunCommandAsync(
		string[] arguments,
		Stream input,
		Stream output
	) {
		ArgumentNullException.ThrowIfNull( arguments );
		ArgumentNullException.ThrowIfNull( input );
		ArgumentNullException.ThrowIfNull( output );
		using var error = new StringWriter(
			CultureInfo.InvariantCulture
		);
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			TextWriter.Null,
			error,
			input,
			output
		);
		var status = await Command.RunAsync(
			arguments,
			context
		).ConfigureAwait( false );
		return (
			status,
			error.ToString()
		);
	}

	private static void RequireSuccess(
		string operation,
		(int Status, string Error) result
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( operation );
		if ( CommandExitCodes.Success == result.Status ) {
			return;
		}
		throw new InvalidOperationException(
			string.Concat(
				operation,
				": expected status 0 but received ",
				result.Status.ToString( CultureInfo.InvariantCulture ),
				". Diagnostic: ",
				result.Error
			)
		);
	}
}
