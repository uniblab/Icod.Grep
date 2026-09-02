# Icod.Grep GNU grep 3.12 Feature-Completeness Audit

**Audit baseline:** `main` at `996861022cf1d28e3a6977df6e900df98fd7ff98`  
**Target:** GNU grep 3.12 command behavior  
**Audit release:** `1.0.1`  
**Current parity release:** `1.4.0` — T4 closes G01 with PCRE.NET 1.6.0 / PCRE2 10.48; T1 closed G02/G07, T3 closed G03, and T2 closed G04–G06

## Executive summary

`Icod.Grep` has a broad and useful GNU grep-compatible implementation. The documented GNU grep 3.12 command-line option surface is essentially represented, and the current test suite exercises the major pattern modes, pattern sourcing, byte offsets, context output, binary policies, recursive traversal, include/exclude rules, output modes, diagnostics, cancellation, color highlighting, and exit-status behavior.

The repository is **not yet feature-complete against GNU grep 3.12**. The remaining work is concentrated in a relatively small number of compatibility areas rather than in ordinary line matching:

1. ✅ Perl-compatible regular expressions (`-P`) are implemented with PCRE2 10.48 via PCRE.NET 1.6.0 in `1.4.0`.
2. ✅ GNU locale/environment selection for the supported C/POSIX and UTF-8 profiles is implemented in `1.3.0`.
3. ✅ GNU color behavior is complete for the GNU grep 3.12 `GREP_COLORS` / terminal-environment contract in `1.2.0`.
4. ✅ `POSIXLY_CORRECT` option-order behavior is implemented in `1.1.0`.
5. ✅ Default device handling under `-r` is aligned with GNU grep in `1.1.0`.
6. ✅ `-o` combined with context options follows GNU's warning/no-effect contract in `1.1.0`.
7. ✅ Malformed UTF-8 output suppression now follows GNU grep's record-level encoding-error behavior in `1.3.0`.
8. Cross-platform text-mode/CRLF behavior needs explicit GNU-parity tests.
9. Multi-character locale collating elements remain outside the shared managed regex engine.
10. `egrep` / `fgrep` compatibility entry points are not supplied; GNU itself treats these names as obsolescent.

G01–G07 are closed within their documented scopes. G08 and G09 remain parity/engine-edge work, while G10 is optional historical command-name compatibility.

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
| Matcher selection | `-G`, `-E`, `-F`, `-P`, `-e`, `-f` |
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

`tests/Grep.Tests/src/CommandTests.cs` covers the broad user-visible command surface, while `tests/Grep.Tests/src/PcreTests.cs` supplies focused GNU `-P` / PCRE2 regression coverage. Together the current suite includes:

- BRE, ERE, fixed-string, and PCRE matching;
- multiple `-e` / `-f` sources and empty-pattern semantics;
- UTF-8 BOM preservation in pattern files;
- case-insensitive, word, line, and inverted matching;
- PCRE lookbehind, backreferences, Unicode properties, and invalid-pattern diagnostics;
- GNU ASCII-only PCRE `\d`, Unicode `[[:digit:]]`, and PCRE2 `(?-aD)` pattern override behavior;
- C/POSIX arbitrary-byte PCRE matching and UTF-8/UCP behavior;
- `-P -z` matching across embedded newlines and embedded-NUL pattern data from `-f`;
- malformed UTF-8 PCRE selection/output suppression and exact `-a` output;
- `-o`, line numbers, and byte offsets;
- counts, max-count, quiet mode, and seekable-stdin repositioning;
- file-list modes;
- NUL-delimited records;
- binary/text/without-match policies;
- context groups, group separators, and legacy numeric context syntax;
- filename prefixes, labels, NUL delimiters, and initial-tab alignment;
- recursion, include/exclude ordering, and directory pruning;
- directory skipping and input diagnostics;
- forced match coloring and PCRE interaction with `-i`, `-o`, and color;
- help/version control paths;
- cancellation and output failures.

This is a strong functional base. The remaining work should add focused conformance tests rather than replace the current suite.

## Compatibility gaps and closure status

### G01 — Perl-compatible regular expressions (`-P`)

**Priority:** High  
**Status:** Closed in `1.4.0`

`Icod.Grep 1.4.0` uses PCRE.NET 1.6.0, which embeds PCRE2 10.48 and supplies native libraries for the repository's Windows/Linux/macOS x64/ARM64 release matrix. C/POSIX locale mode uses PCRE2's 8-bit byte semantics. UTF-8 locale mode enables UTF, UCP, malformed-UTF support, and PCRE2's ASCII-`\d` extra option so GNU grep's `\d` versus `[[:digit:]]` distinction is retained. Grep continues to own `-w`, `-x`, output selection, byte offsets, coloring, and binary/encoding-output policy around the PCRE provider.

