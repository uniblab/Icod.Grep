# Icod.Grep 1.5.0 — T5 Edge Compatibility Roadmap

**Baseline:** `main` / `v1.4.0` at `fbb94c4ae160bfaf23351ac66fc4d60615677ea8`  
**Target release:** `1.5.0`  
**Scope:** G08 Windows CRLF/text-mode parity and G09 multi-character locale collating elements  
**GNU reference:** GNU grep 3.12

## Objective

`1.4.0` closed G01–G07 within their documented scopes. T5 closes the remaining core GNU grep 3.12 compatibility edges without broadening the release into optional historical command-name compatibility (`egrep` / `fgrep`).

## T5.1 — G08 Windows text/binary I/O contract

GNU grep 3.12 distinguishes text and binary I/O on Windows. In default text I/O, CRLF input is presented to matching as LF, Control-Z acts as end-of-file, output LF bytes are emitted as CRLF, and `-b` counts offsets in the translated text stream. `-U` / `--binary` instead preserves input and output bytes. On POSIX-compatible platforms `-U` has no effect.

### Implementation shape

The matcher remains byte-oriented. Windows compatibility is implemented at the platform I/O boundary instead of teaching every matcher about CRLF:

1. `PlatformIoContext` selects Windows text mode for normal process execution and bypasses it for `-U` / `--binary`.
2. `WindowsTextInputStream` collapses CRLF to LF and honors Control-Z EOF.
3. `WindowsTextOutputStream` expands LF to CRLF for process standard output.
4. The grep-local `FileStream` adapter applies the same text-input policy to internally opened operands and pattern files.
5. Linux and macOS retain the existing byte-preserving path in all modes.
6. Injected byte streams remain suitable for deterministic unit testing; explicit test scopes exercise the Windows translation layer independent of the host OS.

This keeps the GNU platform distinction at the point where the platform actually changes bytes and avoids contaminating BRE, ERE, fixed-string, PCRE, binary detection, or record-selection logic with Windows-only branches.

### G08 coverage

The T5 test suite now covers:

- default Windows text-mode CRLF normalization for `^` / `$`;
- default Windows text-mode `-x` behavior;
- translated `-b -o` offsets;
- mixed LF/CRLF input;
- Control-Z end-of-file behavior;
- LF-to-CRLF text output translation;
- `-U` raw CRLF matching; and
- `-U -b -o` raw-stream offsets.

Remaining G08 closure coverage before the PR leaves draft:

- context output across translated CRLF records;
- fixed-string and PCRE anchor/whole-record cases;
- `-z` interaction, where the record separator is NUL but Windows text translation still acts on CRLF bytes inside records;
- an installed-tool Windows smoke that demonstrates default mode versus `-U`; and
- confirmation that Linux/macOS behavior is unchanged.

## T5.2 — G09 multi-character locale collating elements

The limitation is in `Icod.CommandFramework.RegularExpressions`, not in grep's command layer. The current shared provider contract exposes collation in terms of single `Rune` values, and the BCL does not expose a culture's complete collating-element inventory. Multi-scalar expressions such as `[[.ch.]]` therefore produce the stable `UnsupportedCollatingElement` diagnostic rather than silently inventing locale semantics.

### Closure decision

T5 will **not** fabricate generic multi-character locale inventories or rewrite grep patterns locally.

The feature-completeness audit already permits G09 closure by documenting the shared-engine limitation and proving a controlled diagnostic. That is the selected `1.5.0` contract because it is more accurate than claiming GNU locale behavior that the runtime cannot discover generically.

The supported C/POSIX profile has no project-defined multi-character collating elements. The supported generic UTF-8 profile delegates scalar classification/collation to .NET but does not claim knowledge of locale-specific contraction inventories. A future locale-specific provider may extend `Icod.CommandFramework.RegularExpressions` with explicit logical collating elements, but that extension is not required for the documented `Icod.Grep` compatibility scope.

### G09 closure work

1. Retain the stable `UnsupportedCollatingElement` diagnostic for unresolved multi-scalar collating symbols/equivalence elements.
2. Keep the grep regression proving the failure is controlled and returns status 2 rather than silently changing meaning.
3. Document the limitation in README platform/compatibility notes and in the GNU grep 3.12 feature-completeness audit.
4. Keep single-scalar collating symbols, equivalence classes, ranges, and existing locale behavior unchanged.
5. Record multi-scalar provider support as a future `Icod.CommandFramework` extension point rather than a hidden grep-local compatibility shim.

## T5.3 — Differential conformance

Before closure, run focused GNU grep 3.12 differential cases for:

- CRLF and mixed-newline files on Windows;
- normal mode versus `-U`;
- BRE / ERE anchors and whole-line matching;
- fixed strings and PCRE around CR boundaries;
- byte offsets and `-o`;
- context output;
- Control-Z behavior;
- `-z` with embedded CRLF; and
- the documented controlled diagnostic for unsupported multi-scalar collating elements.

## T5.4 — 1.5.0 closure

Before release:

- set `Version`, `PackageVersion`, `AssemblyVersion`, and `grep --version` consistently to `1.5.0`;
- update package release notes and README platform notes;
- mark G08 closed after its conformance tests and Windows installed-tool smoke are green;
- mark G09 closed as a documented shared-provider limitation with controlled-diagnostic regression coverage;
- update the GNU grep 3.12 feature-completeness audit;
- retain G10 as optional historical compatibility rather than a core release blocker; and
- pass the canonical Windows/Linux/macOS package smoke and six-RID archive smoke gates.
