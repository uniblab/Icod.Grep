namespace Icod.Grep.Benchmarks;

using System.Text;

/// <summary>Generates deterministic benchmark input and argument vectors.</summary>
internal static class CorpusFactory {
	private const string MatchToken = "TARGET";

	internal sealed record GeneratedCorpus(
		byte[] Input,
		string[] Arguments,
		int ExpectedMatchCount
	);

	internal static GeneratedCorpus Create( BenchmarkScenario scenario ) {
		ArgumentNullException.ThrowIfNull( scenario );

		var encoding = new UTF8Encoding(
			encoderShouldEmitUTF8Identifier: false
		);
		using var memory = new MemoryStream();
		for ( var index = 0; scenario.RecordCount > index; index++ ) {
			var selected = 0 == index % Math.Max( 1, scenario.MatchEvery );
			var line = CreateLine(
				scenario,
				index,
				selected
			);
			var bytes = encoding.GetBytes( line );
			memory.Write( bytes );
			memory.WriteByte( (byte)'\n' );
		}

		return new GeneratedCorpus(
			memory.ToArray(),
			CreateArguments( scenario ),
			scenario.ExpectedMatchCount
		);
	}

	private static string CreateLine(
		BenchmarkScenario scenario,
		int index,
		bool selected
	) {
		var prefix = scenario.Utf8
			? string.Concat(
				"Καλημέρα 世界 record-",
				index.ToString( "D8", System.Globalization.CultureInfo.InvariantCulture ),
				" "
			)
			: string.Concat(
				"record-",
				index.ToString( "D8", System.Globalization.CultureInfo.InvariantCulture ),
				" "
			);
		var marker = selected
			? string.Concat( "prefix-", MatchToken, " " )
			: "ordinary-data ";
		var minimumLength = prefix.Length + marker.Length;
		var targetLength = Math.Max(
			minimumLength,
			scenario.RecordLength
		);
		return string.Concat(
			prefix,
			marker,
			CreatePadding( targetLength - minimumLength )
		);
	}

	private static string CreatePadding( int count ) {
		if ( 0 >= count ) {
			return string.Empty;
		}
		var builder = new StringBuilder( count );
		const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
		for ( var index = 0; count > index; index++ ) {
			builder.Append(
				alphabet[ index % alphabet.Length ]
			);
		}
		return builder.ToString();
	}

	private static string[] CreateArguments( BenchmarkScenario scenario ) {
		var arguments = new List<string>();
		switch ( scenario.Matcher ) {
			case "basic":
				break;
			case "extended":
				arguments.Add( "-E" );
				break;
			case "fixed":
				arguments.Add( "-F" );
				break;
			case "perl":
				arguments.Add( "-P" );
				break;
			default:
				throw new InvalidOperationException(
					string.Concat(
						"Unknown matcher in benchmark scenario: ",
						scenario.Matcher
					)
				);
		}

		arguments.Add( "-c" );
		if ( 1 >= scenario.PatternCount ) {
			arguments.Add( scenario.Pattern );
			return arguments.ToArray();
		}

		for ( var index = 0; scenario.PatternCount - 1 > index; index++ ) {
			arguments.Add( "-e" );
			arguments.Add(
				string.Concat(
					"NO_MATCH_",
					index.ToString( "D5", System.Globalization.CultureInfo.InvariantCulture )
				)
			);
		}
		arguments.Add( "-e" );
		arguments.Add( scenario.Pattern );
		return arguments.ToArray();
	}
}
