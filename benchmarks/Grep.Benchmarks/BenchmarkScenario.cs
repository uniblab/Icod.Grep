namespace Icod.Grep.Benchmarks;

/// <summary>Describes one deterministic command benchmark scenario.</summary>
internal sealed record BenchmarkScenario(
	string Name,
	string Matcher,
	int RecordCount,
	int RecordLength,
	int MatchEvery,
	int PatternCount,
	string Pattern,
	bool Utf8
) {
	internal int ExpectedMatchCount =>
		0 >= this.RecordCount
			? 0
			: ((this.RecordCount - 1) / Math.Max( 1, this.MatchEvery )) + 1;

	internal BenchmarkScenario ToSmokeScenario() => this with {
		RecordCount = Math.Min( this.RecordCount, 64 ),
		RecordLength = Math.Min( this.RecordLength, 256 ),
		MatchEvery = Math.Min( Math.Max( 1, this.MatchEvery ), 17 ),
		PatternCount = Math.Min( this.PatternCount, 10 )
	};
}
