# Icod.Grep 1.6.0 — T6.7 PCRE Baseline and Candidate 1 Plan

**Measured commit:** `6524f943b6223c36aaed270f8b1133d8537009f1`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating same-code passes, ABBA ordering, 30-second cooldown  
**PCRE dependency:** PCRE.NET 1.6.0 / PCRE2 10.48  
**Status:** baseline accepted; Candidate 1 selected

## 1. Benchmark correction

The first T6.7 physical attempt exposed a benchmark-policy defect rather than a Grep defect: the Unicode-property workload inherited the reference host's default `C` byte locale and therefore correctly ran without PCRE UTF/UCP semantics.

The benchmark was corrected so only the Unicode-property workload enters `LC_ALL=C.UTF-8`, restores the caller's environment afterward, and is covered by the fast cross-platform benchmark smoke. The corrected CI matrix is green.

## 2. Corrected same-code physical baseline

All seven workloads now validate in all four executions. Allocation is highly repeatable.

| Workload | Four-run mean time | Allocation |
| --- | ---: | ---: |
| backreference | 9.06 ms | 3.49 MB |
| dense `-o` | 8.13 ms | 2.59 MB |
| literal | 10.43 ms | 4.35 MB |
| long record | 19.62 ms | 45.58 MB |
| lookbehind | 10.61 ms | 4.34 MB |
| 32 patterns | **54.64 ms** | **1.21 MB** |
| Unicode property | 11.61 ms | 3.71 MB |

The literal, lookbehind, backreference, and Unicode-property cases are all in the same broad CPU envelope. Dense `-o` is modest, and the long-record workload's larger allocation is explained principally by the existing multi-segment record materialization path rather than PCRE-specific per-match allocation.

The standout residual is the 32-pattern case: it is strongly CPU-bound while allocating very little. The current Grep implementation owns 32 independent `PcreRegex8Bit` expressions and deliberately evaluates them independently so leftmost/longest selection semantics remain explicit.

## 3. Why Candidate 1 is JIT rather than pattern combination

Combining independent `-P` expressions into a synthetic alternation would require proving equivalence for ordering, captures, lookarounds, backreferences, control verbs, zero-length behavior, and Grep's leftmost/longest selection rules. That is not an appropriate first performance candidate.

PCRE.NET 1.6.0 already exposes the conservative optimization intended for this exact workload: `PcreOptions.Compiled`, which maps to PCRE2 JIT complete matching. PCRE.NET recommends JIT when the same compiled pattern is matched repeatedly.

Grep compiles each PCRE expression once and then applies it across thousands of records, so the setup/runtime tradeoff is favorable by construction.

## 4. Candidate 1 contract

Candidate 1 will enable `PcreOptions.Compiled` for Grep's `PcreRegex8Bit` expressions.

It SHALL:

1. retain one independent PCRE2 expression per user pattern;
2. preserve `Caseless`, UTF, UCP, `MatchInvalidUtf`, and `AsciiBsD` behavior exactly;
3. preserve all Grep pattern ordering, leftmost/longest selection, `-o`, `-w`, `-x`, cancellation, malformed-UTF, and binary-policy semantics;
4. make no use of `PcreMatchOptions.NoUtfCheck` because Grep must continue to accept and classify malformed input according to its existing contract;
5. make no change to managed BRE/ERE or fixed-string paths; and
6. fall back only according to PCRE.NET/PCRE2's normal JIT support behavior rather than introducing a second matching implementation.

## 5. Acceptance gate

Run the complete `PcreCommandBenchmarks` matrix against this corrected baseline.

Primary workload:

- 32-pattern PCRE.

Secondary workloads:

- literal;
- lookbehind;
- backreference;
- Unicode property;
- dense `-o`;
- long record.

Candidate 1 is accepted if repeated physical measurements show a material CPU improvement in the multi-pattern workload without semantic regression or a meaningful slowdown in the single-pattern controls. Allocation is expected to remain broadly similar because JIT primarily changes native execution strategy rather than Grep's managed ownership model.

A later Candidate 2 may evaluate PCRE.NET's reusable match-buffer API only if Candidate 1 leaves a material managed-allocation residual worth the additional lifetime/disposal complexity.
