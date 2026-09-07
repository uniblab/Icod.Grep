namespace Icod.Grep.Benchmarks;

using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Icod.CommandFramework.Diagnostics;
using Icod.Grep;

/// <summary>Runs sustained T6.8 cancellation and output resilience scenarios.</summary>
internal static class StressResilienceHarness {
	private const int Megabyte = 1024 * 1024;
	private const int CancellationDelayMilliseconds = 50;
	private const int CancellationDeadlineMilliseconds = 2000;

	internal static int Run( string? outputPath ) {
		try {
			return RunAsync( outputPath ).GetAwaiter().GetResult();
		} catch ( Exception exception ) {
			Console.Error.WriteLine( exception.Message );
			return 1;
		}
	}

	private static async Task<int> RunAsync( string? outputPath ) {
		var scenarios = CreateScenarios();
		var results = new List<StressScenarioResult>( scenarios.Count );
		Console.WriteLine(
			string.Concat(
				"T6.8 resilience profile contains ",
				scenarios.Count.ToString( CultureInfo.InvariantCulture ),
				" scenarios. Fixture generation is intentionally outside measured command time."
			)
		);

		for ( var index = 0; index < scenarios.Count; index++ ) {
			var scenario = scenarios[index];
			Console.WriteLine(
				string.Concat(
					"Preparing ",
					(index + 1).ToString( CultureInfo.InvariantCulture ),
					"/",
					scenarios.Count.ToString( CultureInfo.InvariantCulture ),
					": ",
					scenario.Name
				)
			);
			StressScenarioResult result;
			try {
				result = await RunScenarioAsync( scenario ).ConfigureAwait( false );
			} catch ( Exception exception ) {
				result = CreatePreparationFailureResult(
					scenario,
					exception
				);
			}
			results.Add( result );
			Console.WriteLine(
				string.Concat(
					result.Succeeded ? "Completed " : "FAILED ",
					scenario.Name,
					" in ",
					result.ElapsedMilliseconds.ToString(
						"N1",
						CultureInfo.InvariantCulture
					),
					" ms; allocated ",
					result.AllocatedBytes.ToString(
						"N0",
						CultureInfo.InvariantCulture
					),
					" bytes."
				)
			);
		}

		var allSucceeded = results.All(
			static result => result.Succeeded
		);
		var report = new StressReport(
			1,
			"resilience",
			DateTimeOffset.UtcNow,
			Environment.GetEnvironmentVariable( "ICOD_BENCHMARK_COMMIT" ) ?? TryReadGitCommit(),
			typeof( Command ).Assembly.GetName().Version?.ToString(),
			RuntimeInformation.OSDescription,
			RuntimeInformation.OSArchitecture.ToString(),
			RuntimeInformation.ProcessArchitecture.ToString(),
			RuntimeInformation.FrameworkDescription,
			Environment.ProcessorCount,
			null,
			GCSettings.IsServerGC,
			GCSettings.LatencyMode.ToString(),
			GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
			TryGetHardwareInventorySha256(),
			allSucceeded,
			results
		);
		WriteReport(
			report,
			outputPath
		);
		return allSucceeded ? 0 : 1;
	}

