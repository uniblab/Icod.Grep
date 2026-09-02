# Icod.Grep 1.5.0 — T5 Edge Compatibility Roadmap

**Baseline:** `main` / `v1.4.0` at `fbb94c4ae160bfaf23351ac66fc4d60615677ea8`  
**Target release:** `1.5.0`  
**Scope:** G08 Windows CRLF/text-mode parity and G09 multi-character locale collating elements  
**GNU reference:** GNU grep 3.12  
**Status:** Implementation complete — final canonical PR validation pending

## Objective

`1.4.0` closed G01–G07 within their documented scopes. T5 closes the remaining core GNU grep 3.12 compatibility edges without broadening the release into optional historical command-name compatibility (`egrep` / `fgrep`).

## T5.1 — G08 Windows text/binary I/O contract — implemented

GNU grep 3.12 distinguishes text and binary I/O on Windows. In default text I/O, CRLF input is presented to matching as LF, Control-Z acts as end-of-file, output LF bytes are emitted as CRLF, and `-b` counts offsets in the translated logical stream. `-U` / `--binary` instead preserves input and output bytes. On Linux and macOS `-U` has no effect.

The matcher remains byte-oriented. Windows compatibility is implemented at the platform I/O boundary:

1. `PlatformIoContext` establishes default Windows text mode for process execution.
2. Standard-input/output adapters defer their concrete text/binary behavior until the canonical `OptionParser` has parsed the command line.
3. After a successful parse, `Command` publishes the actual `binary-platform` / `-U` selection to `PlatformIoContext`; there is no independent command-line pre-parser.
4. `WindowsTextInputStream` collapses CRLF to LF and honors Control-Z EOF, including CR boundaries split across read buffers.
5. `WindowsTextOutputStream` expands LF to CRLF for process standard output.
6. The grep-local file-stream adapter applies the same input policy to operands and pattern files opened after parsing.
7. Linux/macOS retain the existing raw-byte path.
8. The real installed Windows tool is exercised in CI against both default and `-U` behavior.

Keeping option ownership in the canonical parser preserves GNU option ordering, required option values, long-option abbreviations, and `POSIXLY_CORRECT` semantics without duplicating them in platform I/O code.

### G08 conformance coverage

Implemented tests cover:

- BRE `^` / `$` and `-x` against CRLF input;
- fixed-string and PCRE whole-record/anchor behavior;
- translated `-b -o` offsets;
- mixed LF/CRLF files;
- before/after context over translated records;
- `-z` records containing CRLF;
- Control-Z EOF in default Windows text mode;
- LF-to-CRLF process-output translation;
- `-U` raw CRLF matching and raw offsets;
- `-U` preservation of Control-Z as data; and
- neutral `-U` behavior for ordinary LF data.

The installed-tool Windows package smoke writes a physical CRLF file and requires default `-x alpha` to return success while `-U -x alpha` returns the conventional no-match status 1. The smoke also verifies that `grep --version` exactly matches the package version.

## T5.2 — G09 multi-character locale collating elements — closed by documented boundary

The limitation is in `Icod.CommandFramework.RegularExpressions`, not in grep's command layer. The shared generic provider exposes scalar collation, while .NET does not expose a culture's complete multi-scalar collating-element/contraction inventory. Assigning guessed semantics to `[[.ch.]]` or similar forms would therefore create false compatibility.

T5 selects the alternative closure criterion already defined by the feature audit: document the limitation and prove controlled diagnostics rather than fabricate locale data.

The `1.5.0` contract is:

- single-scalar collating symbols such as `[[.a.]]` remain supported;
- single-scalar equivalence classes such as `[[=a=]]` remain supported;
- unresolved multi-scalar collating symbols/equivalence elements return the stable `UnsupportedCollatingElement` diagnostic and status 2;
- BRE and ERE share that controlled boundary; and
- a future locale-specific provider may add explicit logical collating-element inventories without a grep-local pattern rewrite.

This scope is accurate for the documented C/POSIX byte profile and generic supported UTF-8 profiles: neither claims a hidden locale-contraction inventory that the runtime cannot expose.

## T5.3 — Release closure state

Completed on the branch:

- `Version`, `PackageVersion`, `AssemblyVersion`, command API `--version`, and installed-process `--version` are synchronized to `1.5.0`;
- package release notes describe the final G08/G09 contract;
- README documents Windows default text mode, `-U`, translated offsets/output, and the G09 provider boundary;
- the GNU grep 3.12 feature-completeness audit advances G08/G09 to T5 closure status;
- Windows package smoke tests the actual installed default-vs-`-U` behavior;
- package smoke asserts reported version equals the exact `.nupkg` version; and
- the existing six-RID PCRE/native archive gate remains unchanged.

The remaining gate before PR #11 leaves draft is a fully green canonical PR workflow on the final head. G10 remains optional historical compatibility and is not part of core T5 closure.
