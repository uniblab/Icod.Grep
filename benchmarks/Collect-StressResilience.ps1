[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts/performance/T6.8-resilience',
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (git rev-parse --show-toplevel).Trim()
if (0 -ne $LASTEXITCODE -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    throw 'Unable to resolve the repository root.'
}

function Write-IcodProgressLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ('[{0}] {1}' -f [DateTimeOffset]::Now.ToString('HH:mm:ss'), $Message)
}

Push-Location $repoRoot
try {
    if (-not $AllowDirty) {
        [object[]]$dirty = @(git status --porcelain)
        if (0 -ne $LASTEXITCODE) {
            throw 'Unable to inspect repository status.'
        }
        if (0 -lt $dirty.Length) {
            throw 'The authoritative T6.8 resilience collection requires a clean worktree. Commit/stash changes or use -AllowDirty for an explicitly non-authoritative run.'
        }
    }

    $commit = (git rev-parse HEAD).Trim()
    if (0 -ne $LASTEXITCODE -or [string]::IsNullOrWhiteSpace($commit)) {
        throw 'Unable to resolve the candidate commit.'
    }

    $inventoryPath = Join-Path $repoRoot 'hardware_inventory.txt'
    if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
        throw 'hardware_inventory.txt is required for the physical T6.8 resilience collection.'
    }
    $inventoryHash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()

    $outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
    if (Test-Path -LiteralPath $outputRoot) {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    Write-IcodProgressLine "Preparing T6.8 resilience harness at commit $($commit.Substring(0, 7))."
    & dotnet restore benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj | Out-Host
    if (0 -ne $LASTEXITCODE) {
        throw 'T6.8 resilience-harness restore failed.'
    }
    & dotnet build benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release --no-restore -p:ContinuousIntegrationBuild=true | Out-Host
    if (0 -ne $LASTEXITCODE) {
        throw 'T6.8 resilience-harness build failed.'
    }

    $previousSource = $env:ICOD_BENCHMARK_SOURCE
    $previousLabel = $env:ICOD_BENCHMARK_LABEL
    $previousCommit = $env:ICOD_BENCHMARK_COMMIT
    $previousInventory = $env:ICOD_REFERENCE_INVENTORY_PATH
    $resultPath = Join-Path $outputRoot 'resilience.json'
    $startedUtc = [DateTimeOffset]::UtcNow
    $watch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $env:ICOD_BENCHMARK_SOURCE = 'PhysicalReferenceStress'
        $env:ICOD_BENCHMARK_LABEL = 'T6.8-resilience'
        $env:ICOD_BENCHMARK_COMMIT = $commit
        $env:ICOD_REFERENCE_INVENTORY_PATH = $inventoryPath

        Write-IcodProgressLine 'Starting T6.8 sustained resilience profile.'
        & dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release --no-build --no-restore -- --stress-resilience $resultPath | Out-Host
        $exitCode = $LASTEXITCODE
    } finally {
        $watch.Stop()
        $env:ICOD_BENCHMARK_SOURCE = $previousSource
        $env:ICOD_BENCHMARK_LABEL = $previousLabel
        $env:ICOD_BENCHMARK_COMMIT = $previousCommit
        $env:ICOD_REFERENCE_INVENTORY_PATH = $previousInventory
    }

    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw 'T6.8 resilience profile did not produce its JSON report.'
    }

    $report = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($report.Commit -ne $commit) {
        throw "Resilience report recorded commit '$($report.Commit)' instead of '$commit'."
    }
    if ($report.HardwareInventorySha256 -ne $inventoryHash) {
        throw "Resilience report recorded hardware hash '$($report.HardwareInventorySha256)' instead of '$inventoryHash'."
    }

    $scenarioCount = @($report.Results).Count
    $allSucceeded = [bool]$report.AllSucceeded
    $manifest = [PSCustomObject]@{
        SchemaVersion = 1
        Commit = $commit
        HardwareInventorySha256 = $inventoryHash
        CollectedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        StartedUtc = $startedUtc.ToString('O')
        Output = [System.IO.Path]::GetFileName($resultPath)
        ExitCode = $exitCode
        AllSucceeded = $allSucceeded
        ScenarioCount = $scenarioCount
        ElapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
    } | ConvertTo-Json -Depth 4

    [System.IO.File]::WriteAllText(
        (Join-Path $outputRoot 'manifest.json'),
        $manifest,
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-IcodProgressLine "T6.8 resilience collection complete in $([Math]::Round($watch.Elapsed.TotalSeconds, 1)) seconds. Results: $outputRoot"
    if (0 -ne $exitCode -or -not $allSucceeded) {
        throw 'One or more T6.8 resilience scenarios failed. Inspect resilience.json and manifest.json.'
    }
} finally {
    Pop-Location
}
