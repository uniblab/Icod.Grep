namespace Icod.Grep.Benchmarks;

using System.Text;
using BenchmarkDotNet.Attributes;
using Icod.CommandFramework.Diagnostics;
using Icod.Grep;

/// <summary>Measures PCRE-specific command workloads without process-startup noise.</summary>
[MemoryDiagnoser]
public class PcreCommandBenchmarks {
	private CommandContext? context;
	private StringWriter? error;
	private MemoryStream? input;
	private byte[] inputBytes = Array.Empty<byte>();
	private MemoryStream? output;
	private string[] arguments = Array.Empty<string>();

	/// <summary>Gets the PCRE workload name.</summary>
	[Params(
		"literal",
		"lookbehind",
		"backreference",
		"unicode-property",
		"dense-only-matching",
		"multi-pattern",
		"long-record"
	)]
	public string Workload { get; set; } = string.Empty;

	/// <summary>Prepares deterministic PCRE input and validates the workload.</summary>
	[GlobalSetup]
	public void Setup() {
		(this.inputBytes, this.arguments) = this.CreateWorkload();
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

	/// <summary>Runs the complete PCRE command path.</summary>
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

	private (byte[] Input, string[] Arguments) CreateWorkload() {
		return this.Workload switch {
			"literal" => CreateCatalogWorkload(
				new BenchmarkScenario(
					"pcre-literal",
					"perl",
					16_384,
					80,
					17,
					1,
					"TARGET",
					false
				),
				[ "-P", "-c", "TARGET" ]
			),
			"lookbehind" => CreateCatalogWorkload(
				new BenchmarkScenario(
					"pcre-lookbehind",
					"perl",
					16_384,
					80,
					17,
					1,
					"TARGET",
					false
				),
				[ "-P", "-c", "(?<=prefix-)TARGET" ]
			),
			"backreference" => (
				CreateRepeatedInput(
					16_384,
					static index => 0 == index % 17
						? "record TARGETTARGET payload"
						: "record ordinary payload"
				),
				[ "-P", "-c", "(TARGET)\\1" ]
			),
			"unicode-property" => (
				CreateRepeatedInput(
					16_384,
					static index => 0 == index % 17
						? "Καλημέρα 世界 TARGET payload"
						: "Καλημέρα ordinary payload"
				),
				[ "-P", "-c", "\\p{L}+ 世界" ]
			),
			"dense-only-matching" => CreateCatalogWorkload(
				new BenchmarkScenario(
					"pcre-dense-only-matching",
					"perl",
					4_096,
					96,
					1,
					1,
					"TARGET",
					false
				),
				[ "-P", "-o", "TARGET" ]
			),
			"multi-pattern" => CreateCatalogWorkload(
				new BenchmarkScenario(
					"pcre-multi-pattern",
					"perl",
					4_096,
					96,
					17,
					1,
					"TARGET",
					false
				),
				CreateMultiPatternArguments()
			),
			"long-record" => CreateCatalogWorkload(
				new BenchmarkScenario(
					"pcre-long-record",
					"perl",
					32,
					262_144,
					4,
					1,
					"TARGET",
					false
				),
				[ "-P", "-c", "TARGET" ]
			),
			_ => throw new InvalidOperationException(
				string.Concat(
					"Unknown PCRE benchmark workload: ",
					this.Workload
				)
			)
		};
	}

	private static (byte[] Input, string[] Arguments) CreateCatalogWorkload(
		BenchmarkScenario scenario,
		string[] arguments
	) {
		return (
			CorpusFactory.Create( scenario ).Input,
			arguments
		);
	}

	private static byte[] CreateRepeatedInput(
		int recordCount,
		Func<int, string> createRecord
	) {
		ArgumentNullException.ThrowIfNull( createRecord );
		using var memory = new MemoryStream();
		for ( var index = 0; index < recordCount; index++ ) {
			var bytes = Encoding.UTF8.GetBytes(
				createRecord( index )
			);
			memory.Write( bytes );
			memory.WriteByte( (byte)'\n' );
		}
		return memory.ToArray();
	}

	private static string[] CreateMultiPatternArguments() {
		var arguments = new List<string> {
			"-P",
			"-c"
		};
		for ( var index = 0; index < 31; index++ ) {
			arguments.Add( "-e" );
			arguments.Add(
				string.Concat(
					"NO_MATCH_",
					index.ToString(
						"D2",
						System.Globalization.CultureInfo.InvariantCulture
					)
				)
			);
		}
		arguments.Add( "-e" );
		arguments.Add( "TARGET" );
		return arguments.ToArray();
	}

	private void ValidateWorkload() {
		using var input = new MemoryStream(
			this.inputBytes,
			writable: false
		);
		using var output = new MemoryStream();
		using var error = new StringWriter(
			System.Globalization.CultureInfo.InvariantCulture
		);
		var context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			TextWriter.Null,
			error,
			input,
			output
		);
		var status = Command.RunAsync(
			this.arguments,
			context
		).GetAwaiter().GetResult();
		if ( CommandExitCodes.Success != status ) {
			throw new InvalidOperationException(
				string.Concat(
					this.Workload,
					": PCRE benchmark validation failed with status ",
					status.ToString(
						System.Globalization.CultureInfo.InvariantCulture
					),
					". Diagnostic: ",
					error.ToString()
				)
			);
		}
		if ( 0 == output.Length ) {
			throw new InvalidOperationException(
				string.Concat(
					this.Workload,
					": PCRE benchmark produced no output."
				)
			);
		}
	}
}
