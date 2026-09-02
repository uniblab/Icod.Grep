namespace Icod.Grep.Benchmarks;

using BenchmarkDotNet.Attributes;
using Icod.CommandFramework.Diagnostics;
using Icod.Grep;

/// <summary>Measures the command pipeline without process-startup noise.</summary>
[MemoryDiagnoser]
public sealed class CommandBenchmarks {
	private CommandContext? context;
	private StringWriter? error;
	private MemoryStream? input;
	private MemoryStream? output;
	private string[] arguments = Array.Empty<string>();

	/// <summary>Gets the benchmark scenario name.</summary>
	[ParamsSource( nameof( ScenarioNames ) )]
	public string ScenarioName { get; set; } = string.Empty;

	/// <summary>Gets the source-controlled scenario names.</summary>
	public IEnumerable<string> ScenarioNames =>
		ScenarioCatalog.All.Select(
			static scenario => scenario.Name
		);

	/// <summary>Prepares deterministic input for one benchmark case.</summary>
	[GlobalSetup]
	public void Setup() {
		var scenario = ScenarioCatalog.Get( this.ScenarioName );
		var corpus = CorpusFactory.Create( scenario );
		this.arguments = corpus.Arguments;
		this.input = new MemoryStream(
			corpus.Input,
			writable: false
		);
		this.output = new MemoryStream();
		this.error = new StringWriter(
			System.Globalization.CultureInfo.InvariantCulture
		);
		this.context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			TextWriter.Null,
			this.error,
			this.input,
			this.output
		);
	}

	/// <summary>Runs the complete command parse/compile/search/output-count path.</summary>
	[Benchmark]
	public Task<int> RunCommandAsync() {
		this.input!.Position = 0;
		this.output!.SetLength( 0 );
		this.output.Position = 0;
		this.error!.GetStringBuilder().Clear();
		return Command.RunAsync(
			this.arguments,
			this.context!
		);
	}

	/// <summary>Releases benchmark streams.</summary>
	[GlobalCleanup]
	public void Cleanup() {
		this.input?.Dispose();
		this.output?.Dispose();
		this.error?.Dispose();
	}
}
