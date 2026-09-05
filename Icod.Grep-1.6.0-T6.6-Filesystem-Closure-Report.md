# Icod.Grep 1.6.0 — T6.6 Filesystem / Many-File Closure Report

**Incremental baseline:** `d3281b5fa578f296aca6f049868a694f4d7a19f7`  
**Candidate implementation:** `32aa56cad6b3262ecab9f96fb88cbd4984c7549a`  
**Measured head:** `92c52d7059a24bfb8badabf0a7e54ac4847732f0`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Protocol:** two alternating passes, pre-T6.6 → candidate → candidate → pre-T6.6, 30-second inter-run cooldown  
**Status:** T6.6 Candidate 1 accepted; T6.6 closed; T6.5 selected next

## 1. Candidate

T6.6 Candidate 1 removes redundant read buffering from Grep's file-input wrapper.

Before Candidate 1, each searched file was opened through the Grep-local `FileStream` wrapper with `StreamOperations.DefaultBufferSize` (64 KiB), while Grep's segmented record reader also maintained its own bounded input buffer. For many-file workloads this created two independent buffering layers per file.

Candidate 1 disables the underlying `System.IO.FileStream` read buffer by forcing the read-side constructor buffer size to 1. The Grep segmented reader remains the authoritative bounded buffering layer. Write-side behavior is unchanged.

The implementation applies uniformly to direct file operands and recursively discovered files. Traversal ordering and concurrency behavior are unchanged.

## 2. Physical result

| Workload | Pre-T6.6 mean | Candidate mean | Time change | Pre-T6.6 allocation | Candidate allocation | Allocation change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Large physical file | 227.40 ms | 215.75 ms | **-5.12%** | 283.355 MB | 283.310 MB | effectively flat |
| Many small files | 157.75 ms | 157.90 ms | +0.10% | 34.620 MB | 18.530 MB | **-16.090 MB / -46.48%** |
| Recursive tree | 218.25 ms | 184.35 ms | **-15.53%** | 34.910 MB | 18.805 MB | **-16.105 MB / -46.13%** |

The many-file allocation reduction is nearly exactly the expected magnitude for eliminating one roughly 64 KiB read buffer across 256 opened files.

## 3. Interpretation

The result strongly confirms the buffering hypothesis.

`many-small-files` sheds 16.09 MB of managed allocation while elapsed time is effectively unchanged. `recursive-tree` sheds 16.11 MB and is materially faster. The `large-file` control shows no allocation regression and improves in elapsed time in both aggregate comparison and retained reference-host context.

This is an unusually clean optimization because the allocation improvement is large, deterministic, and directly explained by the removed per-file buffer. No visible ordering, traversal, matching, or output semantics changed.

## 4. T6.6 acceptance

T6.6 Candidate 1 is accepted because:

- the primary 256-file workloads each eliminate about 16 MB of allocation;
- recursive traversal improves materially in elapsed time;
- the direct many-file case remains timing-neutral rather than regressing;
- large sequential-file throughput is not harmed;
- the complete Windows/Linux/macOS, benchmark-smoke, package-smoke, and archive-smoke matrix is green; and
- the change does not introduce traversal parallelism or alter observable ordering.

The post-candidate allocations of about 18.5–18.8 MB are now much closer to what the processed record count and matcher work predict. The dominant redundant per-file buffering cost has been removed.

Further T6.6 work would now require narrower path/traversal micro-optimization with a much smaller expected return. That is not justified before addressing the remaining output-path allocation opportunities.

## 5. Next tranche: T6.5 output, formatting, color, and context allocation

T6.5 is selected next.

The current output path contains several obvious allocation candidates that are independent of matching and filesystem work:

- repeated one-byte arrays such as `new[] { (byte)'\n' }`, separators, NULs, and tabs;
- numeric prefix formatting through intermediate strings;
- repeated SGR string construction via `string.Concat( "\u001b[", sgr, "m" )`;
- color end/reset writes split across multiple small operations;
- `FindAll` materializing a `List<MatchSpan>` before `-o` / highlighted output consumption;
- Windows text-output translation allocating a fresh byte array for every write, including writes without any newline; and
- repeated small write calls around filename/line/byte prefixes and context separators.

### T6.5 Candidate 1 direction

The first candidate should be conservative and allocation-oriented rather than redesigning the output architecture:

1. Replace repeated one-byte heap arrays with static immutable byte storage or stack/span-friendly writes.
2. Avoid allocating translated Windows output arrays when no LF is present.
3. Precompute/cache stable ANSI control byte sequences where semantics allow.
4. Preserve exact output bytes, write ordering, line-buffering behavior, cancellation, and error propagation.
5. Add benchmark scenarios that actually exercise output-heavy paths before accepting any optimization.

### T6.5 benchmark expansion

The existing command benchmark corpus is intentionally output-light (`-c` in the filesystem cases and mostly selection-focused in command scenarios). T6.5 therefore needs explicit deterministic output workloads, including:

- dense selected-record output;
- `-n -b -H` prefix-heavy output;
- dense `-o` output;
- forced color output;
- before/after context groups;
- line-buffered output; and
- Windows text-output translation controls where practical.

The benchmark sink should retain bytes without writing to a console so output volume and allocation remain deterministic.

T6.5 should be measured incrementally against this T6.6-closed state.
