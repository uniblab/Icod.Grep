# Icod.Grep 1.6.0 — T6.0 Reference Baseline Report

**Reference baseline:** `Icod.Grep 1.5.0` at `423c0e9623100492fa01b6e4d14c183761d111d7`  
**Candidate measured:** `Icod.Grep 1.6.0` infrastructure head `200706ece5a0f0c493f62e5661fec18c77bda15b`  
**Collection date:** 2026-09-02  
**Reference host:** physical Windows laptop documented by `hardware_inventory.txt`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating passes, baseline → candidate → candidate → baseline, 30-second inter-run cooldown  
**Status:** T6.0 measurement gate satisfied; first production optimization target selected

## 1. Purpose

T6.0 exists to identify measured performance and allocation bottlenecks before production optimization begins. The baseline and candidate in this collection intentionally have no material search-path difference: the `1.6.0` branch changes production version metadata while adding benchmark, workflow, and documentation infrastructure. This makes the pair useful for separating stable workload characteristics from host-state timing noise.

The resulting data is strong enough to choose the first optimization target.

## 2. Timing interpretation

Wall-clock timing on the laptop remains sensitive to run order, temperature, processor state, background activity, and other host effects even under BenchmarkDotNet and the alternating protocol. Because the baseline and candidate search implementations are effectively identical, measured baseline-versus-candidate percentage differences in this collection must **not** be treated as regressions or improvements.

Across the four observations for each case, representative timing ranges were:

| Benchmark | Four-run range |
| --- | ---: |
| BRE ASCII sparse | 132.5–162.2 ms |
| ERE ASCII dense | 145.5–206.3 ms |
| Fixed strings, 100 patterns | 185.7–338.3 ms |
| Fixed strings, 1,000 patterns | 903.3–1,017.7 ms |
| BRE long line | 682.8–886.0 ms |
| PCRE lookbehind | 6.0–7.4 ms |
| UTF-8 BRE sparse | 104.2–140.5 ms |
| BRE large physical file | 1.262–1.569 s |
| Many small physical files | 293.7–340.3 ms |
| Recursive tree | 353.6–398.9 ms |
| Record reader, 80-byte records | 2.537–3.410 ms |
| Record reader, 4 KiB records | 9.263–11.787 ms |
| Record reader, 256 KiB records | 18.004–23.780 ms |

These ranges are useful as a reference-host envelope. Narrow future performance claims require focused comparisons and repeated measurements; the allocation data below is considerably more stable.

## 3. Allocation findings

Managed allocation measurements were effectively invariant across baseline/candidate and across passes. Variation was generally zero and remained below roughly a quarter-percent even in the filesystem cases.

Representative allocation per operation:

| Benchmark | Allocated |
| --- | ---: |
| BRE ASCII sparse | 604.59 MB |
| ERE ASCII dense | 618.64 MB |
| Fixed strings, 100 patterns | 5.39 MB |
| Fixed strings, 1,000 patterns | 3.42 MB |
| BRE long line | 3,005.83 MB |
| PCRE lookbehind | 5.82 MB |
| UTF-8 BRE sparse | 462.86 MB |
| BRE large physical file | 4,835.96 MB |
| Many small physical files | 316.25 MB |
| Recursive tree | 316.67 MB |
| Record reader, 80-byte records | 6.38 MB |
| Record reader, 4 KiB records | 25.34 MB |
| Record reader, 256 KiB records | 53.54 MB |

The managed BRE/ERE path is therefore the dominant allocation hotspot in the initial T6 corpus.

## 4. Record-reader controls isolate the hotspot

Two benchmark pairs use directly comparable record shapes and make the distinction especially clear.

### 4.1 Short-record corpus

The `ascii-sparse` BRE workload uses 16,384 records of roughly 80 bytes. The 80-byte `RecordReaderBenchmarks` case uses the same record-count/record-length scale.

- Record reader: **6.38 MB allocated**, 2.537–3.410 ms.
- BRE command path: **604.59 MB allocated**, 132.5–162.2 ms.

The full BRE path allocates roughly **95×** as much managed memory as record materialization alone.

### 4.2 Long-record corpus

The `long-line` BRE workload uses 32 records of roughly 256 KiB. The 256 KiB record-reader case uses the same record-count/record-length scale.

- Record reader: **53.54 MB allocated**, 18.004–23.780 ms.
- BRE command path: **3,005.83 MB allocated**, 682.8–886.0 ms.

The full BRE path allocates roughly **56×** as much managed memory as record materialization alone.

These controls rule out the materializing `ByteRecordReader` as the primary explanation for the multi-hundred-megabyte and multi-gigabyte command allocations. Record materialization remains worth improving later, especially for long records, but it is not the first-order T6 bottleneck.

