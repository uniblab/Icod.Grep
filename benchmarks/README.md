# Icod.Grep T6 performance and scalability harness

This directory contains the measurement and stress infrastructure used to develop and validate `Icod.Grep 1.6.0`.

## Measurement policy

The governing rule for T6 is:

> Measure first. Optimize second. Preserve behavior always.

Narrow percentage claims come from repeated measurements on the established physical Windows reference host identified by `hardware_inventory.txt`. Benchmark artifacts record only its SHA-256 digest. GitHub-hosted Windows, Linux, and macOS runs are correctness, portability, orchestration, and smoke gates; their timings are not treated as authoritative physical performance results.

The immutable `1.5.0` baseline commit is:

```text
423c0e9623100492fa01b6e4d14c183761d111d7
```

## Benchmark project

`Grep.Benchmarks/Icod.Grep.Benchmarks.csproj` is intentionally outside `Icod.Grep.sln` and is not packable. Production packages therefore do not acquire BenchmarkDotNet or benchmark sources.

The project contains:

- `CommandBenchmarks` for complete in-process parse/compile/search/count workloads;
- `FileCommandBenchmarks` for large-file, many-small-file, and recursive-tree workloads;
- `RecordReaderBenchmarks` for record-pipeline controls;
- focused fixed-string, output/color, PCRE, and component benchmark groups added during T6;
- deterministic benchmark smoke;
- deterministic T6.8 stress smoke;
- explicit T6.8 physical scaling profiles; and
- explicit sustained cancellation/output-resilience profiles.

## Deterministic smoke

From the repository root:

```powershell
dotnet restore benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj
```

```powershell
dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Staging -- --smoke
```

The PR workflow runs the benchmark and stress smokes on Windows, Linux, and macOS. They validate scenario generation and expected command behavior; they are not statistical performance gates.

## Authoritative 1.5.0 → candidate comparison

`Collect-ReferenceComparison.ps1` is the authoritative whole-suite physical comparison collector.

The default protocol:

1. requires a clean candidate worktree apart from generated root-level `T6.*.zip` bundles;
2. pins the immutable `1.5.0` baseline commit;
3. creates the baseline under a short `%TEMP%` worktree path to avoid Windows long-path cleanup failures;
4. overlays the **current benchmark harness** onto the baseline so both variants use identical measurement code;
5. restores/builds each variant once in Release;
6. runs two alternating ABBA passes: baseline → candidate → candidate → baseline;
7. waits 30 seconds between variants by default;
8. runs the complete benchmark suite by default;
9. writes each pass beneath `artifacts/performance/reference-comparison/`;
10. records exact commits, ordered run sequence, cooldown, filter, and hardware-inventory hash in `comparison.json`; and
11. removes or prunes the temporary baseline worktree after collection.

The final T6.9 release-closure invocation is:

```powershell
powershell .\benchmarks\Collect-ReferenceComparison.ps1 -BaselineCommit '423c0e9623100492fa01b6e4d14c183761d111d7' -BaselineLabel 'baseline-1.5.0' -Filter '*' -OutputDirectory 'artifacts/performance/T6.9-final-reference' -Passes 2 -CooldownSeconds 30
```

Timing is interpreted conservatively because the physical baseline work established measurable run-order/noise effects. Managed allocation is substantially more repeatable and is the primary signal for small resource deltas.

## T6.8 physical scaling

`Collect-StressReference.ps1` collects the explicit physical S1-S3 scaling profiles:

- `records` — 1/16/64 MiB single records across fixed/BRE/ERE/PCRE plus fixed short-record files through 1 GiB;
- `files` — deterministic nested trees through 50,000 files; and
- `patterns` — fixed sets through 10,000 and BRE/ERE/PCRE sets through 1,000.

Example focused collection:

```powershell
powershell .\benchmarks\Collect-StressReference.ps1 -Profiles records -OutputDirectory 'artifacts/performance/T6.8-records' -CooldownSeconds 0
```

The initial S1 run exposed approximately 23× input-size cumulative managed allocation for very large BRE/ERE byte-mode records. The root cause was corrected in `Icod.CommandFramework 2.2.1`; the Grep consumer rerun reduced 64 MiB BRE/ERE allocation by about 39% and brought amplification to approximately 14× while retaining linear growth.

## T6.8 resilience

`Collect-StressResilience.ps1` collects sustained S4/S5 operational behavior on the physical reference host.

```powershell
powershell .\benchmarks\Collect-StressResilience.ps1 -OutputDirectory 'artifacts/performance/T6.8-resilience'
```

The accepted run covers:

- cancellation during a 64 MiB BRE record;
- cancellation with 1,000 BRE patterns;
- cancellation during 10,000-file recursive traversal;
- cancellation during output-heavy operation;
- sustained delayed output through a zero-retention counting sink;
- deterministic write failure; and
- deterministic flush/completion failure.

All accepted S4 cancellation cases completed within the 2-second operational deadline. Backpressure remained bounded, and write/flush failures returned the controlled grep error status.

## Current status

T6.0 through T6.8 are closed. The remaining work is **T6.9 release closure**:

1. final whole-suite physical comparison against `1.5.0`;
2. residual-regression review;
3. final documentation and package metadata consolidation;
4. package/distribution workflow audit; and
5. merge/publish readiness decision for `Icod.Grep 1.6.0`.

Retained tranche/candidate/closure reports at the repository root are the authoritative quantitative record for individual optimizations.
