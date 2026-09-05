# Icod.Grep 1.6.0 — T6.3 Record-Pipeline Closure and Residual Report

**Candidate 1 baseline:** `20da40f34c1ef3c255fdd8df9720b409fe6fde19`  
**Candidate 1 implementation:** `9aeebba810466cf930cf285090a4d84e46149f1d`  
**Candidate 2 baseline:** `ed1fddd6ab617547bfe4fb6d5fd6477349b08a35`  
**Candidate 2 measured head:** `025cd5fc7068aa1f60dcf4da78b25bb6327eb637`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating passes, baseline → candidate → candidate → baseline, 30-second inter-run cooldown  
**Status:** T6.3 accepted and closed; T6.6 selected next

## 1. Candidate 1

Candidate 1 removed Grep's redundant second copy of each already-independent `ByteRecord` by changing `LineRecord.Content` from `byte[]` to `ReadOnlyMemory<byte>` and retaining the existing record memory directly.

The physical result showed the exact expected signature: every command workload reduced allocation by approximately one aggregate corpus copy. Representative reductions were:

- `long-line`: about 8 MB;
- `large-file`: about 12 MB;
- ordinary short-record command workloads: roughly 0.4–1.5 MB depending on corpus size.

Candidate 1 was accepted as an intermediate optimization.

## 2. Candidate 2

Inspection of `Icod.CommandFramework 2.2.0` showed that its compatibility `ByteRecordReader` still performed additional materialization:

1. `DelimitedByteRecordSegmentReader` returned independently owned record segments;
2. `ByteRecordReader` copied those segments into an `ArrayBufferWriter<byte>`; and
3. `ByteRecord` copied the completed span again.

Candidate 2 therefore introduced a Grep-local `ByteRecordReader` that consumes the shared segmented reader directly. It:

- returns a completed single segment directly as `ReadOnlyMemory<byte>`;
- creates an `ArrayBufferWriter<byte>` only when a logical record spans multiple segments;
- returns `WrittenMemory` directly for a multi-segment record rather than copying it again; and
- leaves the shared CommandFramework reader and its benchmark controls unchanged.

Focused tests cover ordinary single-segment records, multi-segment reassembly, empty/consecutive records, final unterminated records, and NUL-delimited records. The full Windows/Linux/macOS, benchmark-smoke, package-smoke, and archive-smoke PR matrix is green.

## 3. Candidate 2 physical result

| Workload | Candidate 1 mean | Candidate 2 mean | Time change | Candidate 1 allocation | Candidate 2 allocation | Allocation change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| BRE ASCII dense | 122.484 ms | 116.675 ms | **-4.74%** | 333.700 MB | 331.625 MB | **-2.075 MB / -0.62%** |
| BRE ASCII sparse | 21.431 ms | 18.067 ms | **-15.70%** | 39.050 MB | 35.430 MB | **-3.620 MB / -9.27%** |
| Fixed, 1 pattern | 4.696 ms | 4.014 ms | **-14.51%** | 4.350 MB | 2.280 MB | **-2.070 MB / -47.59%** |
| Fixed, 100 patterns | 4.689 ms | 4.012 ms | **-14.44%** | 4.220 MB | 2.160 MB | **-2.060 MB / -48.82%** |
| Fixed, 1,000 patterns | 3.668 ms | 3.386 ms | **-7.69%** | 3.770 MB | 2.740 MB | **-1.030 MB / -27.32%** |
| BRE long-line | 118.346 ms | 111.401 ms | **-5.87%** | 205.655 MB | 197.670 MB | **-7.985 MB / -3.88%** |
| PCRE lookbehind | 6.324 ms | 5.529 ms | **-12.58%** | 4.730 MB | 2.410 MB | **-2.320 MB / -49.05%** |
| BRE UTF-8 sparse | 15.247 ms | 13.250 ms | **-13.10%** | 27.300 MB | 24.740 MB | **-2.560 MB / -9.38%** |
| Large physical file | 226.400 ms | 208.300 ms | **-7.99%** | 312.375 MB | 283.330 MB | **-29.045 MB / -9.30%** |
| Many small files | 163.200 ms | 152.550 ms | **-6.53%** | 36.345 MB | 34.450 MB | **-1.895 MB / -5.21%** |
| Recursive tree | 199.250 ms | 193.900 ms | **-2.69%** | 36.755 MB | 34.815 MB | **-1.940 MB / -5.28%** |

