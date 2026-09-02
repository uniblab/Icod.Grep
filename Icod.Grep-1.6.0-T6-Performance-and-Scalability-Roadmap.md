# Icod.Grep 1.6.0 — T6 Performance and Scalability Roadmap

**Baseline:** `main` / merged `1.5.0` at `423c0e9623100492fa01b6e4d14c183761d111d7`  
**Target release:** `1.6.0`  
**Theme:** performance, memory efficiency, and scalability without semantic regression  
**Compatibility reference:** the complete `1.5.0` GNU grep 3.12 behavioral contract  
**Status:** roadmap / measurement design

## 1. Objective

Releases `1.1.0` through `1.5.0` concentrated on command semantics, color, locale/encoding behavior, PCRE support, and the remaining GNU grep 3.12 compatibility edges. That gives `1.6.0` a stable behavioral baseline against which performance work can be judged.

T6 will make `Icod.Grep` materially faster and more memory-efficient on realistic workloads **without changing its observable grep semantics**.

The governing rule for this release is:

> **Measure first. Optimize second. Preserve behavior always.**

No optimization will be accepted merely because it looks faster in source code. Each optimization must be connected to a reproducible benchmark or stress case, must demonstrate a credible improvement in the workload it targets, and must continue to pass the full functional/conformance suite inherited from `1.5.0`.

T6 is explicitly **not** a mandate to outperform GNU grep in every workload. GNU grep is a highly optimized native program with decades of specialized work behind it. The goal is to identify avoidable costs in the managed implementation, remove them carefully, and establish a repeatable performance engineering discipline for future releases.

## 2. Non-negotiable compatibility guardrails

Performance work must not change any established `Icod.Grep 1.5.0` contract, including:

- BRE, ERE, fixed-string, and PCRE matching semantics;
- leftmost match selection and longest-match tie behavior where grep owns span selection;
- `-i`, `-w`, `-x`, `-v`, and `-o` behavior;
- C/POSIX byte semantics and supported UTF-8 locale semantics;
- malformed-UTF handling and binary-file policy;
- byte offsets, line numbers, filename prefixes, and context grouping;
- `-z` NUL-delimited records;
- recursive traversal ordering and include/exclude behavior;
- Windows default text mode versus `-U` binary mode;
- color/highlighting output bytes;
- diagnostics and exit status;
- seekable-standard-input `-m` repositioning behavior;
- package/native PCRE payload requirements; and
- the six supported release RIDs.

T6 must not gain speed by silently weakening diagnostics, skipping difficult records, reordering visible output, narrowing supported inputs, or changing the meaning of patterns.

Parallel traversal or parallel matching is therefore **not** a default T6 assumption. GNU-visible ordering makes concurrency a semantic design problem, not merely a performance switch. It may be investigated experimentally, but it will not be adopted unless deterministic output and resource behavior remain explicit and well tested.

## 3. Performance engineering methodology

### 3.1 Reference-host model

T6 does **not** require privately controlled Linux or macOS hardware in order to make trustworthy performance decisions.

The canonical quantitative performance environment for `1.6.0` is a stable physical Windows development host. Serious before/after performance claims are based on repeated measurements of `Icod.Grep 1.5.0` and the candidate `1.6.0` build on that same host under the same runtime, corpus, and benchmark configuration.

The environments have deliberately different roles:

- **Physical Windows reference host:** authoritative for controlled `1.5.0` → `1.6.0` timing, throughput, allocation, GC, working-set, startup, and scalability comparisons.
- **GitHub-hosted Windows:** mandatory correctness and benchmark-smoke environment; hosted timing data may be retained as secondary diagnostic evidence.
- **GitHub-hosted Linux and macOS:** mandatory correctness, portability, and benchmark-smoke environments; timing data is observational and suitable for finding gross regressions, not for narrow percentage claims.
- **Six RID archive jobs:** packaging and execution validation, not performance benchmarking.

