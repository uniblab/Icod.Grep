# Icod.Grep 1.6.0 — T6.8 Initial Physical Scaling Report

**Measured commit:** `14fa3392947e0f3703095fce2fee9dcd1e77005f`  
**Reference host:** physical Windows reference host, hardware hash `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, Concurrent Workstation GC  
**Status:** initial scaling data accepted for resource analysis; timing warm-up defect corrected for follow-up collection

## 1. Collection integrity

The retained `records.json`, `files.json`, `patterns.json`, and `manifest.json` all identify the same commit and hardware inventory. All 27 scenarios completed successfully and all three profile-level reports recorded `AllSucceeded = true`.

The collection covered:

- 15 record/file-size scenarios;
- 3 file-count scenarios; and
- 9 pattern-count scenarios.

## 2. Timing warm-up defect

The first measured scenario in a profile absorbed one-time command/runtime/matcher initialization. This is visible in two impossible-looking scaling relationships:

- fixed 1 MiB record: 615.2 ms, while fixed 16 MiB record: 151.0 ms;
- fixed 100 patterns: 102.8 ms, while fixed 1,000 patterns: 16.3 ms.

The allocation observations remain useful because their scaling is stable and size-proportional, but those first-scenario elapsed values must not be used as quantitative scaling evidence.

The harness has therefore been amended so the fixed, BRE, ERE, PCRE, and recursive-traversal paths are warmed before the measured profile begins. A follow-up physical collection will establish clean timing curves.

## 3. S1 — record and file scaling

### Single-record allocation

| Matcher | 1 MiB | 16 MiB | 64 MiB | Approx. allocation/input at 64 MiB |
| --- | ---: | ---: | ---: | ---: |
| fixed | 4.27 MB | 67.25 MB | 268.87 MB | 4.01× |
| BRE | 24.19 MB | 386.03 MB | 1,543.95 MB | **23.01×** |
| ERE | 24.18 MB | 386.03 MB | 1,543.95 MB | **23.01×** |
| PCRE | 4.25 MB | 67.25 MB | 268.85 MB | 4.01× |

The fixed and PCRE curves are highly linear at roughly four managed bytes allocated per input byte. The managed BRE/ERE curves are also linear, but at roughly twenty-three allocated bytes per input byte.

At 64 MiB the BRE/ERE scenarios reached approximately 1.3–1.35 GiB working set, compared with roughly 132–136 MiB after the fixed/PCRE scenarios once collection settled.

This is a material T6.8 resource finding.

### Large short-record file allocation

The fixed-string many-record file curve is also linear:

| File size | Time | Allocation |
| --- | ---: | ---: |
| 64 MiB | 0.965 s | 268.97 MB |
| 256 MiB | 3.061 s | 1,075.53 MB |
| 1 GiB | 10.613 s | 4,301.76 MB |

This is about 4.0× managed allocation per input byte, but the working set remains bounded around a few tens of MiB because the allocations are short-lived record-pipeline objects rather than retained whole-file state. The 1 GiB case completed successfully.

## 4. Root cause of BRE/ERE memory amplification

The large-record amplification is traceable to the prepared-input representation in `Icod.CommandFramework 2.2.0`.

`RegularExpressionPreparedByteInput.Prepare` first makes a defensive `source.ToArray()` snapshot and then constructs the internal prepared representation from that owned byte array.

`RegexInput.Decode(ReadOnlyMemory<byte>, ...)` then creates three source-length-capacity lists:

- `List<Rune>`;
- `List<bool>`; and
- `List<int>` for source-coordinate boundaries.

In byte decoding mode, there is exactly one matching unit per source byte. After those lists are filled, collection expressions copy all three into final arrays retained by `RegexInput`.

For a large byte-mode record this creates both transient list backing arrays and final retained arrays of approximately source length. Together with Grep's record ownership and the public prepared-input defensive copy, the measured ~23× allocation amplification is consistent with the current representation rather than an unexplained GC anomaly.

### Candidate direction

The conservative first candidate belongs in CommandFramework, not in Grep: special-case `TextDecodingMode.Bytes` so `RegexInput.Decode` allocates exact final arrays directly and fills them in one pass, rather than building three full-capacity lists and copying them afterward.

That candidate preserves the existing immutable representation and public prepared-input contract. It changes construction cost only.

UTF-8 decoding should remain unchanged in the first candidate because its unit count can be lower than source byte count and therefore has a different allocation tradeoff.

## 5. S2 — many-file scaling

| Files | Time | Allocation | Approx. allocation/file |
| ---: | ---: | ---: | ---: |
| 1,000 | 0.893 s | 4.21 MB | 4.21 KB |
| 10,000 | 3.646 s | 37.52 MB | 3.75 KB |
| 50,000 | 18.387 s | 185.95 MB | 3.72 KB |

The 10k → 50k interval is essentially linear in both time and allocation. Working set remained bounded in the low-forty-MiB range, with peak working set under 45 MiB. No retained-per-file growth pathology is evident.

The 1k elapsed point includes more fixed startup cost and should be remeasured after the warm-up correction before using it in a formal timing slope.

## 6. S3 — pattern-count scaling

All pattern-count cases succeeded.

Allocation at 1,000 patterns was approximately:

- fixed: 1.96 MB;
- BRE: 10.70 MB;
- ERE: 10.70 MB; and
- PCRE: 1.18 MB.

The fixed-string 10,000-pattern case completed with 18.71 MB allocation and approximately 58 MB peak working set. The managed BRE/ERE and PCRE 1,000-pattern cases also completed without abnormal working-set retention.

The elapsed-time curve requires the warmed follow-up collection because the first fixed-pattern point was dominated by one-time initialization.

No pattern-count ceiling was reached in this first profile.

## 7. Decisions

1. Keep the many-file and pattern-count implementations unchanged pending the warmed timing rerun; their resource curves are bounded and broadly linear.
2. Retain the 1 GiB fixed file result as evidence that the record pipeline can process large files without whole-file retention.
3. Treat the BRE/ERE large-record amplification as a concrete T6.8 issue requiring a measured CommandFramework construction optimization.
4. Do not weaken Grep semantics, skip prepared input globally, or impose an arbitrary record-size limit to hide the issue.
5. Rerun the physical T6.8 reference collection after the harness warm-up correction so timing slopes can be accepted independently of this allocation finding.
