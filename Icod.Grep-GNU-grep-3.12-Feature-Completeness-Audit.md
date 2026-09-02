# Icod.Grep GNU grep 3.12 Feature-Completeness Audit

**Audit baseline:** `main` at `996861022cf1d28e3a6977df6e900df98fd7ff98`  
**Target:** GNU grep 3.12 command behavior  
**Audit release:** `1.0.1`  
**Current parity target:** `1.5.0` — T5 closes G08 with explicit Windows text/binary I/O semantics and closes G09 as a documented shared-provider limitation; G01–G07 were closed in `1.1.0`–`1.4.0`

## Executive summary

`Icod.Grep` implements the GNU grep 3.12 command surface across GNU BRE, GNU ERE, fixed strings, PCRE2-backed `-P`, locale/environment selection, color policy, recursion/device behavior, binary-file handling, context/output modes, and the documented Windows platform text/binary distinction.

The compatibility ledger is now:

1. ✅ G01 — Perl-compatible regular expressions (`-P`): closed in `1.4.0` with PCRE.NET 1.6.0 / PCRE2 10.48.
2. ✅ G02 — locale/environment selection: closed in `1.3.0` for the documented C/POSIX and UTF-8 profiles.
3. ✅ G03 — GNU color model and terminal environment: closed in `1.2.0`.
4. ✅ G04 — `POSIXLY_CORRECT` option ordering: closed in `1.1.0`.
5. ✅ G05 — recursive device defaults: closed in `1.1.0`.
6. ✅ G06 — `-o` with context options: closed in `1.1.0`.
7. ✅ G07 — malformed UTF-8 output/binary classification: closed in `1.3.0`.
8. ✅ G08 — Windows CRLF/text-mode parity: implemented for `1.5.0`; final package validation is the release gate.
9. ✅ G09 — multi-character locale collating elements: closed for the documented compatibility scope by explicit controlled-diagnostic behavior rather than invented locale data.
10. G10 — `egrep` / `fgrep` entry points remain optional historical compatibility; GNU itself treats these names as obsolescent.

Within the documented locale and platform scope, G01–G09 are therefore closed. G10 is not a core `grep` completeness blocker.

## Baseline sources

The behavioral reference is GNU grep 3.12 and its manual, especially the sections covering matcher selection, environment variables, character encoding, context/output control, and platform binary/text I/O:

- https://www.gnu.org/software/grep/manual/grep.html
- https://www.gnu.org/software/grep/manual/html_node/Environment-Variables.html
- https://www.gnu.org/software/grep/manual/html_node/Character-Encoding.html
- https://www.gnu.org/software/grep/manual/html_node/Context-Line-Control.html
- https://www.gnu.org/software/grep/manual/html_node/General-Output-Control.html

The BRE/ERE foundation is supplied by `Icod.CommandFramework.RegularExpressions`, whose contract is pinned to GNU grep 3.12 and POSIX.1-2024 where the runtime/provider can represent the required locale information.

## Implemented command surface

| Area | Implemented surface |
| --- | --- |
| Matcher selection | `-G`, `-E`, `-F`, `-P`, `-e`, `-f` |
| Match modifiers | `-i`, `-y`, `--no-ignore-case`, `-w`, `-x`, `-v` |
| Record/platform mode | `-z`, `-U` / `--binary` |
| Output suppression / summaries | `-q`, `-c`, `-l`, `-L`, `-s` |
| Prefix/output metadata | `-H`, `-h`, `--label`, `-n`, `-b`, `-T`, `-Z` |
| Selected output | `-o`, `-m`, `--line-buffered`, `--color` / `--colour` |
| Binary-content policies | `-a`, `-I`, `--binary-files` |
| Directory/device selection | `-d`, `-D`, `-r`, `-R` |
| Path filtering | `--include`, `--exclude`, `--exclude-from`, `--exclude-dir` |
| Context | `-A`, `-B`, `-C`, legacy `-NUM`, `--group-separator`, `--no-group-separator` |
| Program information | `--help`, `-V` / `--version` |

