# Icod.Grep 1.6.0 — T6.5 Output Baseline and Candidate 1 Plan

**Measured commit:** `c0fcffb5d529691891c727fbf9816f2e8d14ec74`  
**Baseline commit:** `c0fcffb5d529691891c727fbf9816f2e8d14ec74`  
**Candidate commit:** `c0fcffb5d529691891c727fbf9816f2e8d14ec74`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating passes, same-code ABBA control, 30-second cooldown  
**Status:** baseline accepted; Candidate 1 selected

## 1. Purpose

T6.5 adds output-heavy benchmark coverage before changing the production output path. The initial physical run deliberately compares the same commit against itself so it establishes workload allocation envelopes and timing noise without conflating them with a code change.

The workloads use deterministic in-memory input and output streams and exercise:

- dense selected-record output;
- filename/line/byte prefixes;
- `--only-matching`;
- forced color;
- before/after context; and
- line-buffered output.

Each benchmark validates that the command succeeds and emits output before measurement.

## 2. Same-code physical baseline

Allocation is perfectly stable across all four executions.

| Workload | Mean time across four runs | Allocation |
| --- | ---: | ---: |
| context-output | 6.015 ms | 10.30 MB |
| dense-output | 5.400 ms | 10.94 MB |
| forced-color | 12.161 ms | **21.63 MB** |
| line-buffered | 5.565 ms | 10.94 MB |
| only-matching | 8.007 ms | 12.38 MB |
| prefix-heavy | 7.787 ms | 11.99 MB |

The four individual allocation observations are identical for every workload. Timing varies materially across the same-code ABBA sequence, confirming that T6.5 acceptance should continue to rely on allocation as the first-order signal and require repeated directional timing evidence before making narrow elapsed-time claims.

## 3. Dominant residual

`forced-color` is the clearest first target.

It runs the same 4,096-record dense corpus as `dense-output`, but allocation rises from 10.94 MB to 21.63 MB — an additional **10.69 MB**, or about **97.7%** over the non-colored dense-output path.

The current selected-record flow already prepares a `PatternInput` before determining whether the record matches. When color highlighting is enabled, `WriteColoredRecordContentAsync` calls `patterns.Prepare(...)` again for the same `ReadOnlyMemory<byte>` before `FindAll` enumerates highlighted spans.

That duplicate preparation is unnecessary for the common selected colored-record path and is directly aligned with the measured forced-color residual.

## 4. Candidate 1 contract

Candidate 1 will reuse the already-prepared `PatternInput` when rendering a selected colored record.

The implementation should:

1. pass the current record's existing `PatternInput` into the selected-record output path;
2. let `WriteColoredRecordContentAsync` consume that prepared input instead of preparing the same record again;
3. retain the existing prepare-on-demand fallback for any colored record that does not already have a prepared input;
4. leave match ordering, color span enumeration, context semantics, `-v`, `-o`, zero-length behavior, cancellation, and exact output bytes unchanged;
5. avoid shared mutable caching or cross-record state; and
6. preserve the existing immutable/per-record lifetime model.

The existing exact GNU-color output tests remain the primary semantic gate.

## 5. Candidate 1 acceptance

Primary physical workload:

- `forced-color`.

Controls:

- `dense-output`;
- `only-matching`;
- `context-output`;
- `prefix-heavy`;
- `line-buffered`.

Candidate 1 is accepted if forced-color allocation falls materially while the controls remain allocation-neutral and exact output tests remain green. Timing should be neutral-to-better; a stable material slowdown would require investigation.

A second T6.5 candidate may then address `only-matching`, prefix formatting, one-byte writes, or Windows text-output translation, but only according to the residual measured after Candidate 1.