Linux/macOS observations still matter. A change that becomes dramatically slower, allocates unexpectedly, times out, or otherwise behaves pathologically on a hosted non-Windows runner must be investigated. Normal hosted-runner timing variation, however, must not be interpreted as a precise regression or improvement.

A Windows result such as “25% faster” must therefore be reported as a result on the identified Windows reference host. T6 will not imply that the same percentage applies to Linux or macOS without controlled measurements on those platforms.

If controlled Linux or macOS hardware becomes available later, it may establish an additional independent reference series. Its measurements supplement rather than invalidate the Windows series.

### 3.2 Required host and run metadata

Every retained benchmark report must identify enough of its environment to make later comparisons meaningful. Record, where available:

- operating system and version;
- architecture;
- CPU vendor/model;
- logical processor count;
- installed/available physical memory;
- .NET SDK and runtime versions;
- runtime architecture;
- GC mode and relevant runtime configuration;
- benchmark framework/configuration identity;
- `Icod.Grep` commit SHA and package/version identity;
- corpus/scenario identity and generator version;
- whether the run is from the physical reference host or hosted CI; and
- date/time of the run.

On the physical reference host, also record useful stability information when practical, such as power plan, virtualization status, processor frequency information, and whether the repository/worktree was clean.

Results from materially different environments must not be silently combined into one trend line or one aggregate score.

### 3.3 Two benchmark levels

T6 uses two complementary benchmark layers.

#### A. In-process component benchmarks

A dedicated benchmark project should exercise hot implementation paths without process startup noise. Candidate subjects include:

- fixed-string search;
- `PatternSet` dispatch across one, several, hundreds, and thousands of patterns;
- BRE/ERE matching through `Icod.CommandFramework.RegularExpressions`;
- PCRE matching through PCRE.NET;
- record materialization and scanning;
- UTF-8 versus byte-oriented matching;
- case-insensitive fixed-string matching;
- only-matching span enumeration;
- prefix formatting and colored-record output; and
- Windows text-mode translation streams.

BenchmarkDotNet is appropriate if it integrates cleanly with .NET 10 and the repository. Benchmark code must never enter the NuGet tool package.

#### B. End-to-end command benchmarks

Macro benchmarks must exercise the real `grep` command over deterministic fixture trees and files. Principal workloads include:

- one large file;
- many small files;
- recursive directory trees;
- no-match, sparse-match, and dense-match cases;
- short and very long records;
- one pattern versus large pattern sets;
- BRE, ERE, fixed strings, and PCRE;
- C/POSIX byte mode and UTF-8 mode;
- `-i`, `-w`, `-x`, `-o`, context, and count-only modes;
- binary probing and binary-policy cases; and
- Windows CRLF/default-text and `-U` cases where relevant.

Process-level benchmarks should distinguish startup cost from steady-state scanning throughput.

### 3.4 Deterministic benchmark corpus

Benchmark data must be reproducible from source-controlled descriptions or deterministic generators. Do not check enormous opaque benchmark files into Git merely for convenience.

Each generated corpus should have:

- a stable scenario name;
- a deterministic seed where generation is probabilistic;
- explicit size/record-count parameters;
- a manifest or equivalent description;
- known expected match counts/output characteristics; and
- enough variation to prevent optimizations from targeting one artificial byte pattern.

Representative corpus classes include:

1. **ASCII log-like text** — short lines, sparse matches.
2. **Dense-match text** — frequent selected spans and output pressure.
3. **UTF-8 multilingual text** — multi-byte runes and Unicode classes/case behavior.
4. **Long-line data** — records from KiB through multi-MiB sizes.
5. **Binary-ish data** — NUL discovery near the beginning and near the end of the probe window.
6. **Many-file trees** — thousands of small files and nested directories.
7. **Large pattern sets** — deterministic `-e` / `-f` collections with both hit and miss populations.

### 3.5 What to measure

At minimum, benchmark reports should capture:

