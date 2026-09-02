namespace Icod.Grep.Benchmarks;

using BenchmarkDotNet.Attributes;
using Icod.CommandFramework.Records;

/// <summary>Measures the shared materializing byte-record pipeline used by grep.</summary>
[MemoryDiagnoser]
public sealed class RecordReaderBenchmarks {
	private byte[] input = Array.Empty<byte>();

	/// <summary>Gets the logical record length.</summary>
	[Params( 80, 4096, 262144 )]
	public int RecordLength { get; set; }

	/// <summary>Generates a stable corpus whose total size remains representative across record lengths.</summary>
	[GlobalSetup]
	public void Setup() {
		var recordCount = this.RecordLength switch {
			80 => 16384,
			4096 => 2048,
			262144 => 32,
			_ => throw new InvalidOperationException(
				string.Concat(
					"Unexpected record length: ",
					this.RecordLength.ToString( System.Globalization.CultureInfo.InvariantCulture )
				)
			)
		};
		var scenario = new BenchmarkScenario(
			"record-reader",
			"basic",
			recordCount,
			this.RecordLength,
			int.MaxValue,
			1,
			"TARGET",
			false
		);
		this.input = CorpusFactory.Create( scenario ).Input;
	}

	/// <summary>Reads and materializes all records.</summary>
	[Benchmark]
	public async Task<int> ReadAllRecordsAsync() {
		using var stream = new MemoryStream(
			this.input,
			writable: false
		);
		using var reader = new ByteRecordReader( stream );
		var count = 0;
		while ( null != await reader.ReadAsync().ConfigureAwait( false ) ) {
			count++;
		}
		return count;
	}
}
