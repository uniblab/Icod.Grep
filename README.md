# GREP(1)

[![PR Staging build](https://github.com/uniblab/Icod.Grep/actions/workflows/pull-request.yaml/badge.svg)](https://github.com/uniblab/Icod.Grep/actions/workflows/pull-request.yaml)
[![Main Release validation](https://github.com/uniblab/Icod.Grep/actions/workflows/main.yaml/badge.svg?branch=main)](https://github.com/uniblab/Icod.Grep/actions/workflows/main.yaml)

## NAME

**grep** — print lines that match patterns

## SYNOPSIS

```text
grep [OPTION]... PATTERNS [FILE]...
```

## DESCRIPTION

`Icod.Grep` is a cross-platform .NET implementation of GNU `grep(1)`, currently modeled on GNU grep 3.12.

`grep` searches each FILE, or standard input when no FILE is supplied, for records that match one or more patterns. Selected records are written to standard output unless an output-suppressing or summary option is in effect.

The implementation is byte-preserving where GNU behavior requires it and supports GNU Basic Regular Expressions, GNU Extended Regular Expressions, fixed-string matching, Perl-compatible regular expressions, recursive directory traversal, pathname include/exclude rules, binary-file policies, byte offsets, record numbers, context output, only-matching output, file-list modes, color output, NUL-delimited records, and conventional GNU grep exit-status semantics.

Neutral infrastructure comes from `Icod.CommandFramework`, including command-line parsing, diagnostics, byte-stream helpers, record readers, read-only filesystem traversal, pathname patterns, and GNU-compatible regular-expression contracts. `Icod.Grep` has no dependency on `Icod.CoreUtils.Shared`. GNU color/terminal integration is routed through `Icod.DCurses 0.1.0`; its terminal stack provides cross-platform terminal attachment detection while grep preserves GNU `GREP_COLORS` SGR semantics.

Perl-compatible regular expressions (`-P`) are implemented with PCRE.NET 1.6.0 / PCRE2 10.48. UTF-8 locales enable PCRE2 UTF/UCP semantics while retaining GNU grep's ASCII-only `\d` behavior; C/POSIX remains byte-oriented.

The current GNU grep 3.12 feature-completeness assessment and remaining compatibility work are tracked in [`Icod.Grep-GNU-grep-3.12-Feature-Completeness-Audit.md`](Icod.Grep-GNU-grep-3.12-Feature-Completeness-Audit.md).

## INSTALLATION

Install the .NET tool from NuGet.org:

```text
dotnet tool install --global Icod.Grep --version 1.4.0
```

The package installs a single command named `grep`.

Runtime-specific ZIP distributions are also published for Windows, Linux, and macOS on x64 and ARM64. The ZIP archives contain `grep` (or `grep.exe` on Windows), `README.md`, `LICENSE`, and `THIRD-PARTY-NOTICES.md`, and require the .NET 10 runtime.

## DEVELOPMENT AND RELEASE

The repository follows the canonical Icod build lifecycle:

- local `build.cmd` / `build.sh` runs the `Debug` cycle;
- pull requests validate `Staging` on Windows, Linux, and macOS, install and exercise the exact packed tool on all three host families, and build/smoke all six supported RID archives;
- pushes to `main` run validation-only `Release` across Windows, Linux, and macOS on x64 and ARM64;
- `distribution-validation.yaml` is a manual deep-distribution diagnostic; and
- only a `v<semver>` tag whose commit is contained in `main`, with a version matching the project package version, may publish a Release.

The local wrappers run `clean → restore → build → test → pack → validate` through `packaging/Invoke-Build.ps1`. Pull-request packaging is produced once and the exact artifact is installed and exercised on all three host families, including a real PCRE lookbehind smoke. Because PCRE.NET carries architecture-specific native code, PR validation also builds and smokes the matching standalone archive on Windows x64, Windows ARM64, Linux x64, Linux ARM64, macOS x64, and macOS ARM64. The six Release runners on `main` each build and smoke their matching RID ZIP archive; Linux x64 additionally produces and verifies the platform-neutral .NET tool package. Ordinary pushes to `main` never publish.

Tagged Release publication keeps the .NET tool package and the six RID archives independent until the final release rendezvous. NuGet.org and GitHub Packages publish the same exact verified package in parallel. See `packaging/README.md` for the detailed distribution contract.

Debug, Staging, and Release all use portable debug information in the product and test projects.

## OPTIONS

### Pattern selection and interpretation

```text
-E, --extended-regexp
    Interpret PATTERNS as extended regular expressions.

-F, --fixed-strings
    Interpret PATTERNS as fixed strings.

-G, --basic-regexp
    Interpret PATTERNS as basic regular expressions. This is the default.

-P, --perl-regexp
    Interpret PATTERNS as Perl-compatible regular expressions.

-e, --regexp=PATTERNS
    Use PATTERNS for matching. May be specified more than once.

-f, --file=FILE
    Read patterns from FILE. FILE may be - for standard input.

-i, -y, --ignore-case
    Ignore case distinctions in patterns and input.

--no-ignore-case
    Restore case-sensitive matching.

-w, --word-regexp
    Select matches that form whole words.

-x, --line-regexp
    Select only records whose entire contents match.

-z, --null-data
    Treat NUL rather than newline as the record delimiter.
```

### Output control

```text
-v, --invert-match
    Select nonmatching records.

-m, --max-count=NUM
    Stop after NUM selected records. -1 means unlimited.

-b, --byte-offset
    Print the byte offset with output records.

-n, --line-number
    Print the input record number with output records.

-H, --with-filename
    Always print the input filename.

-h, --no-filename
    Suppress filename prefixes.

--label=LABEL
    Use LABEL as the displayed name for standard input.

-o, --only-matching
    Print only nonempty matching portions of selected records.

-q, --quiet, --silent
    Suppress normal output and stop after the first selected result.

-c, --count
    Print only a count of selected records for each input.

-l, --files-with-matches
    Print only names of files containing selected records.

-L, --files-without-match
    Print only names of files containing no selected records.

-Z, --null
    Print NUL after filenames instead of the normal delimiter.

--color[=WHEN], --colour[=WHEN]
    Surround matching text with terminal color escapes according to WHEN.
```

### File and directory selection

```text
-a, --text
    Process binary data as text.

-I
    Treat binary files as having no match.

--binary-files=TYPE
    TYPE is binary, text, or without-match.

-d, --directories=ACTION
    ACTION is read, recurse, or skip.

-D, --devices=ACTION
    ACTION is read or skip.

-r, --recursive
    Recurse into directories, following command-line directory links.

-R, --dereference-recursive
    Recurse into directories and follow directory symbolic links.

--include=GLOB
    Search only files matching GLOB.

--exclude=GLOB
    Skip files matching GLOB.

--exclude-from=FILE
    Read exclusion patterns from FILE.

--exclude-dir=GLOB
    Skip directories matching GLOB.
```

### Context and formatting

```text
-B, --before-context=NUM
    Print NUM records of leading context.

-A, --after-context=NUM
    Print NUM records of trailing context.

-C, --context=NUM
    Print NUM records of leading and trailing context.

-NUM
    Legacy shorthand for --context=NUM.

--group-separator=SEP
    Use SEP between separated groups of output.

--no-group-separator
    Do not print group separators.

-s, --no-messages
    Suppress input-file diagnostics.

--line-buffered
    Flush output after each output record.

-T, --initial-tab
    Align record content after prefixes.

-U, --binary
    Retain binary platform input mode.

--help
    Display command help.

-V, --version
    Display version information.
```

## PATTERNS

When neither `-e` nor `-f` is present, the first non-option operand supplies PATTERNS.

Multiple `-e` and `-f` sources are combined in encounter order. Newlines inside an expression separate patterns. An empty pattern file contributes no patterns, while an explicitly empty expression is a valid pattern that selects every record.

GNU Basic Regular Expressions and GNU Extended Regular Expressions are implemented through `Icod.CommandFramework.RegularExpressions`. Fixed-string mode performs literal matching with the selected case policy.

Perl-compatible regular expressions are implemented through PCRE.NET 1.6.0 / PCRE2 10.48. In C/POSIX locale mode PCRE operates on the preserved 8-bit record bytes. In supported UTF-8 locales grep enables PCRE2 UTF and UCP behavior plus malformed-UTF matching support, while retaining GNU grep's ASCII-only `\d` semantics; PCRE2 pattern-level overrides such as `(?-aD)` remain available.

## FILES

With no FILE operand, `grep` reads standard input. A FILE operand of `-` also denotes standard input.

Recursive modes use the read-only traversal engine from `Icod.CommandFramework`. Include, exclude, and exclude-directory patterns are applied according to their command-line order and traversal context.

## BINARY INPUT

Unless text mode is requested, input containing NUL bytes is treated as binary. The default binary policy reports that a binary file matches rather than emitting matching record contents. `-a` treats binary data as text, while `-I` and `--binary-files=without-match` treat binary files as having no match.

## EXIT STATUS

```text
0   At least one result was selected.
1   No result was selected.
2   An error occurred.
```

Cancellation uses the shared command-framework cancellation status.

## PLATFORM NOTES

The implementation targets .NET 10 and is intended to run on Windows, Linux, and macOS. Filesystem traversal and symbolic-link behavior are delegated to cross-platform `Icod.CommandFramework` contracts rather than hidden behind command-local approximations.

## AUTHORS

Inspired by original work from Ken Thompson, author of the original Unix `grep`, and Mike Haertel, author of the original GNU `grep`, together with the many GNU grep contributors whose work developed and maintained the modern utility.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `LICENSE` for the Icod.Grep license and `THIRD-PARTY-NOTICES.md` for the PCRE.NET, PCRE2, and sljit notices applicable to the redistributed `-P` runtime.

## SEE ALSO

`grep(1)`, `egrep(1)`, `fgrep(1)`, `regex(7)`