- elapsed time / throughput;
- bytes processed per second for scanning workloads;
- operations or files processed per second where appropriate;
- managed allocations;
- Gen0/Gen1/Gen2 collection counts where meaningful;
- peak or representative working-set behavior for stress cases;
- startup versus steady-state time for process benchmarks; and
- output volume, since output-heavy and output-suppressed cases are not directly comparable.

CPU time may be recorded where reliable, but elapsed time plus allocation data remain the primary portable measurements.

### 3.6 How comparisons are judged

Authoritative optimization comparisons are made on the same physical Windows reference host, runtime configuration, benchmark configuration, and corpus whenever possible.

A proposed optimization should normally be accepted only when repeated reference-host measurements show a clear improvement larger than normal run-to-run noise. Tiny apparent gains that disappear across repetitions do not justify added complexity.

A local improvement that causes a significant regression in another common workload must be investigated rather than averaged away.

Hosted GitHub runners serve a different purpose. They may emit timings and allocation observations as diagnostic artifacts, but their measurements are **non-authoritative** for narrow comparisons because VM assignment, processor generation, contention, thermal state, and host load can vary between runs.

T6 therefore distinguishes:

- **correctness gates** — mandatory in ordinary CI on all supported host families;
- **benchmark-smoke gates** — prove benchmark code, deterministic fixtures, expected-result validation, and platform execution remain healthy;
- **gross-regression observations** — repeated hosted measurements may flag obvious pathological changes for investigation, but small timing deltas do not fail a PR; and
- **controlled performance comparisons** — authoritative before/after reports from the physical Windows reference host used to justify optimization commits and the final `1.6.0` performance report.

There will be no single cross-platform “performance score.” Results remain workload-by-workload and environment-by-environment.

## 4. T6.0 — Benchmark foundation and 1.5.0 baseline

Before altering hot implementation paths:

1. Add a dedicated benchmark project under `benchmarks/`.
2. Keep benchmarks outside production packaging and ordinary test assemblies.
3. Create deterministic corpus generators and scenario manifests.
4. Establish microbenchmarks for key hot paths.
5. Establish end-to-end benchmarks for the principal user workloads.
6. Capture the immutable `1.5.0` baseline on the physical Windows reference host.
7. Add a manual or explicitly invoked performance workflow that publishes hosted benchmark results as diagnostic artifacts.
8. Add a light benchmark smoke to ordinary CI only if its execution cost remains reasonable.
9. Make every report emit the required host/runtime/corpus metadata from §3.2.
10. Document a repeatable local command/procedure for collecting authoritative Windows reference-host baseline and candidate measurements.
11. Keep hosted Linux/macOS benchmark observations clearly labeled as non-authoritative and non-comparable to the physical Windows series.

### T6.0 exit criterion

No optimization tranche begins until controlled Windows reference-host measurements can answer:

- where time is being spent;
- where allocations are concentrated;
- which workloads scale poorly as file size, record length, file count, or pattern count rises; and
- whether the limiting cost lives in `Icod.Grep`, `Icod.CommandFramework`, PCRE.NET/PCRE2, filesystem I/O, or unavoidable output volume.

Additionally, benchmark-smoke scenarios must execute successfully on Windows, Linux, and macOS CI so the benchmark infrastructure itself does not accidentally become Windows-only.

## 5. T6.1 — Fixed-string search and multi-pattern scalability

This is the first likely optimization target because the current design represents fixed patterns individually and `PatternSet` considers its patterns independently. Its cost can therefore grow poorly as pattern count rises.

Establish baselines for one short/long fixed pattern; 10, 100, 1,000, and 10,000 patterns where practical; early/middle/late/no hits; dense overlapping `-o`; byte and UTF-8 modes; and case-sensitive/case-insensitive operation.

Candidate optimizations include efficient span search for the single-pattern case, allocation reduction in case-insensitive scanning, and a dedicated multi-pattern automaton only for semantic subsets where exact GNU match ordering can be preserved. Threshold-based dispatch should avoid imposing automaton setup cost on small pattern sets.

