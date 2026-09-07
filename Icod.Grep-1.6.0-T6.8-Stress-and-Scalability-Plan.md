# Icod.Grep 1.6.0 — T6.8 Stress, Resource Behavior, and Scalability Plan

**Starting head:** `b9d57ea230ab9df64346b53f24fccae9fa71a5e0`  
**Target release:** `1.6.0`  
**Status:** active tranche

## 1. Objective

T6.8 is not another narrow hot-path optimization tranche. Its purpose is to establish how the current `1.6.0` candidate behaves as workload size and resource pressure increase, and to identify any practical ceiling, pathological growth curve, or non-graceful failure that should be corrected before release.

The governing rule remains:

> Measure first. Optimize second. Preserve behavior always.

Stress cases must distinguish expected cost from avoidable implementation behavior. A workload that is necessarily expensive is not a defect merely because it is large.

## 2. Stress dimensions

T6.8 will cover six independent dimensions.

### S1 — very large records and files

Exercise:

- single records from 1 MiB through at least 64 MiB where practical;
- files from tens of MiB through at least 1 GiB where practical;
- no-match, early-match, late-match, and dense-match variants;
- fixed, BRE, ERE, and PCRE modes;
- count-only versus selected-record output.

Record elapsed time, managed allocation, GC activity, and peak/representative working set where practical.

### S2 — very large file counts

Exercise deterministic trees containing:

- 1,000;
- 10,000; and
- 50,000 files where practical.

Cover flat and nested layouts, include/exclude filtering, no-match/sparse-match cases, and `-r` versus `-R` where relevant. Confirm traversal remains deterministic and resource usage does not grow with total file count beyond retained traversal state.

### S3 — very large pattern sets

Measure fixed, BRE, ERE, and PCRE sets at increasing sizes.

Initial targets:

- 100;
- 1,000;
- 10,000 patterns where the engine remains practical.

Fixed-string tests should exercise the optimized multi-pattern matcher. BRE/ERE/PCRE tests should identify compile-time and steady-state scaling separately where possible.

No arbitrary production pattern-count limit will be introduced merely because a stress case is expensive.

### S4 — cancellation

Exercise cancellation during:

- large-record scanning;
- very large pattern-set matching;
- recursive traversal; and
- output-heavy operation.

Cancellation must terminate promptly enough to be operationally useful, return the established canceled status, and avoid corrupted output or leaked resources.

### S5 — output backpressure and failure

Use controlled streams that:

- delay writes;
- accept bounded chunks;
- fail after a deterministic byte count; and
- throw during flush/completion.

Confirm Grep does not buffer unbounded output, deadlock, swallow I/O failures, or replace the established GNU-compatible error status with an unrelated exception.

### S6 — constrained resource behavior

Where practical on the physical reference host, run selected large-record, large-file, and large-pattern cases under reduced available-memory conditions or an equivalent bounded harness.

The purpose is to identify avoidable retention and catastrophic growth, not to define a synthetic minimum-memory guarantee.

## 3. Harness design

T6.8 should add a dedicated stress entry point rather than forcing every stress case through BenchmarkDotNet. BenchmarkDotNet remains useful for bounded scaling curves, but cancellation, output failure, huge file trees, and working-set observations are better represented by deterministic scenario runners.

The harness should:

1. generate fixtures deterministically;
2. validate expected command status and output characteristics;
3. emit machine-readable JSON results;
4. record commit, runtime, OS, architecture, CPU, memory, and hardware-inventory hash;
5. record scenario parameters explicitly;
6. record elapsed time, managed allocation when measurable, GC counts, and working-set observations where practical;
7. clean up generated fixtures after each run; and
8. support a small CI smoke profile plus larger explicit local profiles.

## 4. CI policy

Ordinary PR CI should run only a bounded stress smoke that proves the harness and representative edge cases work on Windows, Linux, and macOS.

Large file-count, large-file, and constrained-resource profiles remain explicit physical-reference-host diagnostics. Hosted CI timing is not authoritative.

## 5. First implementation tranche

The first code tranche should establish infrastructure before introducing large fixtures:

1. `StressScenario` / result records;
2. deterministic temporary-fixture management;
3. a `--stress-smoke` benchmark-project entry point;
4. JSON result serialization;
5. cancellation and failing-output test streams;
6. small representative S1–S5 smoke cases; and
7. cross-platform CI invocation of the smoke profile.

Only after that foundation is green should the physical large-scale profiles be added and measured.

## 6. T6.8 exit criteria

T6.8 closes when:

- the large-record/file scaling curve is documented;
- the many-file scaling curve is documented;
- large fixed/BRE/ERE/PCRE pattern-set behavior is documented;
- cancellation remains operationally responsive in sustained work;
- output backpressure/failure behavior is bounded and correct;
- no avoidable unbounded retention or catastrophic resource pathology remains; and
- any practical ceilings discovered are documented rather than hidden.

If a stress case exposes a concrete implementation defect, fix and remeasure it before closure. Otherwise, retain the measured ceiling as documentation and proceed to T6.9 release closure.