## 5. Managed regular-expression architecture explains the measurements

`Icod.Grep.RegularExpressionPattern.Find` delegates each BRE/ERE search to `ICompiledRegularExpression.Match` in `Icod.CommandFramework`.

The current shared managed regular-expression implementation performs several allocation-heavy operations for each search:

1. Byte input is decoded into a fresh `RegexInput` representation, including rune, opacity, and source-index storage.
2. Unanchored `Search` attempts the expression at successive possible input-unit starting positions.
3. Each start constructs a fresh `RegexMatchState`.
4. Literal/dot/class nodes advance by producing new states.
5. Sequence matching builds fresh `List<RegexMatchState>` and `HashSet<RegexMatchState>` instances as it advances through nodes.
6. Capture-bearing states clone capture arrays when captures change.

A long no-match record therefore combines full-record decoding with a very large number of object-heavy start attempts. This architecture is consistent with both the measured allocation magnitude and the particularly poor long-line/large-file behavior.

The T6 optimization should address this in the shared managed regex engine rather than adding Grep-specific semantic shortcuts that duplicate BRE/ERE parsing rules.

## 6. Fixed-string and PCRE observations

The original roadmap expected fixed-string multi-pattern scalability to be the first likely target. The measurements refine that priority rather than eliminating it.

The fixed-string cases allocate only a few megabytes, but elapsed time grows substantially with pattern count. Because the 1,000-pattern corpus uses half as many records as the 100-pattern corpus yet still takes roughly 3–5× longer overall, the current independent-pattern dispatch remains a genuine CPU-scaling target. It should be retained as T6.1, but executed after the managed regex work.

PCRE is also informative: the lookbehind workload allocates only about 5.82 MB and completes in approximately 6–7 ms on its corpus. This is not an apples-to-apples matcher comparison, but it further demonstrates that the extraordinary allocation behavior is specific to the managed BRE/ERE path rather than an unavoidable property of `Icod.Grep` record processing.

## 7. T6 execution-order decision

T6.0 is complete. The measured execution order is now:

1. **T6.2 — Managed BRE/ERE matcher dispatch and regex hot paths.**
2. **T6.1 — Fixed-string / multi-pattern scalability.**
3. Re-measure before selecting among T6.3 through T6.8.

The tranche identifiers are retained to preserve roadmap/history continuity; only execution priority changes.

## 8. T6.2 initial objectives

T6.2 should begin in the shared `Icod.CommandFramework.RegularExpressions` implementation and should be benchmark-driven. Initial investigation priorities are:

- establish direct managed-regex microbenchmarks for literal hit/miss, short/long input, BRE/ERE, captures, alternation, repetition, and UTF-8;
- reduce per-start `RegexMatchState` construction where semantics permit;
- reduce `List`/`HashSet` churn in deterministic sequence/literal paths;
- derive safe mandatory-prefix or first-unit search information from the parsed AST so unanchored matching need not invoke the complete expression at every input unit;
- evaluate reusable/prepared decoded input without weakening the public byte-offset contract;
- preserve exact leftmost/longest, capture, locale, malformed-input, cancellation, and match-state-limit behavior; and
- validate each optimization against the existing `Icod.CommandFramework` regex suite plus the complete `Icod.Grep 1.5.0` conformance suite.

The first implementation step should optimize only semantics that can be proven equivalent. General backtracking/state-machine redesign is not required to obtain useful early wins.

## 9. Measurement-protocol follow-up

The reference harness itself remains useful, but this collection exposed two improvements desirable before using it to make narrow optimization claims:

- both compared variants should be built from fresh sibling detached worktrees rather than comparing one fresh worktree to the developer checkout; and
- native BenchmarkDotNet output emitted inside the PowerShell function should be sent directly to the host so `comparison.json` contains only the intended ordered run records.

Future T6.2 comparisons should also prefer focused benchmark filters with repeated alternating passes rather than rerunning the entire hour-scale corpus for every small optimization.

## 10. T6.0 closure

The T6.0 exit questions can now be answered:

- **Where is time being spent?** The managed BRE/ERE matcher is a dominant cost on ordinary and long records; fixed multi-pattern dispatch is also CPU-sensitive at high pattern counts.
- **Where are allocations concentrated?** Overwhelmingly in the managed BRE/ERE matching path, not in record reading alone.
- **Which workloads scale poorly?** Managed BRE/ERE no-match scanning on long records/large files, and fixed-string matching as pattern count grows.
- **Which component should be attacked first?** `Icod.CommandFramework.RegularExpressions`, exercised through `Icod.Grep` BRE/ERE workloads.

Accordingly, T6.0 is **closed** and T6.2 is the first production optimization tranche.
