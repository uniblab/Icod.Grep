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

### 3.1 Two benchmark levels

T6 should use two complementary benchmark layers.

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

A framework such as BenchmarkDotNet is appropriate if it integrates cleanly with the repository and .NET 10. The benchmark project must never be included in the NuGet tool package.

#### B. End-to-end command benchmarks

Macro benchmarks must exercise the real `grep` command over deterministic fixture trees and files. They should measure workloads users actually care about:

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

Process-level benchmarks should include startup cost where that is part of the measured user experience, but startup and steady-state throughput must also be distinguishable.

### 3.2 Deterministic benchmark corpus

Benchmark data must be reproducible from source-controlled descriptions or deterministic generators. Do not check enormous opaque benchmark files into Git merely to make the benchmark suite convenient.

Each generated corpus should have:

- a stable scenario name;
- a deterministic seed where generation is probabilistic;
- explicit size/record-count parameters;
- a manifest or equivalent description;
- known expected match counts/output characteristics; and
- enough variation to prevent optimizations from accidentally targeting one artificial byte pattern.

Representative corpus classes should include:

1. **ASCII log-like text** — short lines, sparse matches.
2. **Dense-match text** — frequent selected spans and output pressure.
3. **UTF-8 multilingual text** — multi-byte runes and Unicode classes/case behavior.
4. **Long-line data** — records from KiB through multi-MiB sizes.
5. **Binary-ish data** — NUL discovery near the beginning and near the end of the probe window.
6. **Many-file trees** — thousands of small files and nested directories.
7. **Large pattern sets** — deterministic `-e` / `-f` collections with both hit and miss populations.

### 3.3 What to measure

At minimum, benchmark reports should capture:

- elapsed time / throughput;
- bytes processed per second for scanning workloads;
- operations or files processed per second where appropriate;
- managed allocations;
- Gen0/Gen1/Gen2 collection counts where meaningful;
- peak or representative working-set behavior for stress cases;
- startup versus steady-state time for process benchmarks; and
- output volume, since an output-heavy benchmark cannot be compared fairly with an output-suppressed one.

CPU time may be recorded where reliable, but elapsed time plus allocation data should remain the primary portable measurements.

### 3.4 How comparisons are judged

Performance comparisons must be made on the same machine/runtime configuration whenever possible. Hosted GitHub runners are useful for proving that benchmark code builds and runs, but their wall-clock variability makes them unsuitable for narrow hard performance thresholds.

T6 should therefore distinguish:

- **correctness gates** — mandatory in ordinary CI;
- **benchmark-smoke gates** — mandatory enough to prove benchmark scenarios still execute and validate expected results; and
- **performance comparisons** — produced as artifacts/reports from controlled manual or dedicated runs and used to justify optimization commits.

A proposed optimization should normally be accepted only when repeated measurements show a clear improvement larger than normal run-to-run noise. Tiny apparent gains that disappear across repetitions are not justification for added complexity.

Likewise, a local improvement that causes a significant regression in another common workload must be investigated rather than averaged away.

## 4. T6.0 — Benchmark foundation and 1.5.0 baseline

Before altering hot implementation paths:

1. Add a dedicated benchmark project under `benchmarks/`.
2. Keep benchmarks outside production packaging and ordinary test assemblies.
3. Create deterministic corpus generators and scenario manifests.
4. Establish microbenchmarks for key hot paths.
5. Establish end-to-end benchmarks for the principal user workloads.
6. Record an immutable or clearly identified `1.5.0` baseline report.
7. Add a manual or explicitly invoked performance workflow that publishes benchmark results as artifacts.
8. Add a light benchmark smoke to CI only if its execution cost remains reasonable.
9. Document machine/runtime metadata with every benchmark report.

### T6.0 exit criterion

No optimization tranche begins until we can answer, with measurements:

- where time is being spent;
- where allocations are concentrated;
- which workloads scale poorly as file size, record length, file count, or pattern count rises; and
- whether the limiting cost lives in `Icod.Grep`, `Icod.CommandFramework`, PCRE.NET/PCRE2, filesystem I/O, or unavoidable output volume.

## 5. T6.1 — Fixed-string search and multi-pattern scalability

