namespace Icod.Grep.Benchmarks;

/// <summary>Describes one deterministic T6.8 stress scenario.</summary>
internal sealed record StressScenario(
	string Name,
	string Dimension,
	IReadOnlyDictionary<string, string> Parameters,
	Func<StressFixtureDirectory, StressScenarioExecution> Prepare
);

/// <summary>Owns prepared per-scenario resources while exposing the measured command operation.</summary>
internal sealed class StressScenarioExecution : IDisposable {
	private readonly Action? dispose;
	private readonly Func<Task<StressScenarioOutcome>> executeAsync;
	private bool disposed;

	internal StressScenarioExecution(
		Func<Task<StressScenarioOutcome>> executeAsync,
		Action? dispose = null
	) {
		ArgumentNullException.ThrowIfNull( executeAsync );
		this.executeAsync = executeAsync;
		this.dispose = dispose;
	}

	internal Task<StressScenarioOutcome> ExecuteAsync() {
		ObjectDisposedException.ThrowIf( this.disposed, this );
		return this.executeAsync();
	}

	public void Dispose() {
		if ( this.disposed ) {
			return;
		}
		this.disposed = true;
		this.dispose?.Invoke();
	}
}

/// <summary>Captures the command-level outcome produced by one stress scenario.</summary>
internal sealed record StressScenarioOutcome(
	int Status,
	long OutputBytes,
	string? Detail = null
);

/// <summary>Captures measured resource observations for one stress scenario.</summary>
internal sealed record StressScenarioResult(
	string Name,
	string Dimension,
	IReadOnlyDictionary<string, string> Parameters,
	bool Succeeded,
	int? Status,
	string? Detail,
	string? Error,
	double ElapsedMilliseconds,
	long AllocatedBytes,
	int Gen0Collections,
	int Gen1Collections,
	int Gen2Collections,
	long WorkingSetBeforeBytes,
	long WorkingSetAfterBytes,
	long ProcessPeakWorkingSetBytes,
	long OutputBytes
);

/// <summary>Captures one machine-readable T6.8 stress run.</summary>
internal sealed record StressReport(
	int SchemaVersion,
	string Profile,
	DateTimeOffset RecordedUtc,
	string? Commit,
	string? GrepAssemblyVersion,
	string OperatingSystem,
	string OSArchitecture,
	string ProcessArchitecture,
	string Framework,
	int ProcessorCount,
	string? CpuModel,
	bool ServerGC,
	string GCLatencyMode,
	long TotalAvailableMemoryBytes,
	string? HardwareInventorySha256,
	bool AllSucceeded,
	IReadOnlyList<StressScenarioResult> Results
);