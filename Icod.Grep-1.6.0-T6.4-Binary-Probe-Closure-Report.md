# Icod.Grep 1.6.0 — T6.4 Binary-Probe Closure Report

**Incremental baseline:** `f23db569dbf6c0dec086aacf481ceecea9a943ad`  
**Candidate implementation:** `58acb3c55cb359fa33b57a9399b08a233abdee47`  
**Measurement-harness head:** `3d24de3b3fb9a8f98ef649235803c4a9176463ef`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating passes, pre-T6.4 → candidate → candidate → pre-T6.4, 30-second inter-run cooldown  
**Status:** T6.4 accepted and closed; T6.3 selected next

## 1. Candidate

T6.4 Candidate 1 replaces the per-seekable-file 98,304-byte probe allocation with a pooled 8 KiB probe chunk while preserving the established 98,304-byte binary-detection window.

For seekable streams the implementation:

- rents a temporary probe buffer from `ArrayPool<byte>`;
- reads in bounded chunks;
- stops immediately when NUL is found;
- stops when the compatibility probe window is exhausted;
- returns the pooled buffer; and
- restores the original stream position in `finally`.

The non-seekable prefix-replay path is intentionally unchanged.

Focused tests pin early NUL termination, exact compatibility-window bounds, nonzero starting-position restoration, and non-seekable prefix replay.

## 2. Incremental physical result

| Workload | Pre-T6.4 mean | Candidate mean | Time change | Pre-T6.4 allocation | Candidate allocation | Allocation change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Large physical file | 223.7 ms | 233.3 ms | +4.3% in this run | 324.36 MB | 324.33 MB | effectively flat |
| Many small files | 182.0 ms | 152.6 ms | **-16.2%** | 45.01 MB | 37.01 MB | **-8.00 MB / -17.8%** |
| Recursive tree | 220.6 ms | 190.5 ms | **-13.6%** | 45.34 MB | 37.34 MB | **-8.00 MB / -17.6%** |

The primary T6.4 workloads therefore improve materially in both elapsed time and managed allocation.

## 3. Large-file control interpretation

The large-file pair in this incremental run shows a nominal 4.3% candidate slowdown, but the retained pre-T6.4 whole-suite reference collected immediately before this tranche measured the same candidate-generation code at approximately **232.05 ms**. T6.4 Candidate 1 measures approximately **233.3 ms** here, a difference of only about **0.5%**.

The incremental pre-T6.4 baseline's 223.7 ms mean is therefore inconsistent with the immediately preceding retained reference series and is best interpreted as favorable host-state/run-order variation on the physical laptop. Allocation is unchanged to measurement precision.

No stable large-file regression is established, and no additional implementation complexity is justified merely to chase this timing excursion.

## 4. Acceptance

T6.4 Candidate 1 satisfies the tranche gate:

- `many-small-files` is materially faster and allocates 8 MB less;
- `recursive-tree` is materially faster and allocates 8 MB less;
- `large-file` allocation is flat and its candidate timing remains in the retained pre-T6.4 reference envelope;
- all binary-probe semantic tests pass; and
- the complete Windows/Linux/macOS CI, benchmark-smoke, package, and archive matrix is green.

T6.4 is therefore **accepted and closed**.

## 5. Next residual target: T6.3

The next tranche is **T6.3 — Record pipeline and very-large-record scalability**.

The first candidate should target an avoidable record copy already visible in `ProcessSourceAsync`. `ByteRecordReader` materializes each record, after which Grep immediately constructs a `LineRecord` using `record.Content.ToArray()` before knowing whether the selected output mode requires ownership of a retained byte array.

This means modes such as `-c`, `-q`, `-l`, and `-L` can pay a second record materialization even though they do not emit ordinary record content or retain before/after context.

### T6.3 Candidate 1 contract

1. Match directly against the `ByteRecordReader` record memory where lifetime permits.
2. Do not construct/copy `LineRecord.Content` unless ordinary output or context retention actually requires it.
3. Preserve exact byte offsets, line numbering, context, color, `-o`, binary policy, malformed-input handling, and `-m` stdin repositioning.
4. Keep retained context records independently owned; never retain memory whose lifetime belongs to the reader's next read.
5. Add focused tests for count-only/no-output paths and context/output paths before changing production behavior.

### Initial measurements

Primary:

- `large-file`;
- `long-line`.

Secondary:

- `ascii-sparse`;
- `many-small-files`;
- record-reader controls.

Candidate 1 should reduce command-path allocation without changing the record-reader control itself and without regressing output-bearing semantics.
