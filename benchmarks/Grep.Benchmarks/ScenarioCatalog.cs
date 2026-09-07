namespace Icod.Grep.Benchmarks;

using System.Text.Json;

/// <summary>Loads the source-controlled T6 benchmark scenario catalog.</summary>
internal static class ScenarioCatalog {
	private static readonly Lazy<IReadOnlyList<BenchmarkScenario>> Scenarios = new(
		LoadScenarios
	);

	internal static IReadOnlyList<BenchmarkScenario> All => Scenarios.Value;

	internal static BenchmarkScenario Get( string name ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		foreach ( var scenario in All ) {
			if ( string.Equals( scenario.Name, name, StringComparison.Ordinal ) ) {
				return scenario;
			}
		}
		throw new ArgumentException(
			string.Concat( "Unknown benchmark scenario: ", name ),
			nameof( name )
		);
	}

	private static IReadOnlyList<BenchmarkScenario> LoadScenarios() {
		var path = System.IO.Path.Combine(
			AppContext.BaseDirectory,
			"scenarios.json"
		);
		var json = File.ReadAllText( path );
		var scenarios = JsonSerializer.Deserialize<List<BenchmarkScenario>>(
			json,
			new JsonSerializerOptions {
				PropertyNameCaseInsensitive = true
			}
		);
		if ( null == scenarios || 0 == scenarios.Count ) {
			throw new InvalidOperationException(
				"The benchmark scenario catalog is empty."
			);
		}

		var names = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var scenario in scenarios ) {
			if ( !names.Add( scenario.Name ) ) {
				throw new InvalidOperationException(
					string.Concat(
						"Duplicate benchmark scenario name: ",
						scenario.Name
					)
				);
			}
		}
		return scenarios;
	}
}
