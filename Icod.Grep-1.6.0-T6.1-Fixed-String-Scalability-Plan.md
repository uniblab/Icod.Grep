# Icod.Grep 1.6.0 — T6.1 Fixed-String / Multi-Pattern Scalability Plan

**Baseline:** `Icod.Grep 1.5.0` at `423c0e9623100492fa01b6e4d14c183761d111d7`  
**Working branch:** `performance-scalability-1.6.0`  
**Stable regex dependency:** `Icod.CommandFramework 2.2.0`  
**Status:** implementation authorized after T6.2 closure

## 1. Purpose

T6.1 addresses the remaining fixed-string CPU-scaling problem identified by the T6.0 physical baseline. Managed BRE/ERE was correctly promoted ahead of this tranche and has now been addressed through `Icod.CommandFramework 2.2.0`; fixed-string dispatch is therefore the next measured Grep-local target.

The governing rule remains:

> Measure first. Optimize second. Preserve behavior always.

## 2. Measured problem

The T6.0 physical reference series showed that fixed-string matching is not an allocation hotspot, but it scales poorly with pattern count:

| Workload | Records | Patterns | Reference-host time envelope | Allocation |
| --- | ---: | ---: | ---: | ---: |
| fixed-100 | 8,192 | 100 | 185.7–338.3 ms | ~5.39 MB |
| fixed-1000 | 4,096 | 1,000 | 903.3–1,017.7 ms | ~3.42 MB |

The 1,000-pattern corpus processes only half as many records yet requires roughly 3–5× the elapsed time. The current architecture explains this: `PatternSet.Find` iterates every `IGrepPattern`, and each `FixedPattern` independently scans the record.

The deterministic benchmark corpus intentionally places `TARGET` last after `NO_MATCH_00000 ... NO_MATCH_NNNNN`, making the pattern-count scaling visible and repeatable.

## 3. Candidate 1 scope

Candidate 1 will introduce an immutable multi-pattern fixed-string searcher for the narrow case whose equivalence is straightforward:

- matcher mode is `-F`;
- more than one pattern exists;
- matching is case-sensitive;
- neither `-w` nor `-x` is active; and
- no pattern is empty.

All other fixed-string cases retain the existing `FixedPattern` implementation unchanged.

This deliberately excludes the difficult semantic cases from the first optimization:

- locale-aware / rune-aware `-i` case folding;
- `-w` candidate rejection and re-search semantics;
- `-x` whole-record semantics; and
- explicitly empty patterns, which select every record.

Those cases may receive separate proven accelerators later, but they are not prerequisites for fixing the measured 100/1,000-pattern bottleneck.

## 4. Data structure

The preferred Candidate 1 implementation is a compiled, immutable Aho-Corasick-style byte automaton constructed once when fixed patterns are compiled.

The automaton will contain only immutable search data after construction:

- trie transitions keyed by byte;
- failure links;
- output pattern lengths for accepting states; and
- the maximum pattern length needed for safe search termination decisions.

Search state is local to each `Find` invocation. No mutable per-record state is stored on the compiled pattern set, so concurrent use remains safe.

UTF-8 case-sensitive fixed strings are suitable for byte search because UTF-8 is self-synchronizing: a valid encoded pattern cannot begin on a continuation byte and accidentally represent the same valid pattern from the middle of another scalar value. Byte-mode locales likewise already use byte-oriented fixed matching.

## 5. Match-selection contract

For the accelerated scope, the combined matcher must reproduce the result that the current independent-pattern dispatcher produces:

1. choose the lowest source byte index at or after `startOffset`;
2. when multiple patterns begin at that byte index, choose the longest match;
3. return no match only when no pattern occurs at or after `startOffset`;
4. preserve byte indices and byte lengths exactly; and
5. observe cancellation at bounded intervals during long searches.

Because Candidate 1 is disabled for `-w` and `-x`, no post-match boundary rejection can invalidate a longer candidate while a shorter same-start candidate would have been accepted. That avoids a subtle semantic trap and keeps the proof local.

## 6. Empty-pattern policy

If any fixed pattern is empty, Candidate 1 is disabled and the existing path is retained. GNU empty-pattern behavior is already covered by the ordinary implementation and must not be reinterpreted by the automaton.

## 7. Case-insensitive policy

`-F -i` remains on the existing rune-aware path for Candidate 1. The current matcher delegates character equality to the locale character-class provider and decodes UTF-8 scalars as necessary; replacing that with byte folding would be incorrect for non-ASCII locales.

A later candidate may compile a rune automaton or another locale-aware accelerator, but only with focused conformance and measurement evidence.

## 8. T6.1 C0 cleanup

Before or alongside Candidate 1, remove the small Grep-local per-record allocation introduced by the `PatternInput` reference wrapper used by the T6.2 prepared-regex integration. The intended form is an immutable value wrapper or an equivalent allocation-free orchestration seam.

The T6.2 physical comparison attributed roughly 0.14–0.33 MiB per command invocation of fixed/PCRE control growth to this wrapper. Removing it is a local cleanup and must not change managed BRE/ERE prepared-input behavior.

## 9. Semantic test gate

Candidate 1 must add focused fixed multi-pattern tests covering at least:

- earlier pattern wins even when it appears later in pattern-source order;
- same-start longest match wins;
- no-match across hundreds/thousands of patterns;
- a match from the last pattern in the set;
- nonzero `startOffset` behavior through repeated `-o`-style searches;
- duplicate patterns;
- prefix-related patterns such as `a`, `ab`, `abc`;
- overlapping patterns such as `aba` and `bab`;
- ASCII and UTF-8 fixed strings;
- cancellation on a large no-match input; and
- fallback controls for `-i`, `-w`, `-x`, and empty patterns.

The complete existing GNU grep 3.12 compatibility suite remains mandatory on Windows, Linux, and macOS.

## 10. Measurement gate

After semantic/CI success, run a focused physical comparison on the established reference host.

Primary cases:

- `fixed-100`;
- `fixed-1000`.

Controls:

- one ordinary fixed-string pattern;
- PCRE lookbehind;
- BRE ASCII sparse using stable `Icod.CommandFramework 2.2.0`;
- record-reader short-record control.

Acceptance requires:

- a decisive fixed-1000 CPU reduction;
- useful fixed-100 improvement;
- no material allocation regression;
- no material regression in the control workloads; and
- identical command results.

The existing two-pass alternating physical protocol with a 30-second cooldown remains authoritative.

## 11. Non-goals

Candidate 1 does not attempt to:

- replace the managed BRE/ERE engine;
- optimize PCRE;
- redesign `ByteRecordReader`;
- implement locale-aware multi-pattern case folding;
- optimize `-w` / `-x` dispatch before proving their candidate-selection requirements; or
- introduce approximate matching, hashing shortcuts with collision risk, or probabilistic filters.

## 12. Exit criterion

T6.1 Candidate 1 is accepted when the complete semantic suite is green and the physical reference comparison demonstrates that fixed-string elapsed time no longer scales approximately linearly with the number of independent patterns in the measured 100/1,000-pattern workloads.