An automaton is not correct merely because it finds the same set of strings. It must preserve `PatternSet` leftmost selection, longest ties, `-w`, `-x`, and repeated `-o` enumeration.

## 6. T6.2 — Pattern dispatch and regex hot paths

Profile repeated scans of the same record by multiple patterns, match/span allocation, repeated decoding, zero-length advancement, `-w`/`-x` retries, dense `FindAll`, and pattern compilation cost.

Possible improvements include specialized single-pattern paths, reduced wrapper allocation, reuse of decoded metadata, or safe matcher-specific batch strategies.

Do not combine independent BRE/ERE expressions into synthetic regular expressions unless exact language and match-selection equivalence is demonstrated.

## 7. T6.3 — Record pipeline and very-large-record scalability

Measure record materialization cost from ordinary lines through multi-MiB records. Evaluate whether segmented infrastructure can be exploited for semantic subsets that can genuinely stream, while BRE/ERE/PCRE and output modes requiring complete spans retain full-record access.

Investigate pooled/reused storage carefully. T6 must not introduce artificial line-length limits, stale-buffer exposure, or unbounded retention of exceptionally large buffers.

## 8. T6.4 — Binary probing and input pipeline

Profile the initial binary probe, small-file overhead, non-seekable prefix copying, Windows text translation interaction, and early versus late NUL discovery.

Candidate improvements include early termination after NUL discovery, reduced copying/zeroing, safe probe-storage reuse, or tighter integration with the first record read. Existing binary policy and stdin behavior remain exact compatibility requirements.

## 9. T6.5 — Output, formatting, color, and context allocation

Profile selected-record output, dense `-o`, filename/line/byte prefixes, context grouping, forced color, count/filename modes, and line-buffered output.

Investigate numeric-formatting allocation, repeated separator encoding, one-byte temporaries, write count/batching, SGR reuse, and unnecessary span collection. Exact output bytes remain mandatory.

## 10. T6.6 — Filesystem traversal and many-file scalability

Use deterministic directory trees to measure thousands of tiny files, deep trees, include/exclude-heavy traversal, no/sparse matches, `-r`/`-R`, and filename-only modes.

Optimize avoidable path/filter/open overhead while preserving deterministic sequential visible ordering. Parallel traversal remains out of scope unless later evidence justifies a separately designed ordered-execution model.

## 11. T6.7 — PCRE-specific profiling

Treat PCRE.NET 1.6.0 / PCRE2 10.48 separately from managed BRE/ERE. Measure compile versus match cost, simple literals, lookarounds/backreferences, Unicode properties, malformed UTF-8 policy, large records, multiple PCRE patterns, and dense `-o`.

Investigate only supported PCRE.NET opportunities such as JIT or reusable native-backed structures. Do not introduce unsafe lifetime tricks for benchmark numbers.

## 12. T6.8 — Stress, resource behavior, and scalability limits

Stress very large records/files, tens of thousands of files, very large fixed/BRE/ERE/PCRE pattern sets, cancellation, output backpressure/failure, and constrained-memory situations where practical.

Record scaling behavior and actual ceilings. Prefer graceful explicit failure where meaningful, but do not invent arbitrary GNU-incompatible limits merely to simplify implementation.

## 13. Optional GNU comparison measurements

GNU grep 3.12 may be used as an informational reference competitor where a suitable environment exists, but GNU timings are not required for T6 and are never pass/fail gates.

Because no controlled Linux host is assumed for this release, GNU comparison is explicitly optional. `Icod.Grep` is judged first against its own pinned `1.5.0` baseline on the Windows reference host.

A full GNU differential-conformance harness remains a separate future project.

## 14. CI and workflow policy

### Ordinary PR CI

Continue to require:

- full Windows/Linux/macOS build and test;
- exact Staging package validation;
- installed-package smoke;
- Windows text-versus-`-U` regression smoke;
- all six RID archive smokes; and
- lightweight benchmark build/fixture/smoke validation when T6.0 adds the benchmark project.

Hosted benchmark-smoke