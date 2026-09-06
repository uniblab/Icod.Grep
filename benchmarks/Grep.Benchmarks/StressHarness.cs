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

/// <summary>Runs deterministic T6.8 stress profiles outside BenchmarkDotNet.</summary>
internal static class StressHarness {
	internal static int RunSmoke( string? outputPath ) {
		try {
			return RunSmokeAsync( outputPath ).GetAwaiter().GetResult();
		} catch ( Exception exception ) {
			Console.Error.WriteLine( exception.Message );
			return 1;
		}
	}

	private static async Task<int> RunSmokeAsync( string? outputPath ) {
		var results = new List<StressScenarioResult>();
		foreach ( var scenario in CreateSmokeScenarios() ) {
			results.Add(
				await RunScenarioAsync( scenario ).ConfigureAwait( false )
			);
		}

		var allSucceeded = results.All(
			static result => result.Succeeded
		);
		var report = new StressReport(
			1,
			"smoke",
			DateTimeOffset.UtcNow,
			Environment.GetEnvironmentVariable( "ICOD_BENCHMARK_COMMIT" ) ?? TryReadGitCommit(),
			typeof( Command ).Assembly.GetName().Version?.ToString(),
			RuntimeInformation.OSDescription,
			RuntimeInformation.OSArchitecture.ToString(),
			RuntimeInformation.ProcessArchitecture.ToString(),
			RuntimeInformation.FrameworkDescription,
			Environment.ProcessorCount,
			TryGetCpuModel(),
			GCSettings.IsServerGC,
			GCSettings.LatencyMode.ToString(),
			GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
			TryGetHardwareInventorySha256(),
			allSucceeded,
			results
		);
		WriteReport( report, outputPath );

		if ( allSucceeded ) {
			return 0;
		}
		foreach ( var result in results.Where( static result => !result.Succeeded ) ) {
			Console.Error.WriteLine(
				string.Concat(
					result.Name,
					": ",
					result.Error ?? "stress scenario failed"
				)
			);
		}
		return 1;
	}

	private static IReadOnlyList<StressScenario> CreateSmokeScenarios() {
		return [
			new StressScenario(
				"S1-large-record-late-match",
				"S1",
				new Dictionary<string, string> {
					[ "recordBytes" ] = (1024 * 1024).ToString( CultureInfo.InvariantCulture ),
					[ "matcher" ] = "fixed",
					[ "output" ] = "count"
				},
				PrepareLargeRecordScenario
			),
			new StressScenario(
				"S2-nested-file-tree",
				"S2",
				new Dictionary<string, string> {
					[ "files" ] = "128",
					[ "directories" ] = "8",
					[ "matchingFiles" ] = "8",
					[ "traversal" ] = "recursive"
				},
				PrepareManyFileScenario
			),
			new StressScenario(
				"S3-fixed-pattern-set",
				"S3",
				new Dictionary<string, string> {
					[ "patterns" ] = "128",
					[ "matcher" ] = "fixed",
					[ "output" ] = "count"
				},
				PreparePatternSetScenario
			),
			new StressScenario(
				"S4-deterministic-cancellation",
				"S4",
				new Dictionary<string, string> {
					[ "inputBytes" ] = (512 * 1024).ToString( CultureInfo.InvariantCulture ),
					[ "readChunkBytes" ] = "4096",
					[ "cancelOnRead" ] = "8"
				},
				PrepareCancellationScenario
			),
			new StressScenario(
				"S5-delayed-bounded-output",
				"S5",
				new Dictionary<string, string> {
					[ "records" ] = "32",
					[ "writeChunkBytes" ] = "8",
					[ "delayMilliseconds" ] = "1"
				},
				PrepareBackpressureScenario
			),
			new StressScenario(
				"S5-write-failure",
				"S5",
				new Dictionary<string, string> {
					[ "failAfterBytes" ] = "64",
					[ "failurePoint" ] = "write"
				},
				PrepareWriteFailureScenario
			),
			new StressScenario(
				"S5-flush-failure",
				"S5",
				new Dictionary<string, string> {
					[ "failurePoint" ] = "flush"
				},
				PrepareFlushFailureScenario
			)
		];
	}

