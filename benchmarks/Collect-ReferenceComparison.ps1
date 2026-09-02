[CmdletBinding()]
param(
    [string]$Filter = '*',
    [string]$OutputDirectory = 'artifacts/performance/reference-comparison',
    [ValidateRange(1, 8)]
    [int]$Passes = 2,
    [ValidateRange(0, 600)]
    [int]$CooldownSeconds = 30,
    [switch]$AllowDirty,
    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'
$baselineCommit = '423c0e9623100492fa01b6e4d14c183761d111d7'
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
            throw 'The authoritative reference comparison requires a clean worktree. Commit/stash changes or use -AllowDirty for an explicitly non-authoritative run.'
        }
    }

    $candidateCommit = (git rev-parse HEAD).Trim()
    if (0 -ne $LASTEXITCODE) {
        throw 'Unable to resolve candidate commit.'
    }

    git cat-file -e "$baselineCommit^{commit}" 2>$null
    if (0 -ne $LASTEXITCODE) {
        git fetch origin $baselineCommit --depth=1
        if (0 -ne $LASTEXITCODE) {
            throw 'Unable to fetch the pinned 1.5.0 baseline commit.'
        }
    }

    $effectivePasses = if ($Smoke) { 1 } else { $Passes }
    $effectiveCooldownSeconds = if ($Smoke) { 0 } else { $CooldownSeconds }
    $totalRuns = 2 * $effectivePasses
    Write-IcodProgressLine "Reference comparison starting. Filter='$Filter'; passes=$effectivePasses; benchmark runs=$totalRuns; cooldown=${effectiveCooldownSeconds}s."
    if (-not $Smoke) {
        Write-IcodProgressLine 'BenchmarkDotNet may spend several minutes inside each run without returning to the PowerShell prompt. This is expected.'
    }

    $outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
    if (Test-Path -LiteralPath $outputRoot) {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    $inventoryPath = Join-Path $repoRoot 'hardware_inventory.txt'
    if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
        throw 'hardware_inventory.txt is required for the reference-host comparison.'
    }

    $repoParent = Split-Path -Parent $repoRoot
    $temporaryRoot = Join-Path $repoParent ('Icod.Grep-T6-' + [Guid]::NewGuid().ToString('N'))
    $baselineRoot = Join-Path $temporaryRoot 'baseline'
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

    Write-IcodProgressLine 'Preparing pinned Icod.Grep 1.5.0 worktree.'
    git worktree add --detach $baselineRoot $baselineCommit
    if (0 -ne $LASTEXITCODE) {
        throw 'Unable to create the pinned 1.5.0 baseline worktree.'
    }

    try {
        $sourceProject = Join-Path $repoRoot 'benchmarks/Grep.Benchmarks'
        $baselineProject = Join-Path $baselineRoot 'benchmarks/Grep.Benchmarks'
        New-Item -ItemType Directory -Path $baselineProject -Force | Out-Null
        Get-ChildItem -LiteralPath $sourceProject -Force | Where-Object {
            $_.Name -notin @('bin', 'obj', 'BenchmarkDotNet.Artifacts')
        } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $baselineProject -Recurse -Force
        }

        function Initialize-IcodBenchmarkVariant {
            param(
                [Parameter(Mandatory)]
                [string]$Root,
                [Parameter(Mandatory)]
                [string]$Label
            )

            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            Write-IcodProgressLine "Restoring/building $Label benchmark harness."
            Push-Location $Root
            try {
                dotnet restore benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj
                if (0 -ne $LASTEXITCODE) {
                    throw "$Label benchmark restore failed."
                }

                dotnet build benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release --no-restore
                if (0 -ne $LASTEXITCODE) {
                    throw "$Label benchmark build failed."
                }
            } finally {
                Pop-Location
                $watch.Stop()
            }
            Write-IcodProgressLine ("$Label harness ready in {0:n1}s." -f $watch.Elapsed.TotalSeconds)
        }

        function Invoke-IcodBenchmarkVariant {
            param(
                [Parameter(Mandatory)]
                [string]$Root,
                [Parameter(Mandatory)]
                [string]$Label,
                [Parameter(Mandatory)]
                [string]$Commit,
                [Parameter(Mandatory)]
                [int]$Pass,
                [Parameter(Mandatory)]
                [int]$RunNumber,
                [Parameter(Mandatory)]
                [int]$RunCount
            )

            $passLabel = "$Label-pass-$Pass"
            $variantOutput = Join-Path $outputRoot $passLabel
            New-Item -ItemType Directory -Path $variantOutput -Force | Out-Null
            $watch = [System.Diagnostics.Stopwatch]::StartNew()

            Write-IcodProgressLine "Starting benchmark run $RunNumber/${RunCount}: $passLabel ($($Commit.Substring(0, 7)))."

            $previousSource = $env:ICOD_BENCHMARK_SOURCE
            $previousLabel = $env:ICOD_BENCHMARK_LABEL
            $previousCommit = $env:ICOD_BENCHMARK_COMMIT
            $previousMetadata = $env:ICOD_BENCHMARK_METADATA_PATH
            $previousInventory = $env:ICOD_REFERENCE_INVENTORY_PATH
            try {
                $env:ICOD_BENCHMARK_SOURCE = if ($Smoke) { 'OrchestrationSmoke' } else { 'PhysicalReference' }
                $env:ICOD_BENCHMARK_LABEL = $passLabel
                $env:ICOD_BENCHMARK_COMMIT = $Commit
                $env:ICOD_BENCHMARK_METADATA_PATH = Join-Path $variantOutput 'metadata.json'
                $env:ICOD_REFERENCE_INVENTORY_PATH = $inventoryPath

                Push-Location $Root
                try {
                    $bdnArtifacts = Join-Path $Root 'BenchmarkDotNet.Artifacts'
                    if (Test-Path -LiteralPath $bdnArtifacts) {
                        Remove-Item -LiteralPath $bdnArtifacts -Recurse -Force
                    }

                    if ($Smoke) {
                        dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release --no-build --no-restore -- --metadata $env:ICOD_BENCHMARK_METADATA_PATH
                        if (0 -ne $LASTEXITCODE) {
                            throw "$passLabel benchmark metadata smoke failed."
                        }
                        dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release --no-build --no-restore -- --smoke
                    } else {
                        dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release --no-build --no-restore -- --filter $Filter
                    }
                    if (0 -ne $LASTEXITCODE) {
                        throw "$passLabel benchmark run failed."
                    }

                    if (Test-Path -LiteralPath $bdnArtifacts) {
                        Copy-Item -LiteralPath $bdnArtifacts -Destination (Join-Path $variantOutput 'BenchmarkDotNet.Artifacts') -Recurse -Force
                    }
                } finally {
                    Pop-Location
                }
            } finally {
                $env:ICOD_BENCHMARK_SOURCE = $previousSource
                $env:ICOD_BENCHMARK_LABEL = $previousLabel
                $env:ICOD_BENCHMARK_COMMIT = $previousCommit
                $env:ICOD_BENCHMARK_METADATA_PATH = $previousMetadata
                $env:ICOD_REFERENCE_INVENTORY_PATH = $previousInventory
                $watch.Stop()
            }

            Write-IcodProgressLine ("Completed benchmark run $RunNumber/${RunCount}: $passLabel in {0:n1} minutes." -f $watch.Elapsed.TotalMinutes)

            return [PSCustomObject]@{
                Label = $Label
                Pass = $Pass
                Output = $passLabel
                Commit = $Commit
                ElapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
            }
        }

        Initialize-IcodBenchmarkVariant -Root $baselineRoot -Label 'baseline-1.5.0'
        Initialize-IcodBenchmarkVariant -Root $repoRoot -Label 'candidate'

        $sequence = New-Object System.Collections.Generic.List[object]
        $runNumber = 0

        for ($pass = 1; $pass -le $effectivePasses; $pass++) {
            if (0 -eq ($pass % 2)) {
                $variants = @(
                    [PSCustomObject]@{ Root = $repoRoot; Label = 'candidate'; Commit = $candidateCommit },
                    [PSCustomObject]@{ Root = $baselineRoot; Label = 'baseline-1.5.0'; Commit = $baselineCommit }
                )
            } else {
                $variants = @(
                    [PSCustomObject]@{ Root = $baselineRoot; Label = 'baseline-1.5.0'; Commit = $baselineCommit },
                    [PSCustomObject]@{ Root = $repoRoot; Label = 'candidate'; Commit = $candidateCommit }
                )
            }

            foreach ($variant in $variants) {
                $runNumber++
                $sequence.Add(
                    (Invoke-IcodBenchmarkVariant `
                        -Root $variant.Root `
                        -Label $variant.Label `
                        -Commit $variant.Commit `
                        -Pass $pass `
                        -RunNumber $runNumber `
                        -RunCount $totalRuns)
                )

                if (0 -lt $effectiveCooldownSeconds -and $runNumber -lt $totalRuns) {
                    Write-IcodProgressLine "Cooling down for $effectiveCooldownSeconds seconds before the next benchmark run."
                    Start-Sleep -Seconds $effectiveCooldownSeconds
                }
            }
        }

        $comparison = [PSCustomObject]@{
            SchemaVersion = 3
            BaselineCommit = $baselineCommit
            CandidateCommit = $candidateCommit
            Filter = $Filter
            Smoke = [bool]$Smoke
            Passes = $effectivePasses
            CooldownSeconds = $effectiveCooldownSeconds
            Sequence = $sequence.ToArray()
            HardwareInventorySha256 = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
            CollectedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        } | ConvertTo-Json -Depth 6
        $comparisonPath = Join-Path $outputRoot 'comparison.json'
        [System.IO.File]::WriteAllText(
            $comparisonPath,
            $comparison,
            [System.Text.UTF8Encoding]::new($false)
        )

        Write-IcodProgressLine "Reference comparison complete. Results: $outputRoot"
    } finally {
        Write-IcodProgressLine 'Removing temporary 1.5.0 worktree.'
        git worktree remove --force $baselineRoot 2>$null
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
} finally {
    Pop-Location
}
