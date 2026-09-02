# Icod.Grep 1.5.0 — T5 Edge Compatibility Roadmap

**Baseline:** `main` / `v1.4.0` at `fbb94c4ae160bfaf23351ac66fc4d60615677ea8`  
**Target release:** `1.5.0`  
**Scope:** G08 Windows CRLF/text-mode parity and G09 multi-character locale collating elements  
**GNU reference:** GNU grep 3.12

## Objective

`1.4.0` closed G01–G07 within their documented scopes. T5 closes the remaining core GNU grep 3.12 compatibility edges without broadening the release into optional historical command-name compatibility (`egrep` / `fgrep`).

## T5.1 — G08 Windows text/binary I/O contract

GNU grep 3.12 distinguishes text and binary I/O on Windows. In default text I/O, CRLF input is presented to matching as LF, Control-Z may act as end-of-file, output newlines are written as CRLF, and `-b` counts bytes after text-I/O processing. `-U` / `--binary` instead preserves input and output bytes as-is. On POSIX-compatible platforms `-U` has no effect.

The current Icod.Grep implementation is byte-preserving on every platform and treats `-U` as a no-op. Therefore the existing behavior already provides the binary-I/O side of the GNU Windows contract, but Windows default text mode requires an explicit compatibility layer.

### G08 closure work

1. Preserve the current raw-byte path for `-U` on Windows and for all modes on POSIX hosts.
2. Add an explicit option-state bit so `-U` is no longer discarded during parsing.
3. On Windows default line mode, normalize CRLF input before matching and output selection while preserving GNU byte-offset semantics for the processed text stream.
4. Account for Windows text-I/O Control-Z behavior where applicable.
5. Ensure normal selected-record output follows Windows text-mode newline behavior while `-U` remains byte-preserving.
6. Cover BRE, ERE, fixed, and PCRE matchers where text normalization can affect anchors or whole-line matching.
7. Verify `-x`, `^` / `$`, `-o`, `-b`, context output, mixed LF/CRLF input, unterminated records, and `-z` interaction.
8. Verify `-U` remains a no-op on Linux and macOS.

### Initial coverage landed

- `-U` preserves CRLF record bytes for anchored matching.
- `-U -b -o` reports offsets against the raw CRLF byte stream.

These tests establish the already-correct binary path before the missing Windows default-text path is introduced.

## T5.2 — G09 multi-character locale collating elements

The limitation is in `Icod.CommandFramework.RegularExpressions`, not in grep's command layer. The current shared contract exposes character-class/collation operations in terms of single `Rune` values, and bracket-expression matching consumes one scalar at a time. Multi-scalar collating elements such as `[[.ch.]]` therefore produce the stable `UnsupportedCollatingElement` diagnostic rather than silently changing semantics.

### Architectural consequence

G09 must not be implemented as a grep-local parser or pattern rewrite. Proper support requires extending the shared regular-expression abstraction so a bracket term can consume one logical collating element spanning one or more input scalars.

### G09 closure work in Icod.CommandFramework

1. Define a collating-element value/contract that can represent one or more Unicode scalars while preserving the existing single-scalar fast path.
2. Extend the character-class/collation provider contract to resolve named collating elements and compare logical elements under the active locale.
3. Extend bracket-expression syntax nodes so a positive collating-symbol term can consume the matched element length rather than always one scalar.
4. Define equivalence-class behavior for multi-scalar elements without regressing existing single-scalar equivalence classes.
5. Define range-endpoint rules: POSIX ranges require each endpoint to denote exactly one collating element even when that element spans multiple scalars.
6. Preserve opaque-byte and C/POSIX behavior.
7. Add shared BRE/ERE tests for supported multi-character elements and stable diagnostics when the active provider cannot resolve one.
8. Publish the resulting Icod.CommandFramework package and update Icod.Grep to consume it.

### Initial grep coverage landed

A regression test records the current controlled-diagnostic boundary for `[[.ch.]]`. It will be converted to positive conformance coverage once the shared engine supports the selected locale element.

## T5.3 — Differential conformance

After G08 and G09 implementation, run focused GNU grep 3.12 differential cases for:

- CRLF and mixed-newline files on Windows;
- normal mode versus `-U`;
- BRE / ERE anchors and whole-line matching;
- fixed strings and PCRE around CR boundaries;
- byte offsets and `-o`;
- context output;
- collating symbols, equivalence classes, and ranges under the selected locale profiles.

## T5.4 — 1.5.0 closure

Before release:

- set `Version`, `PackageVersion`, `AssemblyVersion`, and `grep --version` consistently to `1.5.0`;
- update package release notes and README platform notes;
- mark G08/G09 closed only after their conformance tests are green;
- update the GNU grep 3.12 feature-completeness audit;
- retain G10 as optional historical compatibility rather than a core release blocker;
- pass the canonical Windows/Linux/macOS package smoke and six-RID archive smoke gates.