	private static StressScenarioExecution PrepareLargeRecordScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		const int recordLength = 1024 * 1024;
		var content = new byte[recordLength + 1];
		content.AsSpan().Fill( (byte)'a' );
		var marker = "TARGET"u8;
		marker.CopyTo(
			content.AsSpan(
				recordLength - marker.Length,
				marker.Length
			)
		);
		content[^1] = (byte)'\n';
		var path = fixture.WriteBytes(
			content,
			"large-record.txt"
		);
		var input = new MemoryStream( Array.Empty<byte>(), writable: false );
		var output = new MemoryStream();
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-F", "-c", "TARGET", path ],
					input,
					output
				).ConfigureAwait( false );
				RequireStatus(
					"S1-large-record-late-match",
					CommandExitCodes.Success,
					result.Status,
					result.Error
				);
				RequireCountOutput(
					"S1-large-record-late-match",
					output.ToArray(),
					1
				);
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					"1 MiB single record with a late fixed-string match"
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareManyFileScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		var root = fixture.CreateDirectory( "tree" );
		for ( var index = 0; index < 128; index++ ) {
			var directory = System.IO.Path.Combine(
				root,
				string.Concat(
					"d",
					(index % 8).ToString(
						"D2",
						CultureInfo.InvariantCulture
					)
				)
			);
			Directory.CreateDirectory( directory );
			byte[] content;
			if ( 0 == index % 16 ) {
				content = "record TARGET payload\n"u8.ToArray();
			} else {
				content = "record ordinary payload\n"u8.ToArray();
			}
			File.WriteAllBytes(
				System.IO.Path.Combine(
					directory,
					string.Concat(
						"file-",
						index.ToString(
							"D4",
							CultureInfo.InvariantCulture
						),
						".txt"
					)
				),
				content
			);
		}
		var input = new MemoryStream( Array.Empty<byte>(), writable: false );
		var output = new MemoryStream();
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-F", "-r", "-l", "TARGET", root ],
					input,
					output
				).ConfigureAwait( false );
				RequireStatus(
					"S2-nested-file-tree",
					CommandExitCodes.Success,
					result.Status,
					result.Error
				);
				var lines = Encoding.UTF8.GetString(
					output.ToArray()
				).ReplaceLineEndings( "\n" ).Split(
					'\n',
					StringSplitOptions.RemoveEmptyEntries
				);
				if ( 8 != lines.Length ) {
					throw new InvalidOperationException(
						string.Concat(
							"S2-nested-file-tree: expected 8 matching file names but received ",
							lines.Length.ToString( CultureInfo.InvariantCulture ),
							"."
						)
					);
				}
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					"128 files in 8 directories; 8 matching files"
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static StressScenarioExecution PreparePatternSetScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		_ = fixture;
		var arguments = new List<string> {
			"-F",
			"-c"
		};
		for ( var index = 0; index < 127; index++ ) {
			arguments.Add( "-e" );
			arguments.Add(
				string.Concat(
					"NO_MATCH_",
					index.ToString(
						"D3",
						CultureInfo.InvariantCulture
					)
				)
			);
		}
		arguments.Add( "-e" );
		arguments.Add( "TARGET" );
		var input = new MemoryStream(
			"ordinary record\nTARGET payload\n"u8.ToArray(),
			writable: false
		);
		var output = new MemoryStream();
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					arguments.ToArray(),
					input,
					output
				).ConfigureAwait( false );
				RequireStatus(
					"S3-fixed-pattern-set",
					CommandExitCodes.Success,
					result.Status,
					result.Error
				);
				RequireCountOutput(
					"S3-fixed-pattern-set",
					output.ToArray(),
					1
				);
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					"128 fixed patterns with the matching pattern last"
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareCancellationScenario(
		StressFixtureDirectory fixture
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		_ = fixture;
		var content = new byte[512 * 1024];
		content.AsSpan().Fill( (byte)'a' );
		for ( var index = 63; index < content.Length; index += 64 ) {
			content[index] = (byte)'\n';
		}
		var cancellation = new CancellationTokenSource();
		var input = new TriggeredCancellationReadStream(
			content,
			4096,
			8,
			cancellation.Cancel
		);
		var output = new MemoryStream();
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-a", "-F", "-c", "TARGET" ],
					input,
					output,
					cancellation.Token
				).ConfigureAwait( false );
				RequireStatus(
					"S4-deterministic-cancellation",
					CommandExitCodes.Canceled,
					result.Status,
					result.Error
				);
				if ( !input.CancellationTriggered ) {
					throw new InvalidOperationException(
						"S4-deterministic-cancellation: the configured cancellation trigger was not reached."
					);
				}
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					"Cancellation triggered deterministically after eight 4 KiB reads"
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
		var input = new MemoryStream(
			CreateRepeatedRecords( 32, "TARGET payload\n" ),
			writable: false
		);
		var output = new DelayedChunkWriteStream(
			8,
			1
		);
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-F", "TARGET" ],
					input,
					output
				).ConfigureAwait( false );
				RequireStatus(
					"S5-delayed-bounded-output",
					CommandExitCodes.Success,
					result.Status,
					result.Error
				);
				var lines = Encoding.UTF8.GetString(
					output.ToArray()
				).ReplaceLineEndings( "\n" ).Split(
					'\n',
					StringSplitOptions.RemoveEmptyEntries
				);
				if ( 32 != lines.Length ) {
					throw new InvalidOperationException(
						string.Concat(
							"S5-delayed-bounded-output: expected 32 output records but received ",
							lines.Length.ToString( CultureInfo.InvariantCulture ),
							"."
						)
					);
				}
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					"Output accepted in delayed 8-byte chunks"
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
		var input = new MemoryStream(
			CreateRepeatedRecords( 16, "TARGET payload\n" ),
			writable: false
		);
		var output = new FailingWriteStream(
			64,
			failOnFlush: false
		);
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-F", "TARGET" ],
					input,
					output
				).ConfigureAwait( false );
				RequireOutputFailure(
					"S5-write-failure",
					result
				);
				return new StressScenarioOutcome(
					result.Status,
					output.AcceptedBytes,
					"Write failure converted to the established grep error status"
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
		var input = new MemoryStream(
			"TARGET payload\n"u8.ToArray(),
			writable: false
		);
		var output = new FailingWriteStream(
			long.MaxValue,
			failOnFlush: true
		);
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					[ "-F", "TARGET" ],
					input,
					output
				).ConfigureAwait( false );
				RequireOutputFailure(
					"S5-flush-failure",
					result
				);
				return new StressScenarioOutcome(
					result.Status,
					output.AcceptedBytes,
					"Flush/completion failure converted to the established grep error status"
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

	private static async Task<(int Status, string Error)> RunCommandAsync(
		string[] arguments,
		Stream input,
		Stream output,
		CancellationToken cancellationToken = default
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
			output,
			null,
			cancellationToken
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

	private static void RequireCountOutput(
		string scenario,
		byte[] output,
		long expected
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( scenario );
		ArgumentNullException.ThrowIfNull( output );
		var actual = Encoding.UTF8.GetString(
			output
		).Trim();
		var expectedText = expected.ToString(
			CultureInfo.InvariantCulture
		);
		if ( string.Equals( expectedText, actual, StringComparison.Ordinal ) ) {
			return;
		}
		throw new InvalidOperationException(
			string.Concat(
				scenario,
				": expected count output '",
				expectedText,
				"' but received '",
				actual,
				"'."
			)
		);
	}

	private static void RequireOutputFailure(
		string scenario,
		(int Status, string Error) result
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( scenario );
		RequireStatus(
			scenario,
			CommandExitCodes.UsageError,
			result.Status,
			result.Error
		);
		if (
			!result.Error.Contains(
				"Deterministic T6.8 output failure.",
				StringComparison.Ordinal
			)
		) {
			throw new InvalidOperationException(
				string.Concat(
					scenario,
					": expected the deterministic output-failure diagnostic but received '",
					result.Error.Trim(),
					"'."
				)
			);
		}
	}

	private static void WriteReport(
		StressReport report,
		string? outputPath
	) {
		ArgumentNullException.ThrowIfNull( report );
		var json = JsonSerializer.Serialize(
			report,
			new JsonSerializerOptions {
				WriteIndented = true
			}
		);
		if ( string.IsNullOrWhiteSpace( outputPath ) ) {
			Console.WriteLine( json );
			return;
		}
		var fullPath = System.IO.Path.GetFullPath( outputPath );
		var directory = System.IO.Path.GetDirectoryName( fullPath );
		if ( !string.IsNullOrEmpty( directory ) ) {
			Directory.CreateDirectory( directory );
		}
		File.WriteAllText(
			fullPath,
			json,
			new UTF8Encoding( encoderShouldEmitUTF8Identifier: false )
		);
		Console.WriteLine(
			string.Concat(
				"T6.8 stress smoke report: ",
				fullPath
			)
		);
	}

	private static string? TryReadGitCommit() {
		return TryRunProcess(
			"git",
			"rev-parse HEAD"
		);
	}

	private static string? TryGetCpuModel() {
		if ( OperatingSystem.IsWindows() ) {
			var identifier = Environment.GetEnvironmentVariable(
				"PROCESSOR_IDENTIFIER"
			);
			if ( string.IsNullOrWhiteSpace( identifier ) ) {
				return null;
			}
			return identifier.Trim();
		}
		if ( OperatingSystem.IsMacOS() ) {
			return TryRunProcess(
				"sysctl",
				"-n machdep.cpu.brand_string"
			);
		}
		if ( OperatingSystem.IsLinux() ) {
			try {
				foreach ( var line in File.ReadLines( "/proc/cpuinfo" ) ) {
					if ( !line.StartsWith( "model name", StringComparison.OrdinalIgnoreCase ) ) {
						continue;
					}
					var separator = line.IndexOf( ':' );
					if ( 0 <= separator ) {
						return line[(separator + 1)..].Trim();
					}
					return line.Trim();
				}
			} catch ( IOException ) {
			} catch ( UnauthorizedAccessException ) {
			}
		}
		return null;
	}

	private static string? TryGetHardwareInventorySha256() {
		var path = ResolveInventoryPath();
		if ( null == path ) {
			return null;
		}
		try {
			return Convert.ToHexString(
				SHA256.HashData(
					File.ReadAllBytes( path )
				)
			).ToLowerInvariant();
		} catch ( IOException ) {
			return null;
		} catch ( UnauthorizedAccessException ) {
			return null;
		}
	}

	private static string? ResolveInventoryPath() {
		var configured = Environment.GetEnvironmentVariable(
			"ICOD_REFERENCE_INVENTORY_PATH"
		);
		if (
			!string.IsNullOrWhiteSpace( configured )
			&& File.Exists( configured )
		) {
			return System.IO.Path.GetFullPath( configured );
		}
		var directory = new DirectoryInfo(
			Directory.GetCurrentDirectory()
		);
		while ( null != directory ) {
			var candidate = System.IO.Path.Combine(
				directory.FullName,
				"hardware_inventory.txt"
			);
			if ( File.Exists( candidate ) ) {
				return candidate;
			}
			directory = directory.Parent;
		}
		return null;
	}

	private static string? TryRunProcess(
		string fileName,
		string arguments
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( fileName );
		ArgumentNullException.ThrowIfNull( arguments );
		try {
			using var process = Process.Start(
				new ProcessStartInfo {
					FileName = fileName,
					Arguments = arguments,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			);
			if ( null == process ) {
				return null;
			}
			var output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();
			if ( 0 != process.ExitCode || string.IsNullOrWhiteSpace( output ) ) {
				return null;
			}
			return output.Trim();
		} catch ( Exception exception ) when (
			exception is InvalidOperationException
			or System.ComponentModel.Win32Exception
			or IOException
			or UnauthorizedAccessException
		) {
			return null;
		}
	}
}