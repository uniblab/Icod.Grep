namespace Icod.Grep.Benchmarks;

using System.Globalization;
using Icod.CommandFramework.Diagnostics;
using Icod.Grep;

/// <summary>Provides a fast, deterministic cross-platform benchmark smoke.</summary>
internal static class BenchmarkSmoke {
	internal static int Run() {
		try {
			foreach ( var configured in ScenarioCatalog.All ) {
				var scenario = configured.ToSmokeScenario();
				var corpus = CorpusFactory.Create( scenario );
				var result = RunCommand(
					corpus.Arguments,
					corpus.Input
				);
				if ( CommandExitCodes.Success != result.Status ) {
					throw new InvalidOperationException(
						string.Concat(
							scenario.Name,
							": expected status 0 but received ",
							result.Status.ToString( CultureInfo.InvariantCulture ),
							". Diagnostic: ",
							result.Error
						)
					);
				}
				var expected = string.Concat(
					corpus.ExpectedMatchCount.ToString( CultureInfo.InvariantCulture ),
					Environment.NewLine
				);
				var actual = System.Text.Encoding.UTF8.GetString(
					result.Output
				).ReplaceLineEndings( Environment.NewLine );
				if ( !string.Equals( expected, actual, StringComparison.Ordinal ) ) {
					throw new InvalidOperationException(
						string.Concat(
							scenario.Name,
							": expected count output '",
							expected.Trim(),
							"' but received '",
							actual.Trim(),
							"'."
					)
				);
				}
			}

			ValidateUtf8PcrePropertyWorkload();

			Console.WriteLine(
				string.Concat(
					"T6 benchmark smoke passed for ",
					ScenarioCatalog.All.Count.ToString( CultureInfo.InvariantCulture ),
					" scenarios plus the UTF-8 PCRE property control."
				)
			);
			return 0;
		} catch ( Exception exception ) {
			Console.Error.WriteLine( exception.Message );
			return 1;
		}
	}

	private static void ValidateUtf8PcrePropertyWorkload() {
		var previousLcAll = Environment.GetEnvironmentVariable( "LC_ALL" );
		try {
			Environment.SetEnvironmentVariable( "LC_ALL", "C.UTF-8" );
			var result = RunCommand(
				[ "-P", "-c", "\\p{L}+ 世界" ],
				"Καλημέρα 世界 TARGET payload\nΚαλημέρα ordinary payload\n"u8.ToArray()
			);
			if ( CommandExitCodes.Success != result.Status ) {
				throw new InvalidOperationException(
					string.Concat(
						"UTF-8 PCRE property smoke failed with status ",
						result.Status.ToString( CultureInfo.InvariantCulture ),
						". Diagnostic: ",
						result.Error
					)
				);
			}
			var actual = System.Text.Encoding.UTF8.GetString(
				result.Output
			).ReplaceLineEndings( Environment.NewLine );
			var expected = string.Concat(
				"1",
				Environment.NewLine
			);
			if ( !string.Equals( expected, actual, StringComparison.Ordinal ) ) {
				throw new InvalidOperationException(
					string.Concat(
						"UTF-8 PCRE property smoke expected count '1' but received '",
						actual.Trim(),
						"'."
					)
				);
			}
		} finally {
			Environment.SetEnvironmentVariable(
				"LC_ALL",
				previousLcAll
			);
		}
	}

	private static (int Status, byte[] Output, string Error) RunCommand(
		string[] arguments,
		byte[] input
	) {
		using var inputStream = new MemoryStream(
			input,
			writable: false
		);
		using var outputStream = new MemoryStream();
		using var error = new StringWriter(
			CultureInfo.InvariantCulture
		);
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			TextWriter.Null,
			error,
			inputStream,
			outputStream
		);
		var status = Command.RunAsync(
			arguments,
			context
		).GetAwaiter().GetResult();
		return (
			status,
			outputStream.ToArray(),
			error.ToString()
		);
	}
}