This is the first likely optimization target because the current design represents fixed patterns individually and `PatternSet` considers its patterns independently. That is straightforward and semantically clear, but its cost can grow poorly as the number of patterns grows.

### Baselines to establish

Measure at least:

- one short fixed pattern;
- one long fixed pattern;
- 10, 100, 1,000, and 10,000 fixed patterns where practical;
- hit near the beginning, middle, end, and no hit;
- dense overlapping matches for `-o`;
- case-sensitive C/POSIX byte mode;
- case-insensitive byte mode;
- UTF-8 case-sensitive mode; and
- UTF-8 case-insensitive mode.

### Candidate optimizations

Depending on measurements:

1. Use the most efficient available span search for the single-pattern case.
2. Avoid repeated temporary allocations in case-insensitive scanning.
3. Introduce a dedicated multi-pattern automaton (for example an Aho-Corasick-style matcher) **only** for semantic subsets where it can reproduce grep's required match ordering exactly.
4. Consider threshold-based dispatch so small pattern sets do not pay the setup/memory cost of a large automaton.
5. Preserve pattern-source ordering where it is observable.
6. Keep UTF-8/case-folding semantics delegated to the established locale character-class provider rather than substituting ordinal shortcuts that change behavior.

### Important correctness constraint

An automaton is not automatically correct merely because it finds the same set of strings. It must integrate with `PatternSet`'s existing leftmost selection and longest tie handling, `-w`, `-x`, and repeated `-o` enumeration. If exact equivalence cannot be demonstrated, retain the simpler matcher for that semantic profile.

## 6. T6.2 — Pattern dispatch and regex hot paths

After fixed strings, profile the cost of `PatternSet` repeatedly invoking independent matchers for BRE, ERE, and PCRE pattern collections.

Investigate:

- repeated scans of the same record by multiple patterns;
- match-object and span allocation;
- repeated decoding or rune-boundary work;
- zero-length-match advancement;
- `-w` and `-x` post-filter retry behavior;
- `FindAll` behavior under dense `-o`/color workloads; and
- compilation cost for large pattern-file inputs versus steady-state matching cost.

Possible improvements may include specialized single-pattern paths, reduced wrapper allocation, better reuse of decoded metadata, or safe matcher-specific batch strategies.

Do **not** combine BRE/ERE expressions into a single synthetic regular expression unless exact capture, alternation, error, and match-selection semantics are proven equivalent. Performance work must not turn independent GNU patterns into subtly different language semantics.

## 7. T6.3 — Record pipeline and very-large-record scalability

The current grep command uses a materializing byte-record reader, which is convenient and robust but means a complete record is represented in memory before matching. This deserves deliberate measurement for long-line workloads.

`Icod.CommandFramework` already exposes segmented record-reading infrastructure beneath the materializing reader. T6 should evaluate whether `Icod.Grep` can exploit that infrastructure without compromising regex semantics that require access to the complete logical record.

### Investigation questions

- How much allocation is attributable to record materialization on ordinary lines?
- At what line size does materialization dominate runtime or working set?
- Which matcher modes can operate incrementally and which fundamentally require the whole record?
- Can fixed-string/no-context/no-color cases stream safely while BRE/ERE/PCRE retain materialization?
- Can pooled buffers reduce large-record allocation without dangerous lifetime complexity?
- Can record storage be reused between iterations without retaining unexpectedly large buffers forever?

### Guardrails

- GNU grep accepts very long records; T6 must not introduce artificial line-length limits.
- Buffer pooling must never expose stale bytes in output or matching.
- A fast streaming path must fall back cleanly when an option requires full-record spans, coloring, context retention, or regex semantics.

## 8. T6.4 — Binary probing and input pipeline

`Icod.Grep` probes an initial input prefix for NUL bytes before ordinary record processing. Seekable streams are rewound; non-seekable streams are reconstructed with a prefix stream.

Profile:

- cost of the 96 KiB probe on small files;
- duplicate memory copying for non-seekable input;
- interaction with Windows text translation;
- many-small-file workloads where probe overhead may dominate; and
- binary files with early versus late NULs.

Candidate improvements include early termination after discovering NUL, reducing unnecessary zeroing/copying, reusing probe storage where safe, or integrating probing more closely with the first record read.

Any change must preserve the established binary-policy contract and exact stdin behavior.

## 9. T6.5 — Output, formatting, color, and context allocation

Output-heavy grep can become limited by formatting and writes rather than matching. Profile:

- selected-record output;
- `-o` with many spans;
- filename/line/byte prefixes;
- context grouping;
- `--color=always` with dense matches;
- counts and filename-only modes; and
- line-buffered output.

Investigate:

- repeated one-byte temporary arrays;
- numeric formatting allocation;
- UTF-8 encoding of frequently repeated separators/prefix text;
- write call count and batching opportunities;
- color SGR encoding/reuse; and
- unnecessary construction of match-span collections when output does not need them.

Optimization must preserve exact output bytes. This is an area where allocation reductions may be valuable even when total wall time is dominated by the destination stream.

## 10. T6.6 — Filesystem traversal and many-file scalability

Use deterministic directory trees to separate matching cost from traversal/open cost.

Measure:

- thousands of tiny files;
- deep directory trees;
- include/exclude-heavy traversals;
- no-match and sparse-match cases;
- `-r` versus `-R`; and
- filename-output modes that can terminate each file early.

Investigate avoidable allocations and observations around path filtering, device checks, and file opening.

The default design remains deterministic and sequential. Parallel traversal is out of scope unless later measurements show it is compelling enough to justify a separately designed ordered-execution model.

## 11. T6.7 — PCRE-specific profiling

PCRE.NET 1.6.0 / PCRE2 10.48 is a native-backed matcher with different costs from the managed BRE/ERE engine. Treat it separately rather than assuming managed optimizations transfer.

Measure:

- compile cost versus match cost;
- simple literals expressed through `-P`;
- lookarounds and backreferences;
- Unicode properties;
- valid versus malformed UTF-8 under the existing matching policy;
- large records;
- multiple PCRE patterns; and
- dense `-o` enumeration.

Investigate whether PCRE.NET exposes safe opportunities around JIT use, reusable match data, or reduced wrapper allocation. Do not introduce unsupported native lifetime tricks merely for benchmark numbers.

## 12. T6.8 — Stress, resource behavior, and scalability limits

Performance is not only throughput. The release should establish deliberate behavior as workload sizes increase.

Stress scenarios should include:

- very large records;
- large files;
- tens of thousands of files;
- very large fixed-pattern sets;
- large BRE/ERE pattern sets;
- large PCRE pattern sets within practical native limits;
- cancellation during long scans;
- output backpressure/failure; and
- constrained-memory conditions where practical.

Record whether growth is approximately linear, super-linear, or dominated by a specific setup structure. The roadmap should be updated when measurements reveal actual scalability ceilings.

T6 should prefer graceful explicit failure over catastrophic allocation or uncontrolled resource growth where a meaningful controlled behavior exists, but must not invent arbitrary GNU-incompatible limits simply to simplify implementation.

## 13. T6.9 — Optional GNU comparison measurements

GNU grep 3.12 may be included as a **reference competitor** in controlled Linux macrobenchmark reports. This can help identify where `Icod.Grep` is disproportionately expensive and whether a proposed optimization moves in the right direction.

However:

- GNU timings are informational, not pass/fail gates;
- `Icod.Grep` should first be compared to its own `1.5.0` baseline;
- platform-specific semantics must remain comparable;
- benchmark scenarios must record the exact GNU version; and
- no optimization should copy a native implementation technique that conflicts with managed safety, portability, or repository architecture.

A full GNU differential-conformance harness is a separate possible future project and is not required to complete T6.

## 14. CI and workflow policy

Performance work needs stronger discipline than simply printing benchmark numbers in ordinary PR logs.

### Ordinary PR CI

Continue to require:

- full Windows/Linux/macOS build and test;
- exact Staging package validation;
- installed-package smoke;
- Windows text-versus-`-U` regression smoke; and
- all six RID archive smokes.

Add only lightweight benchmark validation here: benchmark projects build, deterministic fixtures generate correctly, and a very small smoke subset returns expected results.

### Dedicated performance workflow

Add a manually triggered workflow capable of:

- selecting benchmark groups;
- recording commit SHA, OS, architecture, .NET SDK/runtime, CPU, and memory metadata;
- running sufficient warmup/repetition;
- exporting machine-readable results plus a human-readable summary; and
- uploading the reports as workflow artifacts.

