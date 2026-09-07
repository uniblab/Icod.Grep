# Icod.Grep 1.6.0 — T6.9 Final Reference and Release Closure

**Baseline:** `423c0e9623100492fa01b6e4d14c183761d111d7` (`1.5.0`)  
**Candidate:** `8fc41d85ff61c65b1641b51ab80a532cb9be01b8` (`1.6.0` release candidate)  
**Hardware inventory SHA-256:** `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Protocol:** two-pass ABBA, 30-second cooldown, full benchmark filter `*`  
**Status:** accepted

## 1. Scope

This is the final physical-reference comparison for the `1.6.0` T6 performance and scalability tranche. The current benchmark harness was overlaid onto the immutable `1.5.0` baseline so both variants exercised the same benchmark definitions.

The collector sequence was:

1. baseline pass 1;
2. candidate pass 1;
3. candidate pass 2; and
4. baseline pass 2.

Total collector wall-clock duration varied substantially between passes because BenchmarkDotNet selected different iteration/warmup schedules. Those outer run durations are not command-performance measurements. Acceptance is based on the per-benchmark BenchmarkDotNet means and managed-allocation data.

## 2. Summary

Across all 29 benchmark workloads in the final matrix, the two-pass candidate mean is faster than the two-pass `1.5.0` mean. No benchmark shows a two-pass mean timing regression.

Managed allocation is unchanged for the shared record-reader control benchmarks and reduced materially for every Grep command workload where allocation was measurable.

The strongest end-to-end improvements confirm the intended T6 work rather than introducing a new release-closure residual.

## 3. Representative command-path results

| Workload | 1.5.0 mean | 1.6.0 mean | Time change | Allocation change |
| --- | ---: | ---: | ---: | ---: |
| fixed-1000 | 1,201.3 ms | 2.86 ms | **-99.8%** | **-21.5%** |
| fixed-100 | 258.0 ms | 3.48 ms | **-98.7%** | **-60.0%** |
| BRE ASCII sparse | 149.1 ms | 10.73 ms | **-92.8%** | **-96.5%** |
| BRE UTF-8 sparse | 111.9 ms | 8.10 ms | **-92.8%** | **-96.8%** |
| BRE long-line | 773.3 ms | 64.20 ms | **-91.7%** | **-95.8%** |
| large-file | 1,394.5 ms | 123.8 ms | **-91.1%** | **-96.5%** |
| only-matching output | 65.24 ms | 5.16 ms | **-92.1%** | **-96.7%** |
| forced-color output | 63.26 ms | 6.40 ms | **-89.9%** | **-96.5%** |
| many-small-files | 351.1 ms | 124.7 ms | **-64.5%** | **-96.4%** |
| recursive-tree | 365.3 ms | 155.9 ms | **-57.3%** | **-96.3%** |

The fixed multi-pattern gains are dominated by the accepted Aho-Corasick-style matcher. The BRE/ERE and output-heavy gains reflect prepared-input reuse, reduced record copying/materialization, and the later `Icod.CommandFramework 2.2.1` byte-mode construction improvement.

## 4. PCRE results

The dedicated PCRE command matrix also closes without a regression:

| Workload | Time change | Allocation change |
| --- | ---: | ---: |
| Unicode property | **-52.1%** | **-47.4%** |
| 32-pattern command path | **-51.1%** | **-56.6%** |
| dense `-o` | **-49.8%** | **-37.7%** |
| backreference | **-49.1%** | **-42.8%** |
| lookbehind | **-47.8%** | **-54.6%** |
| literal | **-46.0%** | **-54.6%** |
| long record | **-17.3%** | **-26.1%** |

The component-level JIT control continues to show the expected PCRE2 JIT benefit with zero managed allocation in both interpreted and JIT variants.

## 5. Shared record-reader controls

The shared materializing record-reader microbenchmarks intentionally remain production controls. Their managed allocation is unchanged between `1.5.0` and the `1.6.0` candidate at all measured record lengths.

Two-pass timing means are modestly improved:

- 80-byte records: **-16.0%**;
- 4,096-byte records: **-24.3%**;
- 262,144-byte records: **-9.8%**.

Individual pass timing variance remains visible on these short microbenchmarks, including small positive deltas in pass 2. Because allocation is exactly unchanged and the two-pass means are improved, there is no evidence of a shared-reader regression.

## 6. Regression review

No material release-blocking regression is present in the final matrix.

The only positive per-pass timing deltas occur in low-level control/microbenchmark cases and are inconsistent across passes:

- 80-byte record reader, pass 2: approximately +17.9%;
- 4,096-byte record reader, pass 2: approximately +2.5%;
- 262,144-byte record reader, pass 2: approximately +1.3%;
- PCRE long-record, pass 2: approximately +1.1%;
- interpreted PCRE JIT control, pass 2: approximately +3.9%.

Each corresponding two-pass mean is faster for the candidate, and allocation is either unchanged or improved. These are treated as ordinary timing variance rather than product regressions.

## 7. Release workflow audit

The release workflow remains appropriate for `1.6.0`:

- release tags must match `v<semver>`;
- the tagged commit must be contained in `main`;
- `<Version>` and `<PackageVersion>` must match the tag;
- Release restore/build/test/pack occurs before publication;
- the exact package artifact is verified and smoke-tested on Windows, Linux, and macOS;
- six RID archives are built and smoke-tested independently;
- NuGet.org and GitHub Packages publication wait on package validation;
- GitHub Release creation waits on package publication and all archive jobs;
- SHA-256 checksums are generated for the seven binary assets.

No release-workflow change is required by T6.9.

## 8. T6 closure decision

T6.0 through T6.9 are complete.

The final physical comparison confirms the intended performance/scalability improvements without a measurable two-pass mean regression. T6.8 stress/resilience evidence separately established practical scaling, cancellation responsiveness, bounded output backpressure, and controlled write/flush failure.

**The `Icod.Grep 1.6.0` implementation is performance-closure ready.**

Before merging PR #12, only ordinary repository hygiene remains: ensure final PR CI is green, synchronize any user-facing version examples that still mention `1.5.0`, and perform the final merge-readiness review. After merge to `main`, the normal Release validation should complete before pushing tag `v1.6.0`.
