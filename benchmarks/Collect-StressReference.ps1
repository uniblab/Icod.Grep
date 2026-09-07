[CmdletBinding()]
param(
    [ValidateSet('records', 'files', 'patterns')]
    [string[]]$Profiles = @('records', 'files', 'patterns'),
    [string]$OutputDirectory = 'artifacts/performance/T6.8-reference',
    [ValidateRange(0, 600)]
    [int]$CooldownSeconds = 30,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
if (0 -ne $LASTEXITCODE -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    throw 'Unable to resolve the repository root.'
}

function Write-IcodProgressLine {
    param(
        [Parameter(Mandatory)]
        [string]$Message
    )

    Write-Host (
        '[{0}] {1}' -f (
            [DateTimeOffset]::Now.ToString('HH:mm:ss'),
            $Message
        )
    )
}

Push-Location $repoRoot
try {
    if (-not $AllowDirty) {
        $dirty = @(git status --porcelain)
        if (0 -ne $LASTEXITCODE) {
            throw 'Unable to inspect repository status.'
        }
        if (0 -lt $dirty.Count) {
            throw 'The authoritative T6.8 stress collection requires a clean worktree. Commit/stash changes or use -AllowDirty for an explicitly non-authoritative run.'
        }
    }

    $commit = (git rev-parse HEAD).Trim()
    if (0 -ne $LASTEXITCODE -or [string]::IsNullOrWhiteSpace($commit)) {
        throw 'Unable to resolve the candidate commit.'
    }

    $inventoryPath = Join-Path $repoRoot 'hardware_inventory.txt'
    if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
        throw 'hardware_inventory.txt is required for the physical T6.8 reference collection.'
    }
    $inventoryHash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()

    $outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
    if (Test-Path -LiteralPath $outputRoot) {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    Write-IcodProgressLine "Preparing T6.8 stress harness at commit $($commit.Substring(0, 7))."
    dotnet restore benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj
    if (0 -ne $LASTEXITCODE) {
        throw 'T6.8 stress-harness restore failed.'
    }
    dotnet build benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release --no-restore
    if (0 -ne $LASTEXITCODE) {
        throw 'T6.8 stress-harness build failed.'
    }

    $previousSource = $env:ICOD_BENCHMARK_SOURCE
    $previousLabel = $env:ICOD_BENCHMARK_LABEL
    $previousCommit = $env:ICOD_BENCHMARK_COMMIT
    $previousInventory = $env:ICOD_REFERENCE_INVENTORY_PATH
    $collectionStartedUtc = [DateTimeOffset]::UtcNow
    $profileResults = New-Object System.Collections.Generic.List[object]
    $hadFailure = $false

    try {
        $env:ICOD_BENCHMARK_SOURCE = 'PhysicalReferenceStress'
        $env:ICOD_BENCHMARK_COMMIT = $commit
        $env:ICOD_REFERENCE_INVENTORY_PATH = $inventoryPath

        for ($index = 0; $index -lt $Profiles.Count; $index++) {
            $profile = $Profiles[$index]
            $env:ICOD_BENCHMARK_LABEL = "T6.8-$profile"
            $resultPath = Join-Path $outputRoot "$profile.json"
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            Write-IcodProgressLine "Starting T6.8 '$profile' profile ($($index + 1)/$($Profiles.Count))."

            dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release --no-build --no-restore -- --stress-profile $profile $resultPath
            $exitCode = $LASTEXITCODE
            $watch.Stop()

            $reportSucceeded = $false
            $scenarioCount = 0
            if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
                try {
                    $report = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
                    $reportSucceeded = [bool]$report.AllSucceeded
                    $scenarioCount = @($report.Results).Count
                    if ($report.Commit -ne $commit) {
                        throw "Profile '$profile' recorded commit '$($report.Commit)' instead of '$commit'."
                    }
                    if ($report.HardwareInventorySha256 -ne $inventoryHash) {
                        throw "Profile '$profile' recorded hardware hash '$($report.HardwareInventorySha256)' instead of '$inventoryHash'."
                    }
                } catch {
                    Write-Warning "Unable to validate '$profile' report: $($_.Exception.Message)"
                    $reportSucceeded = $false
                }
            }

            $succeeded = (0 -eq $exitCode) -and $reportSucceeded
            if (-not $succeeded) {
                $hadFailure = $true
            }
            $profileResults.Add(
                [PSCustomObject]@{
                    Profile = $profile
                    Output = [System.IO.Path]::GetFileName($resultPath)
                    ExitCode = $exitCode
                    AllSucceeded = $reportSucceeded
                    ScenarioCount = $scenarioCount
                    ElapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
                }
            )

            Write-IcodProgressLine ("Completed T6.8 '$profile' profile in {0:n1} minutes; success={1}." -f $watch.Elapsed.TotalMinutes, $succeeded)
            if (0 -lt $CooldownSeconds -and $index -lt ($Profiles.Count - 1)) {
                Write-IcodProgressLine "Cooling down for $CooldownSeconds seconds before the next profile."
                Start-Sleep -Seconds $CooldownSeconds
            }
        }
    } finally {
        $env:ICOD_BENCHMARK_SOURCE = $previousSource
        $env:ICOD_BENCHMARK_LABEL = $previousLabel
        $env:ICOD_BENCHMARK_COMMIT = $previousCommit
        $env:ICOD_REFERENCE_INVENTORY_PATH = $previousInventory
    }

    $manifest = [PSCustomObject]@{
        SchemaVersion = 1
        Commit = $commit
        HardwareInventorySha256 = $inventoryHash
        CollectedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        StartedUtc = $collectionStartedUtc.ToString('O')
        Profiles = $profileResults.ToArray()
        AllSucceeded = -not $hadFailure
    } | ConvertTo-Json -Depth 6
    $manifestPath = Join-Path $outputRoot 'manifest.json'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        $manifest,
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-IcodProgressLine "T6.8 reference collection complete. Results: $outputRoot"
    if ($hadFailure) {
        throw 'One or more T6.8 stress profiles failed. Inspect the retained JSON reports and manifest.'
    }
} finally {
    Pop-Location
}
