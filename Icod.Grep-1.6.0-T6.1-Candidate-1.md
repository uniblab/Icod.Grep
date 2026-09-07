# Icod.Grep 1.6.0 — T6.1 Candidate 1 Physical Comparison

**Baseline:** `Icod.Grep 1.5.0` at `423c0e9623100492fa01b6e4d14c183761d111d7`  
**Candidate:** `84b62dde0d93779c64535e55dd1d060c6dfe3e97`  
**Reference host inventory SHA-256:** `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** baseline pass 1 → candidate pass 1 → candidate pass 2 → baseline pass 2, 30-second cooldown  
**Filter:** `*fixed*`  
**Status:** primary T6.1 performance gate passed; control gate remains

## 1. Purpose

Candidate 1 replaces independent case-sensitive multi-pattern `-F` scans with the conservative immutable fixed-string multi-pattern accelerator defined in `Icod.Grep-1.6.0-T6.1-Fixed-String-Scalability-Plan.md`.

The primary acceptance questions are:

- does `fixed-1000` improve decisively;
- does `fixed-100` improve materially;
- do command results remain identical; and
- is any allocation change acceptable in context.

The complete semantic/CI gate was already green before this physical run.

## 2. Authoritative AB/BA results

| Workload | Baseline pass 1 | Baseline pass 2 | Candidate pass 1 | Candidate pass 2 | Baseline mean | Candidate mean | Mean reduction | Speedup |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `fixed-100` | 325.6 ms | 323.5 ms | 5.284 ms | 4.928 ms | 324.55 ms | 5.106 ms | **98.43%** | **63.6×** |
| `fixed-1000` | 1,371.0 ms | 1,433.2 ms | 4.186 ms | 4.035 ms | 1,402.1 ms | 4.1105 ms | **99.71%** | **341.1×** |

The result is repeated across both alternating passes and is far outside the timing-noise envelope established by T6.0.

## 3. Allocation results

| Workload | Baseline | Candidate | Delta |
| --- | ---: | ---: | ---: |
| `fixed-100` | 5.39 MB | 5.19 MB | **-3.7%** |
| `fixed-1000` | 3.46 MB | 4.30 MB | **+24.3%** |

The `fixed-1000` percentage increase is visible and must be recorded rather than hidden. In absolute terms it is approximately **0.84 MB per complete command invocation**.

The benchmark measures `Command.RunAsync` end-to-end, including parsing and pattern compilation on every benchmark operation. Candidate 1 therefore rebuilds the immutable 1,000-pattern automaton within each measured operation. The additional allocation is principally consistent with command-time compiled matcher construction rather than a per-record search-loop explosion.

Given the roughly **341×** mean throughput improvement in the 1,000-pattern workload, the additional sub-megabyte command-compilation allocation is acceptable for Candidate 1, subject to the remaining control gate.

## 4. Stability

Pass-to-pass timing spread remains small relative to the magnitude of the improvement:

- `fixed-100` baseline spread: ~0.65%; candidate spread: ~6.97%;
- `fixed-1000` baseline spread: ~4.44%; candidate spread: ~3.67%.

The candidate remains orders of magnitude faster in both passes.

## 5. Interpretation

Candidate 1 changes fixed-string multi-pattern scaling from effectively repeated independent record scans toward compiled multi-pattern dispatch.

The important observation is not merely that `fixed-1000` became faster than its baseline. The candidate now completes `fixed-1000` in approximately the same few-millisecond range as `fixed-100`. That is exactly the scalability behavior T6.1 was intended to obtain.

No semantic compromise was required: the accelerator remains restricted to the proven-equivalent case-sensitive multi-pattern `-F` scope, while `-i`, `-w`, `-x`, and empty-pattern cases retain the established implementation.

## 6. Candidate 1 decision

The **primary T6.1 performance gate passes decisively**.

Candidate 1 is retained.

The remaining T6.1 closure work is a control comparison verifying that the `PatternInput` value-type cleanup and fixed matcher integration do not materially regress unrelated paths. The agreed controls are:

- an ordinary single-pattern fixed-string command;
- PCRE;
- managed BRE; and
- record-reader behavior.

After the control gate, T6.1 can be formally closed and the whole benchmark suite can be re-evaluated to select the next measured T6 target.

## 7. Collector note

`comparison.json` still contains native BenchmarkDotNet console output captured inside sequence entries in addition to the intended run records. This is a known harness-cleanliness issue already identified by the T6.0 report. It does not affect the authoritative BenchmarkDotNet CSV measurements or per-run metadata used for this decision, but the collector should eventually be cleaned so `Sequence` contains only structured run records.
