# Icod.Grep 1.6.0 — T6.3 Candidate 1 Report

**Incremental baseline:** `20da40f34c1ef3c255fdd8df9720b409fe6fde19`  
**Candidate:** `9aeebba810466cf930cf285090a4d84e46149f1d`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating passes, pre-T6.3 → candidate → candidate → pre-T6.3, 30-second inter-run cooldown  
**Status:** Candidate 1 accepted as an intermediate optimization; T6.3 remains open for Candidate 2

## 1. Candidate 1

Candidate 1 removes Grep's redundant second copy of every materialized `ByteRecord`.

`Icod.CommandFramework.Records.ByteRecordReader` already returns independently owned record content. Grep previously copied that content again with `record.Content.ToArray()` when constructing `LineRecord`.

Candidate 1 changes `LineRecord.Content` from `byte[]` to `ReadOnlyMemory<byte>` and retains the already-owned `ByteRecord.Content` directly. Output, context, color, and `-o` paths consume slices of that memory without another whole-record allocation.

Focused tests cover before-context retention, after-context retention, a 256 KiB count-only record, and exact `-bo` output.

## 2. Physical result

The allocation result is deterministic and closely matches the aggregate record-content size of each scenario.

| Workload | Pre-T6.3 allocation | Candidate allocation | Reduction |
| --- | ---: | ---: | ---: |
| BRE ASCII sparse | 40.55 MB | 39.05 MB | **1.50 MB / 3.70%** |
| ERE ASCII dense | 334.56 MB | 333.68 MB | **0.88 MB / 0.26%** |
| BRE UTF-8 sparse | 28.42 MB | 27.30 MB | **1.12 MB / 3.94%** |
| BRE long-line | 213.66 MB | 205.66 MB | **8.00 MB / 3.74%** |
| Fixed, 1 pattern | 5.22 MB | 4.35 MB | **0.87 MB / 16.67%** |
| Fixed, 100 patterns | 5.09 MB | 4.22 MB | **0.87 MB / 17.09%** |
| Fixed, 1,000 patterns | 4.21 MB | 3.77 MB | **0.44 MB / 10.45%** |
| PCRE lookbehind | 5.73 MB | 4.73 MB | **1.00 MB / 17.45%** |
| Large physical file | 324.35 MB | 312.33 MB | **12.02 MB / 3.71%** |
| Many small files | 37.11 MB | 36.26 MB | **0.85 MB / 2.29%** |
| Recursive tree | 37.34 MB | 36.59 MB | **0.75 MB / 2.01%** |

This is exactly the expected signature of deleting one complete record-content copy: every command workload improves allocation, while the reduction scales with the corpus bytes processed.

## 3. Timing interpretation

Timing is dominated by the established reference-host run-order noise rather than by a consistent Candidate 1 regression.

The most important primary cases are:

- `long-line`: 125.43 ms pre-T6.3 mean versus 129.25 ms candidate mean across the two passes. The individual passes reverse direction (130.09 → 139.66 ms in pass 1, 120.78 → 118.85 ms in pass 2), so no stable slowdown is established.
- `large-file`: 246.90 ms pre-T6.3 mean versus 224.20 ms candidate mean. The pre-T6.3 pass-1 value is unusually high at 281.3 ms, while the candidate is stable at 222.9 and 225.5 ms. The result establishes no candidate regression but should not be interpreted as a precise 9% speed claim.

The small matcher controls are likewise timing-noise limited while all show the expected allocation reduction.

## 4. Why T6.3 remains open

Candidate 1 removes the Grep-local duplicate copy, but inspection of `Icod.CommandFramework 2.2.0` shows that the materializing reader itself still performs avoidable copying:

1. `DelimitedByteRecordSegmentReader` returns independently owned `ByteRecordSegment` data.
2. `ByteRecordReader` copies every segment into an `ArrayBufferWriter<byte>`.
3. `ByteRecord` then copies the completed written span into another independent array.

For ordinary records that fit in one segment, Grep therefore still pays the compatibility materializer even though the first completed segment is already independently owned and sufficient for the command.

The record-reader control also confirms that materialization remains expensive, especially for very large records.

## 5. Candidate 2 contract

Candidate 2 should bypass `ByteRecordReader` inside Grep and consume `DelimitedByteRecordSegmentReader` directly.

The implementation should:

1. Return a completed single segment directly as the record's `ReadOnlyMemory<byte>` without another whole-record copy.
2. Allocate an `ArrayBufferWriter<byte>` only when a logical record spans multiple segments.
3. Return the builder's `WrittenMemory` directly when a multi-segment record completes; do not perform a final `ToArray()` copy.
4. Preserve separator exclusion, `IsTerminated`, empty records, consecutive separators, final unterminated records, `-z`, binary policy, byte offsets, context, `-o`, color, cancellation, and `-m` seekable-stdin repositioning.
5. Keep `RecordReaderBenchmarks` unchanged as an upstream control; T6.3 is optimizing Grep's consumption path, not silently changing the shared package during this release.

## 6. Candidate 2 acceptance

Primary physical workloads remain:

- `long-line`;
- `large-file`.

Secondary controls:

- `ascii-sparse`;
- fixed single-pattern;
- PCRE lookbehind;
- `many-small-files` and `recursive-tree`;
- all three unchanged `RecordReaderBenchmarks` controls.

Candidate 2 is accepted if it materially reduces command-path allocation beyond Candidate 1, preserves all command semantics, and leaves the shared record-reader control unchanged.
