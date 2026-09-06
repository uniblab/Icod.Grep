# Icod.Grep 1.6.0 — T6.9 Release Closure Plan

**Target release:** `1.6.0`  
**Baseline:** `1.5.0` at `423c0e9623100492fa01b6e4d14c183761d111d7`  
**Status:** active

## Objective

T6.9 is the final release-closure tranche for the `1.6.0` performance and scalability release. It does not introduce new optimization work unless final validation exposes a concrete regression or correctness defect.

The governing rule remains:

> Measure first. Optimize second. Preserve behavior always.

## Closure gates

T6.9 closes when all of the following are complete:

1. final whole-suite physical comparison against the immutable `1.5.0` baseline;
2. review of residual timing and allocation deltas for material regressions;
3. documentation consolidation so the benchmark/status documents no longer describe completed T6 work as pending;
4. package release-note update describing the shipped `1.6.0` changes rather than the initial benchmark-foundation state;
5. package/distribution workflow audit for the exact `1.6.0` version and current dependencies;
6. final PR CI green on the release-ready head; and
7. explicit merge/publish readiness decision.

## Final physical comparison

The authoritative collector is `benchmarks/Collect-ReferenceComparison.ps1`.

The final run uses:

- baseline commit `423c0e9623100492fa01b6e4d14c183761d111d7`;
- the current release-ready candidate commit;
- the complete benchmark filter (`*`);
- two ABBA passes;
- 30-second cooldowns; and
- the established physical reference-host inventory hash.

The collector overlays the current benchmark harness onto the baseline worktree, so both variants are measured through identical benchmark code.

Timing deltas are interpreted conservatively because earlier physical work established nontrivial run-order/noise effects. Allocation deltas are expected to be substantially more stable. A narrow timing loss is not a release blocker unless it is repeated, material, and unsupported by a compensating resource or semantic improvement.

## Release-readiness policy

T6.9 should not reopen completed tranches merely to pursue additional percentage gains. The release is ready when:

- no material whole-suite regression remains unexplained;
- all known stress/resource pathologies are either fixed or explicitly documented;
- package metadata accurately describes the release;
- the exact packaged tool and RID archives continue to pass the repository's existing validation workflow; and
- the final branch is green.

Any new issue found during T6.9 must be classified as one of:

- **release blocker** — correctness, packaging, severe regression, or uncontrolled failure;
- **documented residual** — real but acceptable limitation with no release-blocking effect; or
- **future optimization** — worthwhile work that belongs after `1.6.0`.
