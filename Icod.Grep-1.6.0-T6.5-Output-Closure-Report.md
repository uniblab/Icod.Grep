# Icod.Grep 1.6.0 — T6.5 Output / Formatting / Color Closure Report

**Baseline:** `ff31a25dc745d4f71a752d05708545d48c939007`  
**Candidate:** `2a784219e90edbbdb2d8a8d6d75633b709720859`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating passes, baseline → candidate → candidate → baseline, 30-second inter-run cooldown  
**Status:** Candidate 1 accepted; T6.5 closed; T6.7 selected next

## 1. Baseline finding

T6.5 first added deterministic output-heavy BenchmarkDotNet workloads rather than optimizing output code without measurement. The same-code ABBA baseline established stable allocation envelopes for dense output, prefixes, `-o`, forced color, context, and line-buffered output.

The clearest residual was forced color. On the same 4,096-record dense corpus:

- ordinary dense output allocated **10.94 MB**;
- forced color allocated **21.63 MB**.

The production path explained the difference: each record had already been prepared for managed BRE selection, but selected colored rendering called `PatternSet.Prepare(...)` again before enumerating highlighted spans.

## 2. Candidate 1

Candidate 1 reuses the already-existing per-record `PatternInput` when a currently processed selected or trailing-context record is rendered with color.

Retained before-context records do not retain prepared input. They pass `null` and continue to use the original prepare-on-demand behavior. This avoids extending prepared-input lifetime or adding cross-record caches.

A focused inverted-match color/context test protects the important edge where a selected non-match is surrounded by context records that do contain the pattern and therefore still require independent context-match highlighting.

The full Windows/Linux/macOS, benchmark-smoke, package-smoke, and archive-smoke PR matrix passed at Candidate 1.

## 3. Physical result

| Workload | Baseline mean | Candidate mean | Time change | Baseline allocation | Candidate allocation | Allocation change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| context-output | 5.555 ms | 5.690 ms | +2.43% | 10.30 MB | 10.30 MB | flat |
| dense-output | 4.913 ms | 4.635 ms | -5.65% | 10.94 MB | 10.94 MB | flat |
| forced-color | 12.486 ms | 7.986 ms | **-36.04%** | 21.63 MB | 12.91 MB | **-8.72 MB / -40.31%** |
| line-buffered | 5.228 ms | 4.817 ms | -7.85% | 10.94 MB | 10.94 MB | flat |
| only-matching | 8.253 ms | 8.107 ms | -1.77% | 12.38 MB | 12.38 MB | flat |
| prefix-heavy | 6.539 ms | 7.180 ms | +9.80% | 11.99 MB | 11.99 MB | flat |

Allocation is the primary acceptance signal because the same-code T6.5 baseline already demonstrated meaningful host-state timing variation. Candidate 1 nevertheless produces a large directional timing improvement on the targeted forced-color workload while every non-target allocation control remains exactly unchanged.

The prefix-heavy timing movement is not accompanied by any allocation or production-path change specific to prefixes and lies within the type of timing variability observed during the same-code reference run; it is not treated as a regression claim.

## 4. Acceptance

Candidate 1 is accepted because:

- forced-color allocation falls by **8.72 MB / 40.31%**;
- forced-color mean elapsed time improves by roughly **36%**;
- all five non-target workloads are allocation-identical;
- exact GNU color bytes remain covered by existing tests;
- inverted-match context highlighting is explicitly guarded; and
- the complete CI/package/archive matrix is green.

## 5. Why T6.5 closes here

After Candidate 1, the remaining output-specific allocation differences are much smaller:

- dense output: 10.94 MB;
- prefix-heavy: 11.99 MB;
- only-matching: 12.38 MB;
- forced color: 12.91 MB.

Possible follow-on work remains identifiable — one-byte temporary arrays, numeric prefix formatting, `FindAll` list materialization, repeated SGR encoding, and Windows text-output translation — but the measured residual is now distributed across several mechanisms rather than dominated by one redundant full-record preparation.

A second candidate would therefore trade additional implementation complexity for substantially smaller expected returns than Candidate 1. Those micro-optimizations are better retained as future work unless later whole-suite measurements elevate one of them into a material residual.

T6.5 is closed.

## 6. Next tranche: T6.7 PCRE-specific profiling

T6.7 is selected next. PCRE uses a different engine and lifetime model from managed BRE/ERE, so it should be measured independently rather than inferred from the managed-regex improvements.

The first T6.7 step is benchmark expansion, not production modification. Add deterministic PCRE workloads covering:

1. a simple literal/control case;
2. lookbehind/lookaround;
3. backreferences;
4. Unicode-property matching under UTF-8;
5. dense `-o` enumeration;
6. multiple PCRE patterns; and
7. a long-record PCRE workload.

The benchmark foundation should distinguish compile/setup effects from repeated matching where practical and validate expected command output before measurement. Production PCRE changes should occur only if the resulting physical baseline identifies an avoidable cost that the public PCRE.NET surface can address safely.
