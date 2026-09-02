# Icod.Grep T6 performance benchmarks

This directory contains the measurement infrastructure for the `1.6.0` T6 performance and scalability tranche.

## Measurement policy

The authoritative quantitative series is collected on the physical Windows reference laptop documented by the repository's `hardware_inventory.txt`. The inventory itself is **not copied into benchmark artifacts**. Reports record only the inventory filename and SHA-256 digest so a result can be tied to the reference-machine declaration without duplicating serial numbers or other machine-specific inventory fields.

GitHub-hosted Windows, Linux, and macOS measurements are diagnostic only. They prove portability and can reveal gross regressions, but they are not used for narrow percentage claims or a cross-platform aggregate score.

## Benchmark project

`Grep.Benchmarks/Icod.Grep.Benchmarks.csproj` is intentionally outside `Icod.Grep.sln` and is not packable. Normal tool builds and NuGet packaging therefore do not acquire BenchmarkDotNet or benchmark sources.

The source-controlled `scenarios.json` catalog describes deterministic command workloads. The first T6.0 catalog covers:

- sparse and dense ASCII records;
- UTF-8 multilingual records;
- multi-MiB long-line pressure;
- fixed-string pattern-count scaling at 100 and 1,000 patterns; and
- PCRE lookbehind.

`CommandBenchmarks` exercises the complete in-process command parse/compile/search/count path without process-startup noise. `RecordReaderBenchmarks` separately measures the shared materializing record reader for short through long records.

## Fast smoke

Run the deterministic, non-statistical smoke from the repository root:

```powershell
dotnet restore benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj
dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Staging -- --smoke
```

The PR workflow runs this smoke on Windows, Linux, and macOS. It validates scenario generation and expected grep results; it does not establish performance numbers.

## Hosted diagnostic benchmarks

The manual `performance-diagnostics.yaml` workflow runs BenchmarkDotNet on hosted Windows, Linux, and macOS runners and uploads the results plus metadata. Those measurements are observational.

Locally, a normal BenchmarkDotNet run can be filtered in the usual way, for example:

```powershell
dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release -- --filter "*CommandBenchmarks*"
```

## Authoritative 1.5.0 → candidate comparison

On the physical Windows reference laptop, use:

```powershell
./benchmarks/Collect-ReferenceComparison.ps1
```

The script:

1. requires a clean candidate worktree by default;
2. creates a detached worktree at the pinned `1.5.0` merge commit;
3. copies the **current benchmark harness** into that baseline worktree so baseline and candidate use identical benchmark code;
4. runs BenchmarkDotNet for baseline and candidate;
5. writes separate metadata files and raw BenchmarkDotNet artifacts beneath `artifacts/performance/reference-comparison/`;
6. records the SHA-256 digest of `hardware_inventory.txt`, not its contents; and
7. removes the temporary baseline worktree.

Use `-Filter` to narrow the benchmark group. Do not compare results gathered under materially different power, thermal, runtime, or workload conditions as though they were one series.

T6 optimization work does not begin until this harness has captured a credible `1.5.0` reference series and identified the measured bottlenecks worth addressing.