GNU long-option abbreviation and GNU-style option/operand permutation are supplied by the shared option parser; `POSIXLY_CORRECT` selects required ordering.

## Test coverage

The command suite plus focused `PcreTests`, locale/color tests, and the T5 `EdgeCompatibilityTests` / `CollationCompatibilityTests` cover:

- BRE, ERE, fixed-string, and PCRE matching;
- multiple pattern sources and pattern files;
- case, word, line, invert, `-o`, count, max-count, quiet, file-list, and prefix modes;
- recursive traversal, include/exclude ordering, device defaults, links, and diagnostics;
- NUL-delimited records and binary-content policy;
- GNU locale selection, C/POSIX arbitrary-byte behavior, UTF-8 classes, and malformed UTF-8 policy;
- GNU `GREP_COLORS`, `GREP_COLOR`, `TERM`, terminal observation, prefixes, context, and separators;
- PCRE lookaround/backreferences/Unicode properties, GNU ASCII `\d`, `[[:digit:]]`, malformed UTF-8, `-z`, `-i`, `-o`, and color;
- Windows default CRLF translation for anchors and `-x`;
- translated Windows `-b` / `-o` offsets;
- mixed LF/CRLF input, context records, fixed strings, PCRE, and `-z` with embedded CRLF;
- Windows Control-Z text EOF and LF-to-CRLF output translation;
- raw `-U` CRLF offsets/matching and Control-Z preservation;
- supported single-scalar collating symbols/equivalence classes; and
- stable BRE/ERE diagnostics for unsupported multi-scalar collating elements.

The PR package gate additionally installs the exact `.nupkg` on Windows, Linux, and macOS; Windows smoke differentiates default CRLF text behavior from `-U` raw-byte behavior. Six RID-specific standalone archive smokes remain mandatory because `-P` carries native PCRE2 payloads.

## Compatibility gaps and closure status

### G01 — Perl-compatible regular expressions (`-P`)

**Priority:** High  
**Status:** Closed in `1.4.0`

`Icod.Grep 1.4.0` uses PCRE.NET 1.6.0 / PCRE2 10.48. C/POSIX uses 8-bit byte semantics; supported UTF-8 profiles enable UTF/UCP and malformed-UTF matching while retaining GNU grep's ASCII-only `\d` behavior. Grep retains ownership of `-w`, `-x`, output selection, coloring, offsets, and binary/encoding policy.

Native packaging, third-party notices, installed-package PCRE smoke, and six-RID archive smoke are part of the permanent artifact contract.

### G02 — Locale and environment selection

**Priority:** High  
**Status:** Closed in `1.3.0` for the documented C/POSIX and UTF-8 profiles

`LC_ALL`, `LC_CTYPE`, `LC_COLLATE`, and `LANG` use GNU/POSIX precedence. `LC_CTYPE` selects byte versus UTF-8 decoding/classification while `LC_COLLATE` supplies ordering/equivalence behavior. C-locale pattern files preserve every source byte; supported UTF-8 profiles use strict UTF-8 pattern decoding.

### G03 — GNU color model and terminal environment

**Priority:** Medium  
**Status:** Closed in `1.2.0`

GNU `GREP_COLORS`, the obsolescent `GREP_COLOR` fallback/warning behavior, `rv`, `ne`, prefix/context/separator styling, `TERM=dumb`, and terminal attachment are implemented without opening a curses session.

### G04 — `POSIXLY_CORRECT`

**Priority:** Medium  
**Status:** Closed in `1.1.0`

The shared parser uses POSIX required ordering when `POSIXLY_CORRECT` is present and GNU operand permutation otherwise.

### G05 — Default device policy during recursion

**Priority:** Medium  
**Status:** Closed in `1.1.0`

Recursive `-r` discovery skips special entries by default, while explicit operands and `-R` retain GNU read behavior unless `-D read|skip` overrides it.

### G06 — `-o` with context options

**Priority:** Medium  
**Status:** Closed in `1.1.0`

