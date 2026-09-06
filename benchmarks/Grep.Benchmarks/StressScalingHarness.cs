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

/// <summary>Runs the explicit physical T6.8 scaling profiles.</summary>
internal static class StressScalingHarness {
	private const int Megabyte = 1024 * 1024;
	private static readonly string[] MatcherNames = [
		"fixed",
		"basic",
		"extended",
		"perl"
	];

	internal static int Run(
		string profile,
		string? outputPath
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( profile );
		try {
			return RunAsync(
				profile,
				outputPath
			).GetAwaiter().GetResult();
		} catch ( Exception exception ) {
			Console.Error.WriteLine( exception.Message );
			return 1;
		}
	}

	private static async Task<int> RunAsync(
		string profile,
		string? outputPath
	) {
		var scenarios = CreateScenarios( profile );
		var results = new List<StressScenarioResult>( scenarios.Count );
		Console.WriteLine(
			string.Concat(
				"T6.8 stress profile '",
				profile,
				"' contains ",
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
		var report = CreateReport(
			profile,
			allSucceeded,
			results
		);
		WriteReport(
			report,
			outputPath
		);
		return allSucceeded ? 0 : 1;
	}

	private static IReadOnlyList<StressScenario> CreateScenarios(
		string profile
	) {
		var scenarios = new List<StressScenario>();
		switch ( profile ) {
			case "records":
				AddRecordScenarios( scenarios );
				break;
			case "files":
				AddFileCountScenarios( scenarios );
				break;
			case "patterns":
				AddPatternScenarios( scenarios );
				break;
			case "reference":
				AddRecordScenarios( scenarios );
				AddFileCountScenarios( scenarios );
				AddPatternScenarios( scenarios );
				break;
			default:
				throw new ArgumentException(
					string.Concat(
						"Unknown stress profile '",
						profile,
						"'. Expected records, files, patterns, or reference."
					),
					nameof( profile )
				);
		}
		return scenarios;
	}

	private static void AddRecordScenarios(
		List<StressScenario> scenarios
	) {
		ArgumentNullException.ThrowIfNull( scenarios );
		foreach ( var sizeMiB in new[] { 1, 16, 64 } ) {
			foreach ( var matcher in MatcherNames ) {
				var capturedSize = sizeMiB;
				var capturedMatcher = matcher;
				scenarios.Add(
					new StressScenario(
						string.Concat(
							"S1-record-",
							capturedMatcher,
							"-",
							capturedSize.ToString( CultureInfo.InvariantCulture ),
							"MiB"
						),
						"S1",
						new Dictionary<string, string> {
							[ "kind" ] = "single-record",
							[ "matcher" ] = capturedMatcher,
							[ "recordMiB" ] = capturedSize.ToString( CultureInfo.InvariantCulture ),
							[ "matchPosition" ] = "late",
							[ "output" ] = "count"
						},
						fixture => PrepareLargeRecordScenario(
							fixture,
							capturedMatcher,
							capturedSize
						)
					)
				);
			}
		}

		foreach ( var sizeMiB in new[] { 64, 256, 1024 } ) {
			var capturedSize = sizeMiB;
			scenarios.Add(
				new StressScenario(
					string.Concat(
						"S1-file-fixed-",
						capturedSize.ToString( CultureInfo.InvariantCulture ),
						"MiB"
					),
					"S1",
					new Dictionary<string, string> {
						[ "kind" ] = "many-record-file",
						[ "matcher" ] = "fixed",
						[ "fileMiB" ] = capturedSize.ToString( CultureInfo.InvariantCulture ),
						[ "recordBytes" ] = "64",
						[ "matchPosition" ] = "final-record",
						[ "output" ] = "count"
					},
					fixture => PrepareLargeFileScenario(
						fixture,
						capturedSize
					)
				)
			);
		}
	}

	private static void AddFileCountScenarios(
		List<StressScenario> scenarios
	) {
		ArgumentNullException.ThrowIfNull( scenarios );
		foreach ( var fileCount in new[] { 1_000, 10_000, 50_000 } ) {
			var capturedCount = fileCount;
			scenarios.Add(
				new StressScenario(
					string.Concat(
						"S2-files-",
						capturedCount.ToString( CultureInfo.InvariantCulture )
					),
					"S2",
					new Dictionary<string, string> {
						[ "files" ] = capturedCount.ToString( CultureInfo.InvariantCulture ),
						[ "layout" ] = "nested",
						[ "matchEveryFiles" ] = "1000",
						[ "traversal" ] = "recursive",
						[ "output" ] = "files-with-matches"
					},
					fixture => PrepareFileCountScenario(
						fixture,
						capturedCount
					)
				)
			);
		}
	}

	private static void AddPatternScenarios(
		List<StressScenario> scenarios
	) {
		ArgumentNullException.ThrowIfNull( scenarios );
		AddPatternSeries(
			scenarios,
			"fixed",
			[ 100, 1_000, 10_000 ]
		);
		AddPatternSeries(
			scenarios,
			"basic",
			[ 100, 1_000 ]
		);
		AddPatternSeries(
			scenarios,
			"extended",
			[ 100, 1_000 ]
		);
		AddPatternSeries(
			scenarios,
			"perl",
			[ 100, 1_000 ]
		);
	}

	private static void AddPatternSeries(
		List<StressScenario> scenarios,
		string matcher,
		IReadOnlyList<int> patternCounts
	) {
		ArgumentNullException.ThrowIfNull( scenarios );
		ArgumentException.ThrowIfNullOrWhiteSpace( matcher );
		ArgumentNullException.ThrowIfNull( patternCounts );
		foreach ( var patternCount in patternCounts ) {
			var capturedCount = patternCount;
			var capturedMatcher = matcher;
			scenarios.Add(
				new StressScenario(
					string.Concat(
						"S3-patterns-",
						capturedMatcher,
						"-",
						capturedCount.ToString( CultureInfo.InvariantCulture )
					),
					"S3",
					new Dictionary<string, string> {
						[ "matcher" ] = capturedMatcher,
						[ "patterns" ] = capturedCount.ToString( CultureInfo.InvariantCulture ),
						[ "records" ] = "129",
						[ "matchingPattern" ] = "last",
						[ "output" ] = "count"
					},
					fixture => PreparePatternScenario(
						fixture,
						capturedMatcher,
						capturedCount
					)
				)
			);
		}
	}

	private static StressScenarioExecution PrepareLargeRecordScenario(
		StressFixtureDirectory fixture,
		string matcher,
		int sizeMiB
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		ArgumentException.ThrowIfNullOrWhiteSpace( matcher );
		if ( sizeMiB <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( sizeMiB ) );
		}
		var path = fixture.GetPath( "large-record.txt" );
		WriteSingleRecordFile(
			path,
			checked( (long)sizeMiB * Megabyte )
		);
		var input = new MemoryStream( Array.Empty<byte>(), writable: false );
		var output = new MemoryStream();
		var arguments = CreateSinglePatternArguments(
			matcher,
			path
		);
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					arguments,
					input,
					output
				).ConfigureAwait( false );
				RequireStatus(
					CommandExitCodes.Success,
					result.Status,
					result.Error
				);
				RequireCountOutput(
					output,
					1
				);
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					string.Concat(
						sizeMiB.ToString( CultureInfo.InvariantCulture ),
						" MiB single record; late ",
						matcher,
						" match"
					)
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareLargeFileScenario(
		StressFixtureDirectory fixture,
		int sizeMiB
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		if ( sizeMiB <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( sizeMiB ) );
		}
		var path = fixture.GetPath( "large-file.txt" );
		WriteManyRecordFile(
			path,
			checked( (long)sizeMiB * Megabyte )
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
					CommandExitCodes.Success,
					result.Status,
					result.Error
				);
				RequireCountOutput(
					output,
					1
				);
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					string.Concat(
						sizeMiB.ToString( CultureInfo.InvariantCulture ),
						" MiB short-record file plus one final matching record"
					)
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static StressScenarioExecution PrepareFileCountScenario(
		StressFixtureDirectory fixture,
		int fileCount
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		if ( fileCount <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( fileCount ) );
		}
		var root = fixture.CreateDirectory( "tree" );
		var directoryCount = fileCount <= 1_000 ? 10 : 100;
		var matchingFileCount = 0;
		var ordinary = "ordinary payload\n"u8.ToArray();
		var matching = "TARGET payload\n"u8.ToArray();
		for ( var index = 0; index < fileCount; index++ ) {
			var directory = System.IO.Path.Combine(
				root,
				string.Concat(
					"d",
					(index % directoryCount).ToString(
						"D3",
						CultureInfo.InvariantCulture
					)
				)
			);
			Directory.CreateDirectory( directory );
			var isMatch = 0 == index % 1_000;
			if ( isMatch ) {
				matchingFileCount++;
			}
			File.WriteAllBytes(
				System.IO.Path.Combine(
					directory,
					string.Concat(
						"file-",
						index.ToString(
							"D5",
							CultureInfo.InvariantCulture
						),
						".txt"
					)
				),
				isMatch ? matching : ordinary
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
				if ( matchingFileCount != lines.Length ) {
					throw new InvalidOperationException(
						string.Concat(
							"Expected ",
							matchingFileCount.ToString( CultureInfo.InvariantCulture ),
							" matching file names but received ",
							lines.Length.ToString( CultureInfo.InvariantCulture ),
							"."
						)
					);
				}
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					string.Concat(
						fileCount.ToString( CultureInfo.InvariantCulture ),
						" files across ",
						directoryCount.ToString( CultureInfo.InvariantCulture ),
						" directories; ",
						matchingFileCount.ToString( CultureInfo.InvariantCulture ),
						" matching files"
					)
				);
			},
			() => {
				input.Dispose();
				output.Dispose();
			}
		);
	}

	private static StressScenarioExecution PreparePatternScenario(
		StressFixtureDirectory fixture,
		string matcher,
		int patternCount
	) {
		ArgumentNullException.ThrowIfNull( fixture );
		ArgumentException.ThrowIfNullOrWhiteSpace( matcher );
		if ( patternCount <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( patternCount ) );
		}
		_ = fixture;
		var arguments = CreatePatternSetArguments(
			matcher,
			patternCount
		);
		var input = new MemoryStream(
			CreatePatternInput(),
			writable: false
		);
		var output = new MemoryStream();
		return new StressScenarioExecution(
			async () => {
				var result = await RunCommandAsync(
					arguments,
					input,
					output
				).ConfigureAwait( false );
				RequireStatus(
					CommandExitCodes.Success,
					result.Status,
					result.Error
				);
				RequireCountOutput(
					output,
					1
				);
				return new StressScenarioOutcome(
					result.Status,
					output.Length,
					string.Concat(
						patternCount.ToString( CultureInfo.InvariantCulture ),
						" ",
						matcher,
						" patterns across 129 records"
					)
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
			"Fixture preparation or scenario construction failed.",
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

	private static string[] CreateSinglePatternArguments(
		string matcher,
		string path
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( matcher );
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		return [
			GetMatcherOption( matcher ),
			"-c",
			"TARGET",
			path
		];
	}

	private static string[] CreatePatternSetArguments(
		string matcher,
		int patternCount
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( matcher );
		if ( patternCount <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( patternCount ) );
		}
		var arguments = new List<string>( checked( 2 + (patternCount * 2) ) ) {
			GetMatcherOption( matcher ),
			"-c"
		};
		for ( var index = 0; index < patternCount - 1; index++ ) {
			arguments.Add( "-e" );
			arguments.Add(
				string.Concat(
					"NO_MATCH_",
					index.ToString(
						"D5",
						CultureInfo.InvariantCulture
					)
				)
			);
		}
		arguments.Add( "-e" );
		arguments.Add( "TARGET" );
		return arguments.ToArray();
	}

	private static string GetMatcherOption( string matcher ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( matcher );
		return matcher switch {
			"fixed" => "-F",
			"basic" => "-G",
			"extended" => "-E",
			"perl" => "-P",
			_ => throw new ArgumentException(
				string.Concat(
					"Unknown matcher '",
					matcher,
					"'."
				),
				nameof( matcher )
			)
		};
	}

	private static byte[] CreatePatternInput() {
		using var memory = new MemoryStream();
		var ordinary = "ordinary payload\n"u8;
		for ( var index = 0; index < 128; index++ ) {
			memory.Write( ordinary );
		}
		memory.Write( "TARGET payload\n"u8 );
		return memory.ToArray();
	}

	private static void WriteSingleRecordFile(
		string path,
		long recordBytes
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		if ( recordBytes <= "TARGET"u8.Length ) {
			throw new ArgumentOutOfRangeException( nameof( recordBytes ) );
		}
		var directory = System.IO.Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( directory ) ) {
			Directory.CreateDirectory( directory );
		}
		var chunk = new byte[Megabyte];
		chunk.AsSpan().Fill( (byte)'a' );
		using var stream = new FileStream(
			path,
			FileMode.Create,
			FileAccess.Write,
			FileShare.None,
			Megabyte,
			FileOptions.SequentialScan
		);
		var remaining = recordBytes - "TARGET"u8.Length;
		while ( 0 < remaining ) {
			var count = (int)Math.Min( remaining, chunk.Length );
			stream.Write( chunk, 0, count );
			remaining -= count;
		}
		stream.Write( "TARGET"u8 );
		stream.WriteByte( (byte)'\n' );
	}

	private static void WriteManyRecordFile(
		string path,
		long targetBytes
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		if ( targetBytes <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( targetBytes ) );
		}
		var directory = System.IO.Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( directory ) ) {
			Directory.CreateDirectory( directory );
		}
		var chunk = new byte[Megabyte];
		chunk.AsSpan().Fill( (byte)'a' );
		for ( var index = 63; index < chunk.Length; index += 64 ) {
			chunk[index] = (byte)'\n';
		}
		using var stream = new FileStream(
			path,
			FileMode.Create,
			FileAccess.Write,
			FileShare.None,
			Megabyte,
			FileOptions.SequentialScan
		);
		var remaining = targetBytes;
		while ( 0 < remaining ) {
			var count = (int)Math.Min( remaining, chunk.Length );
			stream.Write( chunk, 0, count );
			remaining -= count;
		}
		stream.Write( "TARGET\n"u8 );
	}

	private static void RequireStatus(
		int expected,
		int actual,
		string diagnostic
	) {
		if ( expected == actual ) {
			return;
		}
		throw new InvalidOperationException(
			string.Concat(
				"Expected status ",
				expected.ToString( CultureInfo.InvariantCulture ),
				" but received ",
				actual.ToString( CultureInfo.InvariantCulture ),
				". Diagnostic: ",
				diagnostic
			)
		);
	}

	private static void RequireCountOutput(
		MemoryStream output,
		long expected
	) {
		ArgumentNullException.ThrowIfNull( output );
		var actual = Encoding.UTF8.GetString(
			output.ToArray()
		).Trim();
		var expectedText = expected.ToString(
			CultureInfo.InvariantCulture
		);
		if ( string.Equals( expectedText, actual, StringComparison.Ordinal ) ) {
			return;
		}
		throw new InvalidOperationException(
			string.Concat(
				"Expected count output '",
				expectedText,
				"' but received '",
				actual,
				"'."
			)
		);
	}

	private static StressReport CreateReport(
		string profile,
		bool allSucceeded,
		IReadOnlyList<StressScenarioResult> results
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( profile );
		ArgumentNullException.ThrowIfNull( results );
		return new StressReport(
			1,
			profile,
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
				"T6.8 stress profile report: ",
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