	private static IReadOnlyList<StressScenario> CreateScenarios() {
		return [
			new StressScenario(
				"S4-large-record-cancellation",
				"S4",
				new Dictionary<string, string> {
					[ "recordMiB" ] = "64",
					[ "matcher" ] = "basic",
					[ "cancelAfterMilliseconds" ] = CancellationDelayMilliseconds.ToString( CultureInfo.InvariantCulture ),
					[ "deadlineMilliseconds" ] = CancellationDeadlineMilliseconds.ToString( CultureInfo.InvariantCulture )
				},
				PrepareLargeRecordCancellationScenario
			),
			new StressScenario(
				"S4-pattern-set-cancellation",
				"S4",
				new Dictionary<string, string> {
					[ "patterns" ] = "1000",
					[ "matcher" ] = "basic",
					[ "inputMiB" ] = "8",
					[ "cancelAfterMilliseconds" ] = CancellationDelayMilliseconds.ToString( CultureInfo.InvariantCulture ),
					[ "deadlineMilliseconds" ] = CancellationDeadlineMilliseconds.ToString( CultureInfo.InvariantCulture )
				},
				PreparePatternCancellationScenario
			),
			new StressScenario(
				"S4-recursive-traversal-cancellation",
				"S4",
				new Dictionary<string, string> {
					[ "files" ] = "10000",
					[ "directories" ] = "100",
					[ "cancelAfterMilliseconds" ] = CancellationDelayMilliseconds.ToString( CultureInfo.InvariantCulture ),
					[ "deadlineMilliseconds" ] = CancellationDeadlineMilliseconds.ToString( CultureInfo.InvariantCulture )
				},
				PrepareTraversalCancellationScenario
			),
			new StressScenario(
				"S4-output-heavy-cancellation",
				"S4",
				new Dictionary<string, string> {
					[ "records" ] = "100000",
					[ "delayEveryWrites" ] = "32",
					[ "delayMilliseconds" ] = "1",
					[ "cancelAfterMilliseconds" ] = CancellationDelayMilliseconds.ToString( CultureInfo.InvariantCulture ),
					[ "deadlineMilliseconds" ] = CancellationDeadlineMilliseconds.ToString( CultureInfo.InvariantCulture )
				},
				PrepareOutputCancellationScenario
			),
			new StressScenario(
				"S5-sustained-backpressure",
				"S5",
				new Dictionary<string, string> {
					[ "records" ] = "20000",
					[ "delayEveryWrites" ] = "64",
					[ "delayMilliseconds" ] = "1",
					[ "retainedOutputBytes" ] = "0"
				},
				PrepareBackpressureScenario
			),
			new StressScenario(
				"S5-sustained-write-failure",
				"S5",
				new Dictionary<string, string> {
					[ "records" ] = "20000",
					[ "failAfterBytes" ] = "65536",
					[ "failurePoint" ] = "write"
				},
				PrepareWriteFailureScenario
			),
			new StressScenario(
				"S5-sustained-flush-failure",
				"S5",
				new Dictionary<string, string> {
					[ "records" ] = "2000",
					[ "failurePoint" ] = "flush"
				},
				PrepareFlushFailureScenario
			)
		];
	}

	private static StressScenarioExecution PrepareLargeRecordCancellationScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		var path = fixture.GetPath( "large-record.txt" );
		using ( var stream = new FileStream( path, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) {
			var chunk = new byte[Megabyte];
			chunk.AsSpan().Fill( (byte)'a' );
			for ( var index = 0; index < 64; index++ ) {
				stream.Write( chunk );
			}
			stream.WriteByte( (byte)'\n' );
		}
		var input = new MemoryStream( Array.Empty<byte>(), writable: false );
		var output = new MemoryStream();
		var cancellation = new CancellationTokenSource();
		return new StressScenarioExecution(
			async () => {
				cancellation.CancelAfter( CancellationDelayMilliseconds );
				var stopwatch = Stopwatch.StartNew();
				var result = await RunCommandAsync(
					[ "-G", "-c", "TARGET", path ],
					input,
					output,
					cancellation.Token
				).ConfigureAwait( false );
				stopwatch.Stop();
				RequireCanceledWithinDeadline(
					"S4-large-record-cancellation",
					result,
					stopwatch.ElapsedMilliseconds
				);
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					string.Concat(
						"Canceled in ",
						stopwatch.ElapsedMilliseconds.ToString( CultureInfo.InvariantCulture ),
						" ms"
					)
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
				cancellation.Dispose();
			}
		);
	}

	private static StressScenarioExecution PreparePatternCancellationScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		_ = fixture;
		var arguments = new List<string> {
			"-G",
			"-c"
		};
		for ( var index = 0; index < 1000; index++ ) {
			arguments.Add( "-e" );
			arguments.Add(
				string.Concat(
					"NO_MATCH_",
					index.ToString( "D4", CultureInfo.InvariantCulture )
				)
			);
		}
		var content = CreateRepeatedRecords(
			8 * Megabyte / 64,
			"ordinary payload ordinary payload ordinary payload ordinary payload\n"
		);
		var input = new MemoryStream( content, writable: false );
		var output = new MemoryStream();
		var cancellation = new CancellationTokenSource();
		return new StressScenarioExecution(
			async () => {
				cancellation.CancelAfter( CancellationDelayMilliseconds );
				var stopwatch = Stopwatch.StartNew();
				var result = await RunCommandAsync(
					arguments.ToArray(),
					input,
					output,
					cancellation.Token
				).ConfigureAwait( false );
				stopwatch.Stop();
				RequireCanceledWithinDeadline(
					"S4-pattern-set-cancellation",
					result,
					stopwatch.ElapsedMilliseconds
				);
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					string.Concat(
						"Canceled in ",
						stopwatch.ElapsedMilliseconds.ToString( CultureInfo.InvariantCulture ),
						" ms"
					)
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
				cancellation.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareTraversalCancellationScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		var root = fixture.CreateDirectory( "tree" );
		var content = "ordinary payload\n"u8.ToArray();
		for ( var index = 0; index < 10000; index++ ) {
			var directory = System.IO.Path.Combine(
				root,
				string.Concat(
					"d",
					(index % 100).ToString( "D3", CultureInfo.InvariantCulture )
				)
			);
			Directory.CreateDirectory( directory );
			File.WriteAllBytes(
				System.IO.Path.Combine(
					directory,
					string.Concat(
						"f-",
						index.ToString( "D5", CultureInfo.InvariantCulture ),
						".txt"
					)
				),
				content
			);
		}
		var input = new MemoryStream( Array.Empty<byte>(), writable: false );
		var output = new MemoryStream();
		var cancellation = new CancellationTokenSource();
		return new StressScenarioExecution(
			async () => {
				cancellation.CancelAfter( CancellationDelayMilliseconds );
				var stopwatch = Stopwatch.StartNew();
				var result = await RunCommandAsync(
					[ "-F", "-r", "-l", "TARGET", root ],
					input,
					output,
					cancellation.Token
				).ConfigureAwait( false );
				stopwatch.Stop();
				RequireCanceledWithinDeadline(
					"S4-recursive-traversal-cancellation",
					result,
					stopwatch.ElapsedMilliseconds
				);
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					string.Concat(
						"Canceled in ",
						stopwatch.ElapsedMilliseconds.ToString( CultureInfo.InvariantCulture ),
						" ms"
					)
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
				cancellation.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareOutputCancellationScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		_ = fixture;
		var content = CreateRepeatedRecords( 100000, "TARGET payload\n" );
		var input = new MemoryStream( content, writable: false );
		var output = new ThrottledCountingWriteStream( 32, 1 );
		var cancellation = new CancellationTokenSource();
		return new StressScenarioExecution(
			async () => {
				cancellation.CancelAfter( CancellationDelayMilliseconds );
				var stopwatch = Stopwatch.StartNew();
				var result = await RunCommandAsync(
					[ "-F", "TARGET" ],
					input,
					output,
					cancellation.Token
				).ConfigureAwait( false );
				stopwatch.Stop();
				RequireCanceledWithinDeadline(
					"S4-output-heavy-cancellation",
					result,
					stopwatch.ElapsedMilliseconds
				);
				return new StressScenarioOutcome(
					result.Status,
					output.AcceptedBytes,
					string.Concat(
						"Canceled in ",
						stopwatch.ElapsedMilliseconds.ToString( CultureInfo.InvariantCulture ),
						" ms after ",
						output.AcceptedBytes.ToString( CultureInfo.InvariantCulture ),
						" output bytes"
					)
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
				cancellation.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareBackpressureScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		_ = fixture;
		var content = CreateRepeatedRecords( 20000, "TARGET payload\n" );
		var input = new MemoryStream( content, writable: false );
		var output = new ThrottledCountingWriteStream( 64, 1 );
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-F", "TARGET" ],
					input,
					output
				).ConfigureAwait( false );
				RequireStatus(
					"S5-sustained-backpressure",
					CommandExitCodes.Success,
					result.Status,
					result.Error
				);
				if ( content.LongLength != output.AcceptedBytes ) {
					throw new InvalidOperationException(
						string.Concat(
							"S5-sustained-backpressure: expected ",
							content.LongLength.ToString( CultureInfo.InvariantCulture ),
							" output bytes but accepted ",
							output.AcceptedBytes.ToString( CultureInfo.InvariantCulture ),
							"."
						)
					);
				}
				return new StressScenarioOutcome(
					result.Status,
					output.AcceptedBytes,
					string.Concat(
						"Bounded counting sink accepted all output across ",
						output.WriteCount.ToString( CultureInfo.InvariantCulture ),
						" writes; maximum single write ",
						output.MaximumWriteBytes.ToString( CultureInfo.InvariantCulture ),
						" bytes"
					)
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareWriteFailureScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		_ = fixture;
		var content = CreateRepeatedRecords( 20000, "TARGET payload\n" );
		var input = new MemoryStream( content, writable: false );
		var output = new FailingWriteStream( 65536, failOnFlush: false );
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-F", "TARGET" ],
					input,
					output
				).ConfigureAwait( false );
				RequireOutputFailure(
					"S5-sustained-write-failure",
					result
				);
				return new StressScenarioOutcome(
					result.Status,
					output.AcceptedBytes,
					"Write failure remained a controlled grep error"
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareFlushFailureScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		_ = fixture;
		var content = CreateRepeatedRecords( 2000, "TARGET payload\n" );
		var input = new MemoryStream( content, writable: false );
		var output = new FailingWriteStream( long.MaxValue, failOnFlush: true );
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-F", "TARGET" ],
					input,
					output
				).ConfigureAwait( false );
				RequireOutputFailure(
					"S5-sustained-flush-failure",
					result
				);
				return new StressScenarioOutcome(
					result.Status,
					output.AcceptedBytes,
					"Flush failure remained a controlled grep error"
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static async Task<StressScenarioResult> RunScenarioAsync(
		StressScenario scenario
	) {
		ArgumentNullException.ThrowIfNull( scenario );
		using var fixture = new StressFixtureDirectory( scenario.Name );
		using var execution = scenario.Prepare( fixture );
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		using var process = Process.GetCurrentProcess();
		process.Refresh();
		var allocatedBefore = GC.GetTotalAllocatedBytes( precise: true );
		var gen0Before = GC.CollectionCount( 0 );
		var gen1Before = GC.CollectionCount( 1 );
		var gen2Before = GC.CollectionCount( 2 );
		var workingSetBefore = process.WorkingSet64;
		var stopwatch = Stopwatch.StartNew();
		StressScenarioOutcome? outcome = null;
		Exception? failure = null;
		try {
			outcome = await execution.ExecuteAsync().ConfigureAwait( false );
		} catch ( Exception exception ) {
			failure = exception;
		} finally {
			stopwatch.Stop();
		}
		var allocatedAfter = GC.GetTotalAllocatedBytes( precise: true );
		process.Refresh();
		return new StressScenarioResult(
			scenario.Name,
			scenario.Dimension,
			scenario.Parameters,
			null == failure,
			outcome?.Status,
			outcome?.Detail,
			failure?.ToString(),
			stopwatch.Elapsed.TotalMilliseconds,
			Math.Max( 0, allocatedAfter - allocatedBefore ),
			GC.CollectionCount( 0 ) - gen0Before,
			GC.CollectionCount( 1 ) - gen1Before,
			GC.CollectionCount( 2 ) - gen2Before,
			workingSetBefore,
			process.WorkingSet64,
			process.PeakWorkingSet64,
			outcome?.OutputBytes ?? 0
		);
	}

	private static StressScenarioResult CreatePreparationFailureResult(
		StressScenario scenario,
		Exception exception
	) {
		ArgumentNullException.ThrowIfNull( scenario );
		ArgumentNullException.ThrowIfNull( exception );
		return new StressScenarioResult(
			scenario.Name,
			scenario.Dimension,
			scenario.Parameters,
			false,
			null,
			null,
			exception.ToString(),
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0
		);
	}

	private static async Task<(int Status, string Error)> RunCommandAsync(
		string[] arguments,
		Stream input,
		Stream output,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( arguments );
		ArgumentNullException.ThrowIfNull( input );
		ArgumentNullException.ThrowIfNull( output );
		using var error = new StringWriter( CultureInfo.InvariantCulture );
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			TextWriter.Null,
			error,
			input,
			output,
			null,
			cancellationToken
		);
		var status = await Command.RunAsync(
			arguments,
			context
		).ConfigureAwait( false );
		return ( status, error.ToString() );
	}

	private static byte[] CreateRepeatedRecords(
		int count,
		string record
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( record );
		if ( count < 0 ) {
			throw new ArgumentOutOfRangeException( nameof( count ) );
		}
		var bytes = Encoding.UTF8.GetBytes( record );
		using var memory = new MemoryStream();
		for ( var index = 0; index < count; index++ ) {
			memory.Write( bytes );
		}
		return memory.ToArray();
	}

	private static void RequireCanceledWithinDeadline(
		string scenario,
		(int Status, string Error) result,
		long elapsedMilliseconds
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( scenario );
		if ( CommandExitCodes.Canceled != result.Status ) {
			throw new InvalidOperationException(
				string.Concat(
					scenario,
					": expected canceled status but received ",
					result.Status.ToString( CultureInfo.InvariantCulture ),
					". Diagnostic: ",
					result.Error
				)
			);
		}
		if ( CancellationDeadlineMilliseconds < elapsedMilliseconds ) {
			throw new InvalidOperationException(
				string.Concat(
					scenario,
					": cancellation completed in ",
					elapsedMilliseconds.ToString( CultureInfo.InvariantCulture ),
					" ms, exceeding the ",
					CancellationDeadlineMilliseconds.ToString( CultureInfo.InvariantCulture ),
					" ms operational deadline."
				)
			);
		}
	}

	private static void RequireStatus(
		string scenario,
		int expected,
		int actual,
		string diagnostic
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( scenario );
		if ( expected == actual ) {
			return;
		}
		throw new InvalidOperationException(
			string.Concat(
				scenario,
				": expected status ",
				expected.ToString( CultureInfo.InvariantCulture ),
				" but received ",
				actual.ToString( CultureInfo.InvariantCulture ),
				". Diagnostic: ",
				diagnostic
			)
		);
	}

	private static void RequireOutputFailure(
		string scenario,
		(int Status, string Error) result
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( scenario );
		if ( CommandExitCodes.UsageError != result.Status ) {
			throw new InvalidOperationException(
				string.Concat(
					scenario,
					": expected grep error status but received ",
					result.Status.ToString( CultureInfo.InvariantCulture ),
					"."
				)
			);
		}
		if ( !result.Error.Contains( "Deterministic T6.8 output failure.", StringComparison.Ordinal ) ) {
			throw new InvalidOperationException(
				string.Concat(
					scenario,
					": missing deterministic output failure diagnostic."
				)
			);
		}
	}

	private static void WriteReport(
		StressReport report,
		string? outputPath
	) {
		ArgumentNullException.ThrowIfNull( report );
		if ( string.IsNullOrWhiteSpace( outputPath ) ) {
			outputPath = System.IO.Path.Combine(
				"artifacts",
				"performance",
				"stress-resilience.json"
			);
		}
		var fullPath = System.IO.Path.GetFullPath( outputPath );
		var directory = System.IO.Path.GetDirectoryName( fullPath );
		if ( !string.IsNullOrWhiteSpace( directory ) ) {
			Directory.CreateDirectory( directory );
		}
		File.WriteAllText(
			fullPath,
			JsonSerializer.Serialize(
				report,
				new JsonSerializerOptions { WriteIndented = true }
			),
			new UTF8Encoding( encoderShouldEmitUTF8Identifier: false )
		);
		Console.WriteLine(
			string.Concat(
				"T6.8 resilience report: ",
				fullPath
			)
		);
	}

	private static string? TryReadGitCommit() {
		try {
			var startInfo = new ProcessStartInfo {
				FileName = "git",
				Arguments = "rev-parse HEAD",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using var process = Process.Start( startInfo );
			if ( null == process ) {
				return null;
			}
			var value = process.StandardOutput.ReadToEnd().Trim();
			process.WaitForExit();
			return 0 == process.ExitCode && !string.IsNullOrWhiteSpace( value )
				? value
				: null;
		} catch {
			return null;
		}
	}

	private static string? TryGetHardwareInventorySha256() {
		var path = Environment.GetEnvironmentVariable( "ICOD_REFERENCE_INVENTORY_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) ) {
			return null;
		}
		using var stream = File.OpenRead( path );
		return Convert.ToHexString(
			SHA256.HashData( stream )
		).ToLowerInvariant();
	}
}
