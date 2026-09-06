namespace Icod.Grep.Benchmarks;

using System.Text;
using BenchmarkDotNet.Attributes;
using PCRE;

/// <summary>Measures PCRE.NET interpreted versus JIT-compiled matching directly.</summary>
[MemoryDiagnoser]
public sealed class PcreJitBenchmarks : IDisposable {
	private readonly List<PcreRegex8Bit> interpreted = new();
	private readonly List<PcreRegex8Bit> jitted = new();
	private readonly byte[] subject;

	/// <summary>Initializes the deterministic PCRE JIT benchmark state.</summary>
	public PcreJitBenchmarks() {
		this.subject = Encoding.ASCII.GetBytes(
			"record-00000000 ordinary-data abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz"
		);
		for ( var index = 0; index < 31; index++ ) {
			var pattern = string.Concat(
				"NO_MATCH_",
				index.ToString(
					"D2",
					System.Globalization.CultureInfo.InvariantCulture
				)
			);
			this.interpreted.Add(
				new PcreRegex8Bit(
					Encoding.ASCII.GetBytes( pattern ),
					Encoding.ASCII,
					PcreOptions.None
				)
			);
			this.jitted.Add(
				new PcreRegex8Bit(
					Encoding.ASCII.GetBytes( pattern ),
					Encoding.ASCII,
					PcreOptions.Compiled
				)
			);
		}
		this.interpreted.Add(
			new PcreRegex8Bit(
				"TARGET"u8,
				Encoding.ASCII,
				PcreOptions.None
			)
		);
		this.jitted.Add(
			new PcreRegex8Bit(
				"TARGET"u8,
				Encoding.ASCII,
				PcreOptions.Compiled
			)
		);
	}

	/// <summary>Measures all 32 interpreted PCRE expressions over one record.</summary>
	[Benchmark(Baseline = true)]
	public int Interpreted32Patterns() {
		var matches = 0;
		foreach ( var expression in this.interpreted ) {
			if ( expression.Match( this.subject ).Success ) {
				matches++;
			}
		}
		return matches;
	}

	/// <summary>Measures all 32 JIT-compiled PCRE expressions over one record.</summary>
	[Benchmark]
	public int Jitted32Patterns() {
		var matches = 0;
		foreach ( var expression in this.jitted ) {
			if ( expression.Match( this.subject ).Success ) {
				matches++;
			}
		}
		return matches;
	}

	/// <inheritdoc/>
	public void Dispose() {
		foreach ( var expression in this.interpreted ) {
			expression.Dispose();
		}
		foreach ( var expression in this.jitted ) {
			expression.Dispose();
		}
	}
}
