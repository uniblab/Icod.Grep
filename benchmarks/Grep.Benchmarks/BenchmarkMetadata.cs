namespace Icod.Grep.Benchmarks;

using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Icod.Grep;

/// <summary>Writes reproducibility metadata without copying the reference-machine inventory.</summary>
internal static class BenchmarkMetadata {
	internal static void WriteRequestedMetadata() {
		var path = Environment.GetEnvironmentVariable(
			"ICOD_BENCHMARK_METADATA_PATH"
		);
		if ( string.IsNullOrWhiteSpace( path ) ) {
			return;
		}
		Write( path );
	}

	internal static void Write( string path ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		var inventoryPath = ResolveInventoryPath();
		var metadata = new {
			SchemaVersion = 1,
			RecordedUtc = DateTimeOffset.UtcNow,
			Source = Environment.GetEnvironmentVariable( "ICOD_BENCHMARK_SOURCE" ) ?? "Unspecified",
			Label = Environment.GetEnvironmentVariable( "ICOD_BENCHMARK_LABEL" ) ?? "Unspecified",
			Commit = Environment.GetEnvironmentVariable( "ICOD_BENCHMARK_COMMIT" ) ?? TryReadGitCommit(),
			GrepAssemblyVersion = typeof( Command ).Assembly.GetName().Version?.ToString(),
			OperatingSystem = RuntimeInformation.OSDescription,
			OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
			ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
			Framework = RuntimeInformation.FrameworkDescription,
			ProcessorCount = Environment.ProcessorCount,
			CpuModel = TryGetCpuModel(),
			ServerGC = GCSettings.IsServerGC,
			GCLatencyMode = GCSettings.LatencyMode.ToString(),
			TotalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
			ScenarioCatalogVersion = 1,
			ScenarioNames = ScenarioCatalog.All.Select( static scenario => scenario.Name ).ToArray(),
			HardwareInventory = null == inventoryPath
				? null
				: new {
					Path = System.IO.Path.GetFileName( inventoryPath ),
					Sha256 = Convert.ToHexString(
						SHA256.HashData(
							File.ReadAllBytes( inventoryPath )
						)
					).ToLowerInvariant()
				}
		};
		var directory = System.IO.Path.GetDirectoryName(
			System.IO.Path.GetFullPath( path )
		);
		if ( !string.IsNullOrEmpty( directory ) ) {
			Directory.CreateDirectory( directory );
		}
		File.WriteAllText(
			path,
			JsonSerializer.Serialize(
				metadata,
				new JsonSerializerOptions {
					WriteIndented = true
				}
			)
		);
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
			return string.IsNullOrWhiteSpace( identifier )
				? null
				: identifier.Trim();
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
					if ( line.StartsWith( "model name", StringComparison.OrdinalIgnoreCase ) ) {
						var separator = line.IndexOf( ':' );
						return 0 <= separator
							? line[ (separator + 1).. ].Trim()
							: line.Trim();
					}
				}
			} catch ( IOException ) {
			}
			catch ( UnauthorizedAccessException ) {
			}
		}
		return null;
	}

	private static string? TryRunProcess(
		string fileName,
		string arguments
	) {
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
			return 0 == process.ExitCode && !string.IsNullOrWhiteSpace( output )
				? output.Trim()
				: null;
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
