# Icod.Grep T6 performance benchmarks

This directory contains the measurement infrastructure for the `1.6.0` T6 performance and scalability tranche.

## T6.0 status

The benchmark foundation is implemented and its deterministic smoke has passed on GitHub-hosted Windows, Linux, and macOS. The development version surfaces are synchronized to `1.6.0` while the comparison script pins the immutable `1.5.0` merge commit as the baseline.

The first physical-reference pilot was collected on September 2, 2026. It successfully established that allocation measurements are highly repeatable, but it also exposed substantial run-order/timing variance despite there being no production search-code difference between the pinned `1.5.0` baseline and the pilot `1.6.0` candidate beyond version metadata. The reference protocol was therefore strengthened before accepting timing results as optimization evidence.

T6.0 remains measurement-first: production hot-path optimization must not begin until the stabilized comparison identifies the bottlenecks worth attacking.

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

`CommandBenchmarks` exercises the complete in-process command parse/compile/search/count path without process-startup noise. `FileCommandBenchmarks` exercises large-file, many-small-file, and recursive-tree workloads. `RecordReaderBenchmarks` separately measures the shared materializing record reader for short through long records.

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

The default authoritative protocol now:

1. requires a clean candidate worktree by default;
2. creates a detached worktree at the pinned `1.5.0` merge commit;
3. copies the **current benchmark harness** into that baseline worktree so baseline and candidate use identical benchmark code;
4. restores/builds each variant once;
5. runs two alternating passes in ABBA order: baseline → candidate → candidate → baseline;
6. waits 30 seconds between benchmark variants by default to reduce immediate thermal/run-order coupling;
7. runs all benchmark classes by default, including the record-reader microbenchmarks;
8. writes each pass to a separate result directory beneath `artifacts/performance/reference-comparison/`;
9. writes separate metadata files plus an ordered comparison manifest;
10. records the SHA-256 digest of `hardware_inventory.txt`, not its contents; and
11. removes the temporary baseline worktree.

Use `-Filter` to narrow the benchmark group, `-Passes` to increase the number of alternating passes, and `-CooldownSeconds` to change the interval between variants. Do not compare results gathered under materially different power, thermal, runtime, or workload conditions as though they were one series.

### Pilot finding

The initial one-pass reference run is retained as a measurement-methodology finding, not as a performance regression result. Baseline and candidate production search behavior were effectively identical, yet wall-clock deltas ranged from about **+66% to -29%** depending on the scenario. Allocation values, by contrast, were essentially identical between variants.

The stable allocation data also exposed a likely optimization priority: managed BRE/ERE command workloads allocate hundreds of megabytes on roughly megabyte-scale inputs, while analogous fixed-string workloads allocate only a few megabytes. Long-line and large-file BRE cases likewise show multi-gigabyte allocations. This suggests the managed regex/search path deserves focused profiling once the stabilized timing series is collected.

T6 optimization work does not begin until the strengthened reference protocol has produced a credible series and identified the measured bottlenecks worth addressing.
