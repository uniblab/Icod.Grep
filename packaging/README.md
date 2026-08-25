# Icod.Grep distribution packaging

`Icod.Grep` has two supported distribution forms:

1. a framework-dependent .NET tool package with package ID `Icod.Grep` and command name `grep`;
2. runtime-specific ZIP archives containing a single published `grep` executable plus `README.md` and `LICENSE`.

The ZIP archives are produced for:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

The default ZIPs are framework-dependent and therefore require the .NET 10 runtime. `BuildReleaseArchive.ps1` also accepts `-SelfContained` for manual builds when a self-contained archive is useful.

## Validate the distributions

From the repository root:

```text
pwsh ./packaging/VerifyDistribution.ps1 -Configuration Release
```

The validation script restores, builds, and tests the solution; exercises the directly built `grep`; creates the `.nupkg`; verifies that it declares exactly one .NET tool command named `grep`; installs the package into an isolated tool path; and exercises the installed tool.

## Build one ZIP archive

For example:

```text
pwsh ./packaging/BuildReleaseArchive.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 1.0.0
```

The resulting archive is written under `artifacts/release/` with a name such as:

```text
Icod.Grep-1.0.0-win-x64.zip
```

## Release contract

Pushing a tag named `v<version>` starts `.github/workflows/release.yaml`. The workflow requires the tagged commit to be contained in `main` and requires the tag version to match both `<Version>` and `<PackageVersion>` in `Icod.Grep.csproj`.

A successful release:

- validates the distribution on Windows, Linux, and macOS on x64 and ARM64;
- builds all six ZIP archives;
- builds the `Icod.Grep` NuGet tool package;
- publishes the package to NuGet.org;
- publishes the package to GitHub Packages;
- creates SHA-256 checksums; and
- creates the GitHub Release with the ZIPs, `.nupkg`, and checksum file.

The NuGet.org publication follows the same trusted-publishing setup used by the other Icod command suites and expects the `NUGET_USER` repository secret used by `NuGet/login@v1`.
