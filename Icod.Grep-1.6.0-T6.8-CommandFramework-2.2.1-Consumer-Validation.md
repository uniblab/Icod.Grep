# Icod.Grep 1.6.0 — T6.8 CommandFramework 2.2.1 Consumer Validation

**Original T6.8 reference commit:** `14fa3392947e0f3703095fce2fee9dcd1e77005f`  
**Consumer-validation commit:** `6a2055f7f0d4c10b0910b55631f47bdf3d1bfb08`  
**Dependency change:** `Icod.CommandFramework 2.2.0` → `2.2.1`  
**Reference host hardware SHA-256:** `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Profile:** T6.8 `records`  
**Scenario count:** 15  
**Status:** accepted; large-record BRE/ERE allocation pathology materially reduced

## 1. Purpose

The first T6.8 physical record-scaling run exposed disproportionate managed allocation in the managed GNU BRE/ERE path. At 64 MiB, both BRE and ERE allocated approximately 1.544 GB, or roughly 23 times the input size.

Investigation traced a large avoidable component to byte-mode prepared-input construction in `Icod.CommandFramework 2.2.0`. The shared engine allocated full-capacity `List<Rune>`, `List<bool>`, and `List<int>` buffers and then copied those collections into their final arrays.

`Icod.CommandFramework 2.2.1` changes byte mode to allocate and populate the final arrays directly. Its focused physical package validation measured a 47.36% reduction in the shared prepared-input construction allocation.

This report verifies the result through the real Icod.Grep consumer path.

## 2. Dataset integrity

The focused rerun is authoritative for this comparison because:

- all 15 `records` scenarios succeeded;
- the report commit is `6a2055f7f0d4c10b0910b55631f47bdf3d1bfb08`;
- the hardware inventory hash exactly matches the original T6.8 reference host;
- the runtime remains .NET 10.0.11/x64; and
- the only production dependency change at the consumer-validation commit is `Icod.CommandFramework 2.2.0` → `2.2.1`.

The scaling harness also includes explicit warm-up before measurement in the new run. Consequently, the original first-scenario elapsed values are not directly comparable to their new counterparts. Managed-allocation comparisons remain the primary acceptance evidence.

## 3. BRE/ERE managed allocation

| Scenario | 2.2.0 allocation | 2.2.1 allocation | Reduction | Amplification before | Amplification after |
| --- | ---: | ---: | ---: | ---: | ---: |
| BRE, 1 MiB | 24,186,664 B | 14,667,744 B | **-39.36%** | 23.07× | 13.99× |
| ERE, 1 MiB | 24,181,856 B | 14,663,136 B | **-39.36%** | 23.06× | 13.98× |
| BRE, 16 MiB | 386,034,760 B | 234,978,288 B | **-39.13%** | 23.01× | 14.01× |
| ERE, 16 MiB | 386,034,736 B | 234,965,400 B | **-39.13%** | 23.01× | 14.01× |
| BRE, 64 MiB | 1,543,953,296 B | 939,882,232 B | **-39.12%** | 23.01× | 14.01× |
| ERE, 64 MiB | 1,543,952,584 B | 939,964,112 B | **-39.12%** | 23.01× | 14.01× |

The improvement is exceptionally consistent across record size and across both managed GNU syntax profiles. The consumer path saves approximately nine bytes of managed allocation for every input byte, matching the mechanism identified in CommandFramework.

The remaining roughly 14× amplification is still substantial, but the curve is linear and the avoidable list-plus-copy component has been removed. Further reduction would require a different representation strategy rather than another local construction cleanup and is not justified without separate measurement and semantic-risk analysis.

## 4. Control scenarios

The fixed-string and PCRE allocation controls remain effectively unchanged at the larger, reliable sizes:

| Scenario | 2.2.0 allocation | 2.2.1 allocation | Change |
| --- | ---: | ---: | ---: |
| fixed, 16 MiB | 67,249,288 B | 67,190,840 B | -0.09% |
| PCRE, 16 MiB | 67,249,056 B | 67,198,520 B | -0.08% |
| fixed, 64 MiB | 268,866,528 B | 268,848,880 B | -0.01% |
| PCRE, 64 MiB | 268,846,872 B | 268,849,544 B | ~0.00% |
| fixed file, 256 MiB | 1,075,526,608 B | 1,075,452,424 B | -0.01% |
| fixed file, 1 GiB | 4,301,763,576 B | 4,301,698,016 B | ~0.00% |

This isolation is important: the allocation reduction is specific to the managed BRE/ERE prepared-input path rather than a general harness or runtime shift.

## 5. Elapsed time

The new 64 MiB managed-regex cases also improve materially:

| Scenario | Original | CommandFramework 2.2.1 | Change |
| --- | ---: | ---: | ---: |
| BRE, 64 MiB | 1,302.7 ms | 763.3 ms | **-41.4%** |
| ERE, 64 MiB | 1,415.1 ms | 825.0 ms | **-41.7%** |

At 16 MiB, BRE is effectively flat to modestly faster while ERE is slower in this single stress observation. T6.8 is not using one-pass stress elapsed values as a narrow microbenchmark gate. The robust conclusion is that the allocation pathology is corrected and no large-record throughput regression is present at the largest measured record size.

## 6. Working-set observation

For the 64 MiB managed-regex scenarios, the observed process peak falls materially:

- BRE: approximately 1.380 GB → 0.779 GB (**-43.6%**);
- ERE: approximately 1.414 GB → 0.812 GB (**-42.6%**).

`ProcessPeakWorkingSetBytes` is cumulative for the benchmark process, so later scenarios can inherit an earlier peak. These values are therefore supporting evidence rather than an independent per-scenario memory measurement. They nevertheless agree with the managed-allocation reduction and remove the most concerning practical memory-pressure signal from the original run.

## 7. Acceptance decision

The CommandFramework 2.2.1 consumer change is accepted for Icod.Grep 1.6.0 because:

- BRE/ERE managed allocation falls by approximately **39.1%** in the real Grep large-record path;
- allocation amplification falls from approximately **23× to 14×** and remains linear from 1 through 64 MiB;
- fixed and PCRE controls are effectively unchanged;
- the largest BRE/ERE record cases are substantially faster rather than slower;
- observed peak working set falls by more than 40% in the targeted 64 MiB cases; and
- cross-platform PR CI is green with the published package.

The original T6.8 large-record prepared-input pathology is therefore **closed**.

## 8. Remaining T6.8 work

This result does not close T6.8 as a whole. Remaining exit gates are:

- sustained S4 cancellation responsiveness;
- sustained S5 output backpressure/failure behavior; and
- practical S6 constrained-resource behavior or a documented reason that a synthetic memory cap would not provide reliable cross-platform evidence.

The existing deterministic cross-platform smoke already establishes basic S4/S5 correctness. The next tranche should extend those dimensions with explicit sustained physical/reference scenarios and then close T6.8 if no new avoidable pathology appears.
