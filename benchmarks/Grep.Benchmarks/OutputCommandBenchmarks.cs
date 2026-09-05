namespace Icod.Grep.Benchmarks;

using BenchmarkDotNet.Attributes;
using Icod.CommandFramework.Diagnostics;
using Icod.Grep;

/// <summary>Measures output-heavy command paths without process-startup noise.</summary>
[MemoryDiagnoser]
public class OutputCommandBenchmarks {
	private CommandContext? context;
	private StringWriter? error;
	private MemoryStream? input;
	private byte[] inputBytes = Array.Empty<byte>();
	private MemoryStream? output;
	private string[] arguments = Array.Empty<string>();
	private string? previousGrepColors;

	/// <summary>Gets the output workload name.</summary>
	[Params(
		"dense-output",
		"prefix-heavy",
		"only-matching",
		"forced-color",
		"context-output",
		"line-buffered"
	)]
	public string Workload { get; set; } = string.Empty;

	/// <summary>Prepares deterministic input and output policy.</summary>
	[GlobalSetup]
	public void Setup() {
		var denseScenario = new BenchmarkScenario(
			"output-dense",
			"basic",
			4096,
			96,
			1,
			1,
			"TARGET",
			false
		);
		var sparseScenario = denseScenario with {
			Name = "output-context",
			MatchEvery = 16
		};
		var scenario = "context-output" == this.Workload
			? sparseScenario
			: denseScenario;
		this.inputBytes = CorpusFactory.Create( scenario ).Input;
		this.arguments = this.Workload switch {
			"dense-output" => [ "TARGET" ],
			"prefix-heavy" => [ "-H", "-n", "-b", "TARGET" ],
			"only-matching" => [ "-o", "TARGET" ],
			"forced-color" => [ "--color=always", "TARGET" ],
			"context-output" => [ "-B", "2", "-A", "2", "TARGET" ],
			"line-buffered" => [ "--line-buffered", "TARGET" ],
			_ => throw new InvalidOperationException(
				string.Concat(
					"Unknown output benchmark workload: ",
					this.Workload
				)
			)
		};
		this.previousGrepColors = Environment.GetEnvironmentVariable(
			"GREP_COLORS"
		);
		Environment.SetEnvironmentVariable(
			"GREP_COLORS",
			"ms=31:mc=31:sl=:cx=:fn=35:ln=32:bn=32:se=36:ne"
		);
		this.input = new MemoryStream(
			this.inputBytes,
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
		this.ValidateWorkload();
	}

	/// <summary>Runs the complete output-heavy command path.</summary>
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

	/// <summary>Releases benchmark streams and restores the environment.</summary>
	[GlobalCleanup]
	public void Cleanup() {
		Environment.SetEnvironmentVariable(
			"GREP_COLORS",
			this.previousGrepColors
		);
		this.input?.Dispose();
		this.output?.Dispose();
		this.error?.Dispose();
	}

	private void ValidateWorkload() {
		using var inputStream = new MemoryStream(
			this.inputBytes,
			writable: false
		);
		using var outputStream = new MemoryStream();
		using var error = new StringWriter(
			System.Globalization.CultureInfo.InvariantCulture
		);
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			TextWriter.Null,
			error,
			inputStream,
			outputStream
		);
		var status = Command.RunAsync(
			this.arguments,
			context
		).GetAwaiter().GetResult();
		if ( CommandExitCodes.Success != status ) {
			throw new InvalidOperationException(
				string.Concat(
					this.Workload,
					": output benchmark validation failed with status ",
					status.ToString(
						System.Globalization.CultureInfo.InvariantCulture
					),
					". Diagnostic: ",
					error.ToString()
				)
			);
		}
		if ( 0 == outputStream.Length ) {
			throw new InvalidOperationException(
				string.Concat(
					this.Workload,
					": output benchmark produced no bytes."
				)
			);
		}
	}
}
