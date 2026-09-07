# Icod.Grep 1.6.0 — T6.7 PCRE Closure Report

**Baseline:** `6524f943b6223c36aaed270f8b1133d8537009f1`  
**Candidate:** `5dbcf2dd6bfa638c5b1cb9d272e39a712f1368d1`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two-pass alternating ABBA comparison with 30-second cooldown  
**Status:** Candidate 1 accepted; T6.7 closed; T6.8 selected next

## 1. Scope

T6.7 treated PCRE.NET 1.6.0 / PCRE2 10.48 separately from managed BRE/ERE and fixed-string matching. The benchmark matrix covered:

- a literal control;
- lookbehind;
- backreferences;
- Unicode-property matching under an explicit UTF-8 locale;
- dense `-o` enumeration;
- 32 independent PCRE patterns; and
- 262 KiB long records.

The Unicode-property workload was corrected after the first physical attempt revealed that the reference host's default `C` locale correctly disables UTF/UCP semantics. The final matrix explicitly enters `LC_ALL=C.UTF-8` only for that workload and restores the prior environment afterward.

## 2. Baseline finding

The corrected same-code physical baseline showed that ordinary single-pattern PCRE workloads were broadly clustered around 8–12 ms. The 32-pattern workload was the clear CPU outlier at roughly 53–55 ms while allocating only about 1.2 MB.

This made the residual a native execution-cost problem rather than a managed-allocation problem.

A focused PCRE.NET component benchmark then compared interpreted PCRE2 execution with `PcreOptions.Compiled`. Across four physical executions, JIT consistently required only about 44–48% of interpreted elapsed time and allocated no managed memory in either component path.

PCRE.NET 1.6.0's native wrapper requests JIT through `pcre2_jit_compile` but retains the ordinary compiled code if JIT cannot be produced, so requesting JIT does not introduce a new hard failure mode for unsupported patterns or hosts.

## 3. Candidate 1

Candidate 1 changes Grep's PCRE option initialization from:

```csharp
var options = PcreOptions.None;
```

to:

```csharp
var options = PcreOptions.Compiled;
```

All existing `Caseless`, UTF, UCP, `MatchInvalidUtf`, and `AsciiBsD` behavior remains unchanged. Grep continues to own one independent PCRE expression per user pattern and preserves its existing leftmost/longest selection logic.

The complete PR matrix passed on Windows, Linux, and macOS, including benchmark smoke, package smoke, and all six archive RIDs.

## 4. Physical Candidate 1 result

| Workload | Baseline mean | Candidate mean | Time change | Speedup | Baseline allocation | Candidate allocation |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| backreference | 8.884 ms | 5.208 ms | **-41.38%** | 1.71× | 3.49 MB | 3.49 MB |
| dense `-o` | 7.834 ms | 4.545 ms | **-41.99%** | 1.72× | 2.59 MB | 2.59 MB |
| literal | 10.564 ms | 6.604 ms | **-37.49%** | 1.60× | 4.35 MB | 4.35 MB |
| long record | 19.732 ms | 20.018 ms | +1.45% | 0.99× | 45.58 MB | 45.58 MB |
| lookbehind | 10.157 ms | 6.741 ms | **-33.64%** | 1.51× | 4.35 MB | 4.35 MB |
| 32 patterns | 53.020 ms | 27.321 ms | **-48.47%** | **1.94×** | 1.21 MB | 1.20 MB |
| Unicode property | 10.637 ms | 6.335 ms | **-40.44%** | 1.68× | 3.72 MB | 3.72 MB |

The long-record movement is small, allocation-identical, and consistent with that workload being dominated by record materialization and memory traffic rather than PCRE execution. It is not treated as a regression claim.

## 5. Acceptance

Candidate 1 is accepted because:

- the primary 32-pattern PCRE workload improves by **48.47%**, nearly doubling throughput;
- every ordinary PCRE control except the memory-dominated long-record case improves by roughly **34–42%**;
- managed allocation remains essentially unchanged;
- the production diff is one option-bit initialization;
- no pattern-combination or semantic rewrite was introduced;
- PCRE.NET preserves ordinary compiled-code fallback when JIT is unavailable; and
- the full cross-platform CI/package/archive matrix is green.

## 6. Why there is no T6.7 Candidate 2

The remaining managed allocations do not identify a compelling PCRE-specific residual. The literal/lookbehind allocations are in the same broad envelope as Grep's common per-record command pipeline, while the 32-pattern workload allocates only about 1.2 MB in total.

PCRE.NET's reusable match-buffer API could reduce some object creation, but the measured data no longer shows a PCRE-specific allocation problem large enough to justify the additional lifetime, disposal, and concurrency complexity in this release.

The long-record workload's 45.58 MB allocation is unchanged by JIT and is attributable principally to the already-known large-record materialization path rather than PCRE matching itself.

T6.7 therefore closes with Candidate 1.

## 7. Next tranche: T6.8 stress and resource behavior

T6.8 will stop looking for narrow hot-path wins and instead establish practical scaling behavior and failure characteristics for:

1. very large records and files;
2. tens of thousands of files;
3. very large fixed/BRE/ERE/PCRE pattern sets;
4. cancellation during sustained work;
5. output backpressure and output failure; and
6. constrained-memory / resource-pressure behavior where practical.

The first T6.8 step is a deterministic stress harness and documented limits matrix. Production changes will be made only when a stress case reveals avoidable growth, pathological behavior, or non-graceful failure.
