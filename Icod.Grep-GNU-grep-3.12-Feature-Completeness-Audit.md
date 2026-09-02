# Icod.Grep GNU grep 3.12 Feature-Completeness Audit

**Audit baseline:** `main` at `996861022cf1d28e3a6977df6e900df98fd7ff98`  
**Target:** GNU grep 3.12 command behavior  
**Audit release:** `1.0.1`  
**Current parity release:** `1.2.0` — T3 color completeness closes G03; T2 closed G04, G05, and G06

## Executive summary

`Icod.Grep` has a broad and useful GNU grep-compatible implementation. The documented GNU grep 3.12 command-line option surface is essentially represented, and the current test suite exercises the major pattern modes, pattern sourcing, byte offsets, context output, binary policies, recursive traversal, include/exclude rules, output modes, diagnostics, cancellation, color highlighting, and exit-status behavior.

The repository is **not yet feature-complete against GNU grep 3.12**. The remaining work is concentrated in a relatively small number of compatibility areas rather than in ordinary line matching:

1. Perl-compatible regular expressions (`-P`) are recognized but unavailable.
2. GNU locale/environment selection is not wired into matching and pattern decoding.
3. ✅ GNU color behavior is complete for the GNU grep 3.12 `GREP_COLORS` / terminal-environment contract in `1.2.0`.
4. ✅ `POSIXLY_CORRECT` option-order behavior is implemented in `1.1.0`.
5. ✅ Default device handling under `-r` is aligned with GNU grep in `1.1.0`.
6. ✅ `-o` combined with context options follows GNU's warning/no-effect contract in `1.1.0`.
7. Binary classification does not yet account for locale encoding errors the way GNU grep does.
8. Cross-platform text-mode/CRLF behavior needs explicit GNU-parity tests.
9. Multi-character locale collating elements remain outside the shared managed regex engine.
10. `egrep` / `fgrep` compatibility entry points are not supplied; GNU itself treats these names as obsolescent.

Items 1–3 remain concrete implementation gaps. Items 4–6 are closed by the `1.1.0` T2 command-semantics tranche. Items 7–10 remain compatibility boundaries or parity risks that should be closed with targeted tests and, where necessary, implementation work.

## Baseline sources

The audit uses the GNU grep 3.12 manual as the behavioral reference:

- https://www.gnu.org/software/grep/manual/grep.html
- https://www.gnu.org/software/grep/manual/html_node/Environment-Variables.html
- https://www.gnu.org/software/grep/manual/html_node/Character-Encoding.html
- https://www.gnu.org/software/grep/manual/html_node/Context-Line-Control.html
- https://www.gnu.org/software/grep/manual/html_node/General-Output-Control.html

The managed BRE/ERE foundation is supplied by `Icod.CommandFramework.RegularExpressions`, whose own contract is explicitly pinned to GNU grep 3.12 and POSIX.1-2024.

## Implemented command surface

The current parser represents the GNU grep 3.12 option families below.

| Area | Implemented surface |
| --- | --- |
| Matcher selection | `-G`, `-E`, `-F`, `-P` (recognized), `-e`, `-f` |
| Match modifiers | `-i`, `-y`, `--no-ignore-case`, `-w`, `-x`, `-v` |
| Record mode | `-z`, `-U` |
| Output suppression / summaries | `-q`, `-c`, `-l`, `-L`, `-s` |
| Prefix/output metadata | `-H`, `-h`, `--label`, `-n`, `-b`, `-T`, `-Z` |
| Selected output | `-o`, `-m`, `--line-buffered`, `--color` / `--colour` |
| Binary policies | `-a`, `-I`, `--binary-files` |
| Directory/device selection | `-d`, `-D`, `-r`, `-R` |
| Path filtering | `--include`, `--exclude`, `--exclude-from`, `--exclude-dir` |
| Context | `-A`, `-B`, `-C`, legacy `-NUM`, `--group-separator`, `--no-group-separator` |
| Program information | `--help`, `-V` / `--version` |

The implementation also supports GNU long-option abbreviation through the shared option parser and GNU-style permutation of options and operands in its default mode.

## Existing test coverage

