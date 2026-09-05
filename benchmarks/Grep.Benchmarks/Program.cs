namespace Icod.Grep.Benchmarks;

using BenchmarkDotNet.Running;

/// <summary>Provides the T6 benchmark entry point.</summary>
public static class Program {
	/// <summary>Runs benchmark, smoke, or metadata modes.</summary>
	public static int Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );

		if ( 0 < args.Length && "--smoke" == args[ 0 ] ) {
			return BenchmarkSmoke.Run();
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
