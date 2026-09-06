namespace Icod.Grep.Benchmarks;

using BenchmarkDotNet.Running;

/// <summary>Provides the T6 benchmark entry point.</summary>
public static class Program {
	/// <summary>Runs benchmark, smoke, metadata, or stress modes.</summary>
	public static int Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );

		if ( 0 < args.Length && "--smoke" == args[ 0 ] ) {
			return BenchmarkSmoke.Run();
		}
		if ( 0 < args.Length && "--stress-smoke" == args[ 0 ] ) {
			string? outputPath = null;
			if ( 1 < args.Length ) {
				outputPath = args[ 1 ];
			}
			return StressHarness.RunSmoke( outputPath );
		}
		if ( 0 < args.Length && "--stress-profile" == args[ 0 ] ) {
			if ( 2 > args.Length ) {
				Console.Error.WriteLine(
					"--stress-profile requires one of: records, files, patterns, reference."
				);
				return 1;
			}
			string? outputPath = null;
			if ( 2 < args.Length ) {
				outputPath = args[ 2 ];
			}
			StressScalingWarmup.Run();
			return StressScalingHarness.Run(
				args[ 1 ],
				outputPath
			);
		}
		if ( 1 < args.Length && "--metadata" == args[ 0 ] ) {
			BenchmarkMetadata.Write(
				args[ 1 ]
			);
			return 0;
		}

		BenchmarkMetadata.WriteRequestedMetadata();
		_ = BenchmarkSwitcher
			.FromAssembly( typeof( Program ).Assembly )
			.Run( args );
		return 0;
	}
}