`tests/Grep.Tests/src/CommandTests.cs` currently covers the most important user-visible paths, including:

- BRE, ERE, and fixed-string matching;
- multiple `-e` / `-f` sources and empty-pattern semantics;
- UTF-8 BOM preservation in pattern files;
- case-insensitive, word, line, and inverted matching;
- `-o`, line numbers, and byte offsets;
- counts, max-count, quiet mode, and seekable-stdin repositioning;
- file-list modes;
- NUL-delimited records;
- binary/text/without-match policies;
- context groups, group separators, and legacy numeric context syntax;
- filename prefixes, labels, NUL delimiters, and initial-tab alignment;
- recursion, include/exclude ordering, and directory pruning;
- directory skipping and input diagnostics;
- conflicting matcher diagnostics and unavailable Perl mode;
- forced match coloring;
- help/version control paths;
- cancellation and output failures.

This is a strong functional base. The remaining work should add focused conformance tests rather than replace the current suite.

## Open compatibility gaps

### G01 — Perl-compatible regular expressions (`-P`)

**Priority:** High  
**Status:** Missing by design

GNU grep 3.12 defines `-P` / `--perl-regexp` as one of its four matcher variants. `Icod.Grep` parses `-P`, then deliberately returns a controlled diagnostic because the shared managed regex layer implements GNU BRE/ERE rather than PCRE.

**Closure criteria:**

- provide a PCRE-compatible provider with GNU grep-specific UTF-8 and character-class behavior; or
- explicitly narrow the project compatibility claim to exclude `-P`.

A silent translation to .NET regular expressions would not be sufficient for GNU compatibility.

### G02 — Locale and environment selection

**Priority:** High  
**Status:** Incomplete

GNU grep uses `LC_ALL`, category-specific `LC_*` variables, and `LANG` to determine character encoding, character classes, case folding, and collation. The current implementation selects `UnicodeRegularExpressionCharacterClassProvider.CurrentCulture` and the regex byte matcher defaults to UTF-8 decoding. Pattern files are also decoded as UTF-8 unconditionally.

This means the process environment does not currently provide GNU's `C`/`POSIX` byte-locale behavior or category-specific locale selection.

**Closure criteria:**

- resolve `LC_ALL` / `LC_CTYPE` / `LC_COLLATE` / `LANG` using GNU/POSIX precedence;
- select byte versus UTF-8 decoding from the effective locale;
- inject the corresponding character-class/collation provider;
- apply the same effective encoding policy to pattern files;
- add C-locale and UTF-8-locale conformance tests.

### G03 — GNU color model and terminal environment

**Priority:** Medium  
**Status:** Partial

The current implementation supports `--color=never|auto|always` aliases and highlights matched spans with a fixed bold-red sequence. GNU grep additionally uses `GREP_COLORS` (and the obsolescent `GREP_COLOR`) to color matched text, whole selected/context lines, filenames, line numbers, byte offsets, and separators. GNU `auto` behavior also considers `TERM`.

**Closure criteria:**

- parse the GNU `GREP_COLORS` capability set;
- honor the relevant `GREP_COLOR` fallback/warning behavior;
- color prefix fields, selected/context lines, and separators where configured;
- make `auto` terminal decisions include `TERM` capability policy;
- add tests for default and customized color capabilities.

### G04 — `POSIXLY_CORRECT`

**Priority:** Medium  
**Status:** Closed in `1.1.0`

The parser now selects POSIX required ordering when `POSIXLY_CORRECT` is present and GNU operand permutation otherwise. GNU grep switches to POSIX option ordering when `POSIXLY_CORRECT` is set, so options appearing after file operands are then treated as file operands rather than permuted options.

**Closure criteria:**

- inspect `POSIXLY_CORRECT` before parser construction;
- select POSIX ordering when present and GNU permutation otherwise;
- add paired tests proving the same argument vector is interpreted differently in the two modes.

### G05 — Default device policy during recursion

**Priority:** Medium  
**Status:** Closed in `1.1.0`

Recursive `-r` / `-d recurse` now skips discovered special files by default, while direct operands and `-R` retain read-by-default behavior. An explicit `-D read|skip` overrides the default policy.