The command emits the GNU-style warning and disables context/group-separator behavior when context options are combined with `-o`.

### G07 — Encoding-error binary classification

**Priority:** Medium  
**Status:** Closed in `1.3.0`

NUL remains the file-level binary heuristic. Under UTF-8, malformed records still participate in matching/status/counts but unsafe detailed output is suppressed unless `-a` selects text mode.

### G08 — Windows CRLF/text-mode parity

**Priority:** Medium-Low  
**Status:** Implemented for `1.5.0`; closure gated by final canonical PR validation

GNU grep distinguishes Windows text I/O from `-U` binary I/O. `Icod.Grep 1.5.0` implements that distinction at the stream boundary:

- default Windows input collapses CRLF to LF before matching;
- Control-Z terminates Windows text input;
- default Windows output expands LF to CRLF;
- `-b` observes the translated logical input stream;
- `-U` / `--binary` bypasses platform translation and preserves raw bytes; and
- Linux/macOS retain byte-preserving platform I/O, so `-U` is behaviorally neutral there.

The translation layer is shared by standard process I/O and internally opened file/pattern operands, leaving BRE, ERE, fixed strings, PCRE, record selection, and binary-content policy platform-neutral.

**Closure evidence:** unit tests cover anchors, `-x`, fixed strings, PCRE, `-o`, `-b`, context, mixed newlines, `-z`, Control-Z, output translation, and raw `-U`; the installed Windows package smoke proves default `-x alpha` matches a CRLF file while `-U -x alpha` does not.

### G09 — Multi-character locale collating elements

**Priority:** Low  
**Status:** Closed as a documented shared-provider limitation in `1.5.0`

`Icod.CommandFramework.RegularExpressions` supports single-scalar collating symbols and equivalence classes. Its generic .NET provider cannot discover a locale's complete multi-scalar collating-element/contraction inventory because the BCL does not expose that inventory. Assigning guessed semantics would be less compatible than failing explicitly.

For the documented Icod.Grep locale scope:

- C/POSIX has no project-defined multi-scalar collating elements;
- generic supported UTF-8 profiles use .NET scalar classification/collation without claiming hidden contraction inventories;
- single-scalar `[[.x.]]`, `[[=x=]]`, ranges, and existing equivalence behavior remain supported; and
- unresolved multi-scalar forms such as `[[.ch.]]` and `[[=ch=]]` return the stable `UnsupportedCollatingElement` diagnostic/status 2 in BRE and ERE.

This selects the audit's explicit alternative closure criterion: document the limitation and prove controlled diagnostics. A future locale-specific provider may add explicit logical collating-element inventories without requiring a grep-local pattern rewrite.

### G10 — `egrep` and `fgrep` compatibility entry points

**Priority:** Low / optional  
**Status:** Not supplied by design

GNU grep 3.12 treats these names as obsolescent and recommends `grep -E` / `grep -F`. No change is required for core `grep` completeness. Lightweight wrappers remain an optional historical-compatibility feature.

## Closure sequence

- **T1 / `1.3.0`** — G02 locale/environment selection and G07 encoding-error behavior.
- **T2 / `1.1.0`** — G04 option ordering, G05 recursive device defaults, G06 `-o` plus context.
- **T3 / `1.2.0`** — G03 GNU color/terminal model.
- **T4 / `1.4.0`** — G01 PCRE2-backed `-P`, native packaging, and redistribution contract.
- **T5 / `1.5.0`** — G08 Windows platform text/binary I/O and G09 explicit multi-scalar collation boundary.

## Release assessment

`1.5.0` is the intended GNU grep 3.12 parity-closure release for the project's documented C/POSIX + supported UTF-8 locale profiles and Windows/Linux/macOS platform scope. Once the canonical T5 PR gate is fully green, G01–G09 are closed within those documented bounds. G10 remains an optional historical command-name feature rather than a core `grep` gap.

Future work can therefore focus on performance, hardening, additional locale-specific providers, or optional historical entry points without representing unfinished core GNU grep 3.12 command semantics as ordinary feature debt.