The NuGet package and standalone RID archives carry `THIRD-PARTY-NOTICES.md` alongside the redistributed PCRE.NET / PCRE2 runtime. Exact package verification requires the managed PCRE assembly, all six native assets, and the notice; archive construction likewise requires the notice and executes a real PCRE smoke on matching hosts.

**Closure criteria:**

- use an actual PCRE2 provider rather than translating patterns to .NET regular expressions;
- retain C/POSIX byte semantics and UTF-8/UCP behavior;
- preserve GNU grep's ASCII-only `\d` behavior under UTF-8/UCP;
- validate lookarounds, backreferences, Unicode properties, invalid-pattern diagnostics, and existing output modifiers;
- validate the native PCRE2 payload across release RIDs.

### G02 — Locale and environment selection

**Priority:** High  
**Status:** Closed for the documented C/POSIX and UTF-8 scope in `1.3.0`

`Icod.Grep 1.3.0` resolves `LC_ALL`, `LC_CTYPE`, `LC_COLLATE`, and `LANG` with category-specific precedence. `LC_CTYPE` selects C/POSIX byte semantics versus the supported UTF-8 Unicode profile and supplies classification/case behavior; `LC_COLLATE` independently supplies range/equivalence ordering. The implementation composes the existing `Icod.CommandFramework` C-locale and Unicode regular-expression providers rather than duplicating their class/collation rules.

Pattern files follow the same effective character-encoding contract: C/POSIX uses a one-byte Latin-1 transport to preserve every source byte, while UTF-8 profiles use strict UTF-8 decoding. Command-line pattern strings are re-exposed as their UTF-8 argument bytes when the effective C/POSIX profile is byte-oriented.

**Closure criteria:**

- resolve `LC_ALL` / `LC_CTYPE` / `LC_COLLATE` / `LANG` using GNU/POSIX precedence;
- select byte versus UTF-8 decoding from the effective locale;
- inject the corresponding character-class/collation provider;
- apply the same effective encoding policy to pattern files;
- add C-locale and UTF-8-locale conformance tests.

### G03 — GNU color model and terminal environment

**Priority:** Medium  
**Status:** Closed in `1.2.0`

`Icod.Grep 1.2.0` implements GNU `GREP_COLORS` capabilities, the obsolescent `GREP_COLOR` fallback/warning contract, selected/context and prefix/separator styling, `rv`, `ne`, and `TERM=dumb` suppression for `--color=auto`. Terminal attachment is observed through the `Icod.DCurses` / `Icod.Terminal` stack without opening a curses session.

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
**Status:** Closed in `1.3.0`

NUL discovery remains the file-level binary heuristic. Under a UTF-8 locale, malformed UTF-8 is handled separately at record granularity: the record still participates in matching, status, and counts, but unsafe detailed record output is suppressed unless `-a` / text mode is selected. This reproduces GNU grep 3.12's `encoding-error` regression behavior, including `-I` continuing to report valid records elsewhere in the same non-NUL file.

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

### T1 — Locale and byte contract — complete in `1.3.0`

G02 and G07 are closed for the repository's documented C/POSIX and UTF-8 profiles. LC_CTYPE controls byte-vs-UTF-8 decoding and character classes; LC_COLLATE is resolved independently for collation; C-locale pattern files preserve byte identity; malformed UTF-8 selected records affect matching/status but unsafe record output is suppressed unless `-a` is used, matching GNU grep 3.12's encoding-error regression contract.

### T2 — Command semantics — complete in `1.1.0`

G04, G05, and G06 are closed with targeted regression tests.

### T3 — Color completeness — complete in `1.2.0`

G03 is closed. `Icod.DCurses 0.1.0` supplies the canonical terminal stack; grep uses terminal endpoint observation for `--color=auto` while retaining GNU SGR strings as the command-owned `GREP_COLORS` policy.

### T4 — PCRE support — complete in `1.4.0`

G01 is closed with PCRE.NET 1.6.0 / PCRE2 10.48. PCRE remains a Grep-local dependency rather than expanding `Icod.CommandFramework` with a heavyweight native dependency. Native packaging and redistribution notices are now part of the verified package/archive contract.

### T5 — Edge compatibility

Close G08 and G09 with targeted tests and implementation only where the selected compatibility target requires it. Treat G10 as optional historical command-name compatibility.

## Release assessment

Version `1.0.1` was the maintenance release that established this audit. The parity sequence then closed the identified command-behavior gaps in discrete releases: `1.1.0` closed G04, G05, and G06; `1.2.0` closed G03; `1.3.0` closed G02 and G07 for the documented C/POSIX and UTF-8 profiles; and `1.4.0` closes G01 with PCRE.NET 1.6.0 / PCRE2 10.48 plus verified native packaging across the supported release RIDs.

After `1.4.0`, the remaining core compatibility work is T5: G08 Windows CRLF/text-mode conformance and G09 multi-character locale collating elements. G10 remains optional historical command-name compatibility and is not required for core `grep` completeness.