**Closure criteria:**

- distinguish an explicit `-D read|skip` choice from the default policy;
- default recursive `-r` discovery to skip special entries;
- retain read behavior for explicit command-line operands and `-R` unless overridden;
- add FIFO/device-capability tests on platforms that can create them.

### G06 — `-o` with context options

**Priority:** Medium  
**Status:** Closed in `1.1.0`

When context is requested with `-o`, `Icod.Grep` now emits a warning, clears context state completely, and suppresses context-group separators.

**Closure criteria:**

- emit the GNU-style warning when context is requested with `-o`;
- disable context grouping and separators completely for that combination;
- update the current `-o -C 0` test expectation accordingly.

### G07 — Encoding-error binary classification

**Priority:** Medium  
**Status:** Needs implementation and parity tests

GNU grep treats NUL characters as ordinary matchable characters, but unless `-a` is used it can classify data as binary when NULs are present or when locale encoding errors would otherwise be emitted. `Icod.Grep` currently detects binary input primarily by probing for NUL bytes.

**Closure criteria:**

- couple binary classification to the effective locale/decoding policy from G02;
- add malformed-UTF-8 tests under both C and UTF-8 locales;
- verify text, binary, and `without-match` output/status behavior against GNU grep 3.12.

### G08 — Windows CRLF/text-mode parity

**Priority:** Medium-Low  
**Status:** Requires targeted conformance testing

The managed reader is byte-oriented and splits line records at LF. On Windows, CRLF input therefore needs explicit tests for anchors, `-x`, byte offsets, and `-U` semantics. This may be desirable byte-preserving behavior in some modes, but it should be deliberate and documented rather than incidental.

**Closure criteria:**

- add CRLF tests for normal and `-U` operation on Windows;
- compare expected behavior with the selected GNU-on-Windows compatibility target;
- document any intentional cross-platform divergence.

### G09 — Multi-character collating elements

**Priority:** Low  
**Status:** Shared regex limitation

The shared managed regular-expression engine supports single-scalar collating symbols and equivalence classes but explicitly reports multi-scalar collating elements as unsupported. This is a locale/collation edge of POSIX/GNU matching rather than a grep command-parser gap.

**Closure criteria:**

- either implement multi-character collation in `Icod.CommandFramework.RegularExpressions`; or
- document the limitation in the grep compatibility statement and add a controlled-diagnostic test.

### G10 — `egrep` and `fgrep` compatibility entry points

**Priority:** Low  
**Status:** Not supplied

GNU grep 3.12 still discusses the historical `egrep` and `fgrep` command names, but treats them as obsolescent and recommends `grep -E` and `grep -F`. The current NuGet tool intentionally installs only `grep`.

**Closure criteria:**

No change is required for core `grep` completeness. If command-name compatibility is desired, add lightweight wrappers that issue the same obsolescence behavior as the chosen GNU baseline.

## Recommended closure order

### T1 — Locale and byte contract

Address G02 and G07 together. They share the same locale-selection and decoding boundary and should not be implemented independently.

### T2 — Command semantics — complete in `1.1.0`

G04, G05, and G06 are closed with targeted regression tests.

### T3 — Color completeness — complete in `1.2.0`

G03 is closed. `Icod.DCurses 0.1.0` supplies the canonical terminal stack; grep uses terminal endpoint observation for `--color=auto` while retaining GNU SGR strings as the command-owned `GREP_COLORS` policy.

### T4 — PCRE decision

Resolve G01 explicitly. Full `-P` support is the largest remaining feature decision and may justify a separate package/provider boundary.

### T5 — Edge compatibility

Close G08 and G09 with targeted tests and implementation only where the selected compatibility target requires it. Treat G10 as optional historical command-name compatibility.

## Release assessment

Version `1.0.1` was the maintenance release that established this audit. Version `1.1.0` is the first parity release and closes G04, G05, and G06. The original `1.0.1` assessment was: the `Icod.CommandFramework` dependency has moved to `2.1.0`, the NuGet package now has an icon, CI/CD has been standardized, and this audit establishes the remaining GNU grep 3.12 closure work. The audit does **not** claim that the gaps above are fixed in `1.0.1`.