Where practical, support comparing the current commit against the pinned `1.5.0` baseline in the same workflow invocation so environmental drift affects both sides similarly.

Do not establish fragile "must be 3% faster" gates on ephemeral hosted runners. If a future stable self-hosted benchmark environment is available, stricter regression thresholds can be considered separately.

## 15. Optimization acceptance checklist

Every optimization PR/commit should answer the following questions:

1. **Which measured workload is slow or allocation-heavy?**
2. **What baseline numbers demonstrate the problem?**
3. **What implementation change addresses that measured cause?**
4. **What before/after measurements demonstrate improvement?**
5. **What workloads were checked for regressions?**
6. **Which semantic tests prove behavior is unchanged?**
7. **Does the optimization add memory/setup cost that needs a threshold or fallback?**
8. **Does it affect C/POSIX, UTF-8, Windows text mode, or `-U` differently?**
9. **Does it introduce concurrency, pooling, unsafe code, native lifetime management, or other complexity requiring additional tests?**
10. **Is the complexity justified by the measured gain?**

A simpler implementation with essentially the same performance is preferred over a clever implementation whose benefit is within measurement noise.

## 16. Versioning and documentation policy

Once implementation work begins, maintain all version surfaces at `1.6.0`:

- `<Version>`;
- `<PackageVersion>`;
- `<AssemblyVersion>`;
- `grep --version`; and
- package release notes.

The README should not advertise benchmark percentages until the final benchmark suite and representative results are stable. Release notes should describe qualitative improvements and carefully scoped quantitative results, including enough test context to avoid implying universal speedups.

A final `Icod.Grep 1.6.0 Performance Report` may be added near release closure summarizing representative `1.5.0` → `1.6.0` changes, measured on identified hardware/runtime configurations.

## 17. Proposed tranche order

### T6.0 — Measurement foundation

Benchmark project, deterministic corpora, macro runner, baseline capture, reporting workflow.

### T6.1 — Fixed-string and multi-pattern search

Attack the most obvious pattern-count scalability problem, but only after T6.0 quantifies it.

### T6.2 — Matcher dispatch and regex hot paths

Reduce repeated scanning/decoding/allocation where measurements justify it.

### T6.3 — Record pipeline and long-line memory

Improve allocation and scalability for long records, potentially using specialized streaming paths where semantics permit.

### T6.4 — Binary/input pipeline

Reduce probe and first-read overhead, especially for many-small-file workloads.

### T6.5 — Output and formatting

Reduce write count and formatting/color allocation while preserving exact bytes.

### T6.6 — Traversal scalability

Optimize many-file and path-filtering workloads without changing deterministic ordering.

### T6.7 — PCRE-specific tuning

Profile and optimize the native-backed path independently.

### T6.8 — Stress and resource closure

Validate scaling behavior, cancellation, failure modes, and memory characteristics.

### T6.9 — Release performance report and documentation

Consolidate results, audit all compatibility gates, update release notes, and prepare `1.6.0` for merge/tagging.

The order after T6.0 is intentionally revisable. Measurements, not roadmap numbering, decide which hot path deserves attention next.

## 18. Definition of done for `1.6.0`

`1.6.0` is ready only when:

- a reproducible benchmark framework exists and is documented;
- the `1.5.0` baseline is captured;
- benchmark scenarios cover representative file-size, file-count, pattern-count, matcher, locale, and output regimes;
- at least the materially significant measured bottlenecks identified by T6.0 have been addressed or explicitly documented as external/unavoidable;
- accepted optimizations show credible measured improvements in their target scenarios;
- no material regression is knowingly introduced in common workloads without an explicit, justified tradeoff;
- all `1.5.0` semantic/conformance tests remain green;
- Windows/Linux/macOS package smoke and all six RID archive smokes remain green;
- package/version metadata is consistently `1.6.0`;
- release documentation accurately describes what improved and under what benchmark conditions; and
- no benchmark-only shortcut has leaked into production behavior.

The success criterion for T6 is not a single headline number. It is a faster, more scalable implementation **plus a trustworthy framework for proving and preserving those gains in every release that follows**.
