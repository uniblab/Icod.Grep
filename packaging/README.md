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

## Lifecycle

| Lifecycle | Configuration | Entry point |
| --- | --- | --- |
| local `build.cmd` / `build.sh` | `Debug` | `packaging/Invoke-Build.ps1` |
| pull request | `Staging` | `.github/workflows/pull-request.yaml` |
| push to `main` | `Release` | `.github/workflows/main.yaml` |
| manual diagnostic | selected | `.github/workflows/distribution-validation.yaml` |
| `v*` tag contained in `main` | `Release` | `.github/workflows/release.yaml` |

Ordinary pushes to `main` validate but never publish.

## Validate the distributions

For the standard local Debug cycle:

```text
build.cmd
./build.sh
```

Both wrappers run `clean → restore → build → test → pack → validate` through `packaging/Invoke-Build.ps1`.

For a deliberate deep distribution diagnostic:

```text
pwsh ./packaging/VerifyDistribution.ps1 -Configuration Release
```

The deep validation script restores, builds, and tests the solution; exercises the directly built `grep`; creates the `.nupkg`; verifies that it declares exactly one .NET tool command named `grep`; installs the package into an isolated tool path; and exercises the installed tool.

`VerifyPackageArtifact.ps1` is the narrower exact-package gate used by automated package-producing jobs. It verifies the expected package identity and version, the .NET tool settings, the `grep` command/runner, the package icon, and required package payload files without rebuilding the product.

## Build one ZIP archive

For example:

```text
pwsh ./packaging/BuildReleaseArchive.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 1.0.1
```

The resulting archive is written under `artifacts/release/` with a name such as:

```text
Icod.Grep-1.0.1-win-x64.zip
```

The archive builder structurally verifies the archive and executes the staged command whenever the requested RID matches the current host.

## CI and release contract

Pull requests build and test Staging on Windows, Linux, and macOS. Linux produces and exactly verifies the tool package once; that same package artifact is then installed and exercised on all three host families.

Pushes to `main` run Release validation on the six supported OS/architecture runners. Each runner builds/tests the solution and builds/smokes its matching RID archive. Linux x64 additionally produces and exactly verifies the platform-neutral .NET tool package. No publication occurs from `main`.

`distribution-validation.yaml` is manual-only and runs the deeper `VerifyDistribution.ps1` diagnostic on the six supported runners with a selected Debug, Staging, or Release configuration.

Pushing a tag named `v<version>` starts `.github/workflows/release.yaml`. The workflow requires the tagged commit to be contained in `main` and requires the tag version to match both `<Version>` and `<PackageVersion>` in `Icod.Grep.csproj`.

A tagged release:

- builds/tests and exactly verifies the Release .NET tool package once;
- installs and exercises that exact package on Windows, Linux, and macOS;
- independently builds and matching-host smokes all six RID ZIP archives;
- publishes the exact verified package to NuGet.org and GitHub Packages in parallel;
- waits for package publication and all archives;
- creates SHA-256 checksums; and
- creates the GitHub Release with six ZIPs, the `.nupkg`, and checksum file.

The NuGet.org publication uses trusted publishing through `NuGet/login@v1` and the repository's Release environment. GitHub Packages uses the workflow `GITHUB_TOKEN`.
