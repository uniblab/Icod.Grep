# Icod.Grep 1.6.0 — T6.1 Closure and Residual Report

**Pinned baseline:** `Icod.Grep 1.5.0` at `423c0e9623100492fa01b6e4d14c183761d111d7`  
**Candidate measured:** `a0af7e8faa47f5a67f8d2cfa97d6d03c7a26a782`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating passes, baseline → candidate → candidate → baseline, 30-second inter-run cooldown  
**Status:** T6.1 accepted and closed; T6.4 selected as the next optimization tranche

## 1. Purpose

This report closes T6.1 after the fixed-string multi-pattern Candidate 1 implementation and records the whole-suite residual profile used to select the next T6 optimization target.

Candidate 1 consists of:

- immutable Aho-Corasick-style byte matching for case-sensitive multi-pattern `-F`;
- conservative dispatch limited to multiple non-empty patterns without `-i`, `-w`, or `-x`;
- preservation of the existing fixed-pattern fallback for all excluded semantic cases;
- leftmost / longest selection preservation;
- cancellation coverage;
- direct matcher tests and end-to-end command tests; and
- an allocation-free `PatternInput` value wrapper.

## 2. T6.1 primary result

The focused T6.1 reference comparison already established an overwhelming fixed-pattern scaling improvement. The full residual comparison independently corroborates it.

| Workload | Baseline mean | Candidate mean | Time reduction | Baseline allocation | Candidate allocation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Fixed strings, 100 patterns | 340.925 ms | 4.987 ms | **98.54%** | 5.40 MB | 5.19 MB |
| Fixed strings, 1,000 patterns | 1,004.347 ms | 4.039 ms | **99.60%** | 3.49 MB | 4.30 MB |

The fixed-100 candidate is roughly **68× faster** than the two-pass baseline mean. The fixed-1000 candidate is roughly **249× faster** in this whole-suite run. A separate focused comparison produced similarly decisive results, confirming that the improvement is not an artifact of this full-suite run.

The fixed-1000 allocation increase is approximately 0.81 MB per complete command invocation in this run. `CommandBenchmarks` includes pattern parsing and compilation inside every measured operation, so this number includes rebuilding the immutable 1,000-pattern automaton each time. It is a bounded command-setup cost, not per-record search growth. Given the roughly 99.6% CPU reduction and the absence of a hot-loop allocation signature, this does not block Candidate 1 acceptance.

## 3. T6.1 control gate

A single-pattern fixed-string scenario was added as a control. The remaining matcher and record-reader controls were measured in the same four-run sequence.

| Control | Baseline mean | Candidate mean | Baseline allocation | Candidate allocation | Interpretation |
| --- | ---: | ---: | ---: | ---: | --- |
| Fixed string, 1 pattern | 4.682 ms | 5.135 ms | 5.31 MB | 5.31 MB | allocation flat; timing within small-workload host noise |
| PCRE lookbehind | 6.233 ms | 7.213 ms | 5.82 MB | 5.82 MB | allocation flat; candidate remains within prior reference-host timing envelope |
| Record reader, 80 bytes | 2.772 ms | 3.242 ms | 6.38 MB | 6.38 MB | allocation exactly flat; timing noise dominates at this scale |
| Record reader, 4 KiB | 11.261 ms | 11.472 ms | 25.34 MB | 25.34 MB | effectively flat |
| Record reader, 256 KiB | 20.847 ms | 21.434 ms | 53.54 MB | 53.54 MB | effectively flat |

No control shows a managed-allocation regression attributable to Candidate 1. The small timing deltas are not accompanied by allocation or scaling changes and are within the kind of run-order variation already documented for the physical laptop.

## 4. Whole-suite residual profile

The same run provides the post-T6.2/T6.1 residual profile.