The principal T6.3 workloads both improve in allocation and elapsed time. `long-line` improves in both individual paired passes, as does `large-file`, so the aggregate improvement is not merely a run-order artifact.

## 4. Shared record-reader control

The unchanged `Icod.CommandFramework` record-reader benchmarks retain their established allocation envelope:

| Record length | Candidate 1 allocation | Candidate 2 allocation |
| --- | ---: | ---: |
| 80 | 6.38 MB | 6.38 MB |
| 4,096 | 25.34 MB | 25.34 MB |
| 262,144 | 53.535 MB | 53.530 MB |

The tiny 0.005 MB difference at 262,144 bytes is measurement rounding. The control confirms that Candidate 2 improves Grep's consumption path without silently changing the shared package.

Control timings show the same reference-host drift observed elsewhere in the ABBA sequence and are not accompanied by allocation or code changes.

## 5. T6.3 acceptance

T6.3 is accepted and closed because:

- Candidate 1 removed one provably redundant complete record copy;
- Candidate 2 removed the remaining Grep-side compatibility materializer for ordinary records;
- very-large records now require only the materialization genuinely needed to join multiple bounded segments;
- ordinary records reuse independently owned segment memory directly;
- primary physical workloads improve materially;
- shared reader controls remain unchanged; and
- the complete correctness/package/archive matrix is green.

Further materialization reduction would require changing the shared segmented-reader ownership contract or introducing a genuinely streaming matcher/output architecture. Those are not justified by the remaining T6.3 measurements in this release.

## 6. Residual analysis and next tranche

The new residual makes **T6.6 — Filesystem traversal and many-file scalability** the next measured target.

Candidate 2 leaves:

- `many-small-files`: about **34.45 MB**;
- `recursive-tree`: about **34.82 MB**.

Each workload contains 256 files × 32 records × 80 bytes = 8,192 records. By contrast, the in-memory `ascii-sparse` workload contains 16,384 records × 80 bytes and allocates about 35.43 MB. The many-file workload therefore processes only half as many records while consuming nearly the same managed allocation, demonstrating a large per-file cost independent of matcher scaling.

The direct-file and recursive-tree allocations are also very close, which shows that traversal enumeration itself is not the dominant difference. The common file-open/input infrastructure is the stronger candidate.

Both Grep file-open sites currently construct `FileStream` with `StreamOperations.DefaultBufferSize` (64 KiB) while the Grep-local segmented record reader already owns a bounded 64 KiB pooled read buffer. This creates a strong hypothesis that ordinary file input is double-buffered and that the per-file `FileStream` buffer is a major part of the 256-file residual.

### T6.6 Candidate 1 contract

1. Eliminate redundant `FileStream` buffering for ordinary searched files while retaining the Grep-local segmented reader's bounded buffer.
2. Apply the same policy to direct file operands and recursively discovered files.
3. Preserve asynchronous sequential file access, sharing semantics, cancellation, binary probing, Windows text translation, byte offsets, and all visible ordering.
4. Do not introduce traversal parallelism.
5. Keep pattern-file reads unchanged initially; they are not part of the measured many-file hot path.
6. Add/retain semantic coverage for direct files and recursive files before physical acceptance.

### T6.6 Candidate 1 physical gate

Primary:

- `many-small-files`;
- `recursive-tree`.

Controls:

- `large-file`;
- `ascii-sparse`;
- `long-line`;
- unchanged record-reader benchmarks.

Candidate 1 should materially reduce per-file managed allocation and should not regress large sequential-file throughput.
