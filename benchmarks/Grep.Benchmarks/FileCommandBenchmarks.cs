namespace Icod.Grep.Benchmarks;

using BenchmarkDotNet.Attributes;
using Icod.CommandFramework.Diagnostics;
using Icod.Grep;

/// <summary>Measures command-level filesystem workloads without process-startup noise.</summary>
[MemoryDiagnoser]
public sealed class FileCommandBenchmarks {
	private CommandContext? context;
	private StringWriter? error;
	private MemoryStream? output;
	private string[] arguments = Array.Empty<string>();
	private string? temporaryDirectory;

	/// <summary>Gets the filesystem workload name.</summary>
	[Params( "large-file", "many-small-files", "recursive-tree" )]
	public string Workload { get; set; } = string.Empty;

	/// <summary>Creates deterministic filesystem fixtures.</summary>
	[GlobalSetup]
	public void Setup() {
		this.temporaryDirectory = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			string.Concat(
				"Icod.Grep.Benchmarks-",
				Guid.NewGuid().ToString( "N" )
			)
		);
		Directory.CreateDirectory( this.temporaryDirectory );
		this.arguments = this.Workload switch {
			"large-file" => this.CreateLargeFileWorkload(),
			"many-small-files" => this.CreateManySmallFilesWorkload(),
			"recursive-tree" => this.CreateRecursiveTreeWorkload(),
			_ => throw new InvalidOperationException(
				string.Concat(
					"Unknown filesystem benchmark workload: ",
					this.Workload
				)
			)
		};

		this.output = new MemoryStream();
		this.error = new StringWriter(
			System.Globalization.CultureInfo.InvariantCulture
		);
		this.context = new CommandContext(
			"grep",
			new StringReader( string.Empty ),
			TextWriter.Null,
			this.error,
			new MemoryStream( Array.Empty<byte>(), writable: false ),
			this.output
		);
	}

	/// <summary>Runs the command against the prepared physical fixtures.</summary>
	[Benchmark]
	public Task<int> RunCommandAsync() {
		this.output!.SetLength( 0 );
		this.output.Position = 0;
		this.error!.GetStringBuilder().Clear();
		return Command.RunAsync(
			this.arguments,
			this.context!
		);
	}

	/// <summary>Removes temporary fixtures.</summary>
	[GlobalCleanup]
	public void Cleanup() {
		this.output?.Dispose();
		this.error?.Dispose();
		if (
			!string.IsNullOrWhiteSpace( this.temporaryDirectory )
			&& Directory.Exists( this.temporaryDirectory )
		) {
			Directory.Delete(
				this.temporaryDirectory,
				recursive: true
			);
		}
	}

	private string[] CreateLargeFileWorkload() {
		var scenario = ScenarioCatalog.Get( "ascii-sparse" ) with {
			RecordCount = 131072
		};
		var corpus = CorpusFactory.Create( scenario );
		var path = System.IO.Path.Combine(
			this.temporaryDirectory!,
			"large.txt"
		);
		File.WriteAllBytes( path, corpus.Input );
		return [ "-c", scenario.Pattern, path ];
	}

	private string[] CreateManySmallFilesWorkload() {
		var paths = new List<string>( 258 ) {
			"-c",
			"TARGET"
		};
		for ( var index = 0; 256 > index; index++ ) {
			var directory = System.IO.Path.Combine(
				this.temporaryDirectory!,
				string.Concat(
					"set-",
					(index / 32).ToString( "D2", System.Globalization.CultureInfo.InvariantCulture )
				)
			);
			Directory.CreateDirectory( directory );
			var path = System.IO.Path.Combine(
				directory,
				string.Concat(
					"file-",
					index.ToString( "D4", System.Globalization.CultureInfo.InvariantCulture ),
					".txt"
				)
			);
			var scenario = new BenchmarkScenario(
				"many-small-file",
				"basic",
				32,
				80,
				17,
				1,
				"TARGET",
				false
			);
			File.WriteAllBytes(
				path,
				CorpusFactory.Create( scenario ).Input
			);
			paths.Add( path );
		}
		return paths.ToArray();
	}

	private string[] CreateRecursiveTreeWorkload() {
		var root = System.IO.Path.Combine(
			this.temporaryDirectory!,
			"tree"
		);
		for ( var directoryIndex = 0; 32 > directoryIndex; directoryIndex++ ) {
			var directory = System.IO.Path.Combine(
				root,
				string.Concat(
					"d-",
					directoryIndex.ToString( "D2", System.Globalization.CultureInfo.InvariantCulture )
				)
			);
			Directory.CreateDirectory( directory );
			for ( var fileIndex = 0; 8 > fileIndex; fileIndex++ ) {
				var path = System.IO.Path.Combine(
					directory,
					string.Concat(
						"f-",
						fileIndex.ToString( "D2", System.Globalization.CultureInfo.InvariantCulture ),
						".txt"
					)
				);
				var scenario = new BenchmarkScenario(
					"recursive-file",
					"basic",
					32,
					80,
					17,
					1,
					"TARGET",
					false
				);
				File.WriteAllBytes(
					path,
					CorpusFactory.Create( scenario ).Input
				);
			}
		}
		return [ "-r", "-c", "TARGET", root ];
	}
}