| Workload | Baseline mean | Candidate mean | Time change | Baseline allocation | Candidate allocation | Allocation change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| BRE ASCII sparse | 166.957 ms | 22.735 ms | **-86.38%** | 604.59 MB | 40.65 MB | **-93.28%** |
| ERE ASCII dense | 173.535 ms | 123.784 ms | **-28.67%** | 618.64 MB | 334.66 MB | **-45.90%** |
| BRE UTF-8 sparse | 122.744 ms | 15.644 ms | **-87.25%** | 462.87 MB | 28.52 MB | **-93.84%** |
| BRE long line | 831.474 ms | 120.542 ms | **-85.50%** | 3,005.86 MB | 213.76 MB | **-92.89%** |
| Fixed, 1 pattern | 4.682 ms | 5.135 ms | +9.68% | 5.31 MB | 5.31 MB | flat |
| Fixed, 100 patterns | 340.925 ms | 4.987 ms | **-98.54%** | 5.40 MB | 5.19 MB | -3.89% |
| Fixed, 1,000 patterns | 1,004.347 ms | 4.039 ms | **-99.60%** | 3.49 MB | 4.30 MB | +23.21% |
| PCRE lookbehind | 6.233 ms | 7.213 ms | +15.73% | 5.82 MB | 5.82 MB | flat |
| Large physical file | 1,618.550 ms | 232.050 ms | **-85.66%** | 4,835.99 MB | 324.36 MB | **-93.29%** |
| Many small files | 314.500 ms | 184.650 ms | **-41.29%** | 316.56 MB | 45.01 MB | **-85.78%** |
| Recursive tree | 361.300 ms | 229.150 ms | **-36.58%** | 317.08 MB | 45.34 MB | **-85.70%** |
| Record reader, 80 bytes | 2.772 ms | 3.242 ms | +16.96% | 6.38 MB | 6.38 MB | flat |
| Record reader, 4 KiB | 11.261 ms | 11.472 ms | +1.87% | 25.34 MB | 25.34 MB | flat |
| Record reader, 256 KiB | 20.847 ms | 21.434 ms | +2.82% | 53.54 MB | 53.54 MB | flat |

The managed-regex improvements remain intact. Fixed multi-pattern scaling is no longer a residual bottleneck.

## 5. Why T6.4 is next

The residual many-file cases point directly at binary probing rather than record materialization.

`PrepareInputAsync` currently allocates a `BinaryProbeLength` buffer of **98,304 bytes for every input file**, even for seekable files and even when the file is only a few KiB. Both the `many-small-files` and `recursive-tree` benchmark fixtures contain 256 files.

Therefore the probe arrays alone account for:

- `98,304 × 256 = 25,165,824` bytes;
- exactly **24 MiB** of transient managed storage per command invocation.

The complete candidate allocations are only about 45 MiB for these workloads, so more than half of the remaining allocation is explained by binary-probe storage before considering record/matcher costs.

The current seekable-file path also reads up to the entire probe window, scans it for NUL, seeks back, and then reads the same bytes again through the record pipeline. For the benchmark's small files this means avoidable double-reading in addition to the 24 MiB of probe-array allocation.

This is precisely the scope of T6.4: small-file probe overhead, reduced copying/zeroing, early NUL discovery, safe probe-storage reuse, and tighter interaction with the first read.

T6.6 filesystem traversal remains a credible later target, but attacking traversal before removing the measured 98-KiB-per-file probe cost would mix two causes and make the traversal residual harder to interpret.

## 6. T6.4 initial candidate

The first T6.4 candidate should be intentionally conservative:

1. For **seekable streams**, do not allocate a 98,304-byte retained prefix buffer.
2. Probe in bounded chunks using pooled temporary storage.
3. Stop probing immediately when a NUL is found or when the 98,304-byte compatibility window is exhausted.
4. Restore the original stream position before returning, including the normal non-binary path.
5. Keep the existing non-seekable prefix-replay behavior initially so stdin/pipe semantics are not coupled to the first optimization.
6. Preserve the exact `BinaryFileMode`, `-z`, stdin seek/reposition, cancellation, and binary diagnostic contracts.

This candidate directly targets the measured physical-file workloads while minimizing semantic surface area.

## 7. Acceptance measurements for T6.4 Candidate 1

Primary workloads:

- `many-small-files`;
- `recursive-tree`.

Secondary controls:

- `large-file`;
- ordinary in-memory command cases;
- record-reader controls;
- binary-policy functional tests;
- non-seekable input functional tests.

Candidate 1 should materially reduce many-file managed allocation, should improve or at least not regress elapsed time beyond reference-host noise, and must preserve all binary-policy behavior.

## 8. T6.1 closure

T6.1 is **accepted and closed**.

The next production optimization tranche is **T6.4 — Binary probing and input pipeline**. T6.3, T6.5, T6.6, T6.7, and T6.8 remain open for later selection after T6.4 is measured.
