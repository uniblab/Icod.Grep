# Icod.Grep 1.6.0 — T6.8 Resilience and Resource Closure

**Reference-host commit:** `42d271da42cee859cae1a4fa61de8e5069dc4620`  
**Hardware inventory SHA-256:** `d73c6e3314dc77d24dd2b28a51221d9b77b5cc6b9796ae00fe8c9c0d92821c9b`  
**Runtime:** .NET 10.0.11, x64, workstation GC  
**Profile:** sustained S4/S5 resilience  
**Scenarios:** 7  
**Status:** all scenarios succeeded

## 1. Purpose

This run closes the sustained cancellation and output-failure portions of T6.8 after the earlier S1-S3 physical scaling work and the `Icod.CommandFramework 2.2.1` large-record allocation correction.

The resilience harness intentionally exercises operational behavior rather than narrow benchmark throughput. Fixture generation is outside measured command time. The measured command region records elapsed time, managed allocation, GC counts, working set, process peak working set, output bytes, status, and scenario-specific validation.

## 2. S4 — sustained cancellation responsiveness

All cancellation scenarios returned the established canceled status (`130`) and completed within the explicit 2,000 ms operational deadline.

| Scenario | Trigger | Completion | Allocation | GC | Result |
| --- | --- | ---: | ---: | --- | --- |
| 64 MiB BRE record | cancel after 50 ms | **485 ms** | 64,720 B | none | pass |
| 1,000 BRE patterns / 8 MiB input | cancel after 50 ms | **59 ms** | 2,582,544 B | Gen0: 1 | pass |
| 10,000-file recursive traversal | cancel after 50 ms | **120 ms** | 44,560 B | none | pass |
| output-heavy 100,000-record stream | cancel after 50 ms | **64 ms** | 156,776 B | none | pass |

The large-record BRE case is the slowest response because cancellation can only be observed at explicit checks inside record preparation and matching. Even so, 485 ms remains well inside the selected 2-second operational bound and is acceptable for the current release.

The other sustained workloads terminate close to the trigger time. No scenario leaked output state or returned an unrelated error status.

## 3. S5 — output backpressure and failure

### 3.1 Sustained backpressure

The bounded counting sink retained **zero** output bytes while applying a 1 ms delay every 64 writes.

- input/output records: 20,000;
- accepted output bytes: **300,000**;
- writes: **40,000**;
- maximum single write: **14 bytes**;
- elapsed time: **6,368.4 ms**;
- managed allocation: **7,237,304 B**;
- collections: **Gen0 3, Gen1 0, Gen2 0**;
- status: success.

The command did not accumulate output in memory while the sink was deliberately slower than the producer. Working set after the scenario was lower than before it, so there is no evidence of retained backpressure buffering.

### 3.2 Deterministic write failure

The write sink failed after 65,536 bytes of capacity. Grep stopped with its established controlled error status (`2`) after accepting **65,535 bytes** and surfaced the deterministic output-failure diagnostic.

- elapsed time: **4.94 ms**;
- allocation: **1,585,568 B**;
- no GC collections;
- no unhandled exception escaped the command boundary.

### 3.3 Deterministic flush/completion failure

A flush failure after 2,000 output records also returned status `2` with the expected diagnostic.

- accepted output bytes before failure: **30,000**;
- elapsed time: **1.45 ms**;
- allocation: **784,400 B**;
- no GC collections.

Both failure modes therefore preserve the command's normal I/O error contract.

## 4. S6 — constrained-resource decision

No additional synthetic memory-cap experiment is required for T6.8.

The physical scaling work already exercised the dominant resource risks directly:

- a 1 GiB short-record file succeeds with bounded working set;
- 50,000-file traversal remains linear and bounded;
- large fixed, BRE, ERE, and PCRE pattern sets were measured explicitly;
- 64 MiB single-record BRE/ERE amplification was identified, traced into CommandFramework, corrected in `2.2.1`, and remeasured end-to-end;
- the corrected BRE/ERE curve remains linear at about 14x cumulative managed allocation rather than the prior approximately 23x;
- sustained backpressure retains no growing output buffer;
- cancellation and output failure remain controlled.

A process-wide synthetic memory cap would mix several unrelated mechanisms: managed GC hard limits, native PCRE2 allocations, operating-system file cache behavior, memory-mapped/runtime state, and process working-set trimming. Such a result would not define a portable Grep memory guarantee and could easily overstate or understate real failure behavior.

For this release, the more trustworthy evidence is the measured allocation/working-set scaling on the fixed physical host plus controlled operational failure behavior. T6.8 therefore records **no synthetic minimum-memory guarantee** and does not add an arbitrary file, record, or pattern ceiling.

## 5. T6.8 exit criteria

The T6.8 exit criteria are satisfied:

- large-record and large-file curves are documented;
- many-file scaling is documented through 50,000 files;
- fixed/BRE/ERE/PCRE pattern-set scaling is documented;
- sustained cancellation remains operationally responsive;
- output backpressure is bounded;
- write and flush failures remain controlled;
- the one concrete catastrophic allocation pathology found during stress testing was corrected and remeasured; and
- practical resource ceilings and non-guarantees are documented rather than hidden.

## 6. Decision

**T6.8 is closed.**

No further production change is justified by the current stress evidence. The next work is **T6.9 — release closure**, including the final whole-suite physical comparison, documentation consolidation, package/release-note audit, and merge/publish readiness review for `Icod.Grep 1.6.0`.
