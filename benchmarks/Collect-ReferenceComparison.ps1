[CmdletBinding()]
param(
    [string]$Filter = '*CommandBenchmarks*',
    [string]$OutputDirectory = 'artifacts/performance/reference-comparison',
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$baselineCommit = '423c0e9623100492fa01b6e4d14c183761d111d7'
$repoRoot = (git rev-parse --show-toplevel).Trim()
if (0 -ne $LASTEXITCODE -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    throw 'Unable to resolve the repository root.'
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

    $outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    $inventoryPath = Join-Path $repoRoot 'hardware_inventory.txt'
    if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
        throw 'hardware_inventory.txt is required for the reference-host comparison.'
    }

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('Icod.Grep-T6-' + [Guid]::NewGuid().ToString('N'))
    $baselineRoot = Join-Path $temporaryRoot 'baseline'
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

    git worktree add --detach $baselineRoot $baselineCommit
    if (0 -ne $LASTEXITCODE) {
        throw 'Unable to create the pinned 1.5.0 baseline worktree.'
    }

    try {
        $sourceBenchmarks = Join-Path $repoRoot 'benchmarks'
        $baselineBenchmarks = Join-Path $baselineRoot 'benchmarks'
        New-Item -ItemType Directory -Path $baselineBenchmarks -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceBenchmarks 'Grep.Benchmarks') -Destination $baselineBenchmarks -Recurse -Force

        function Invoke-IcodBenchmarkVariant {
            param(
                [Parameter(Mandatory)]
                [string]$Root,
                [Parameter(Mandatory)]
                [string]$Label,
                [Parameter(Mandatory)]
                [string]$Commit
            )

            $variantOutput = Join-Path $outputRoot $Label
            if (Test-Path -LiteralPath $variantOutput) {
                Remove-Item -LiteralPath $variantOutput -Recurse -Force
            }
            New-Item -ItemType Directory -Path $variantOutput -Force | Out-Null

            $previousSource = $env:ICOD_BENCHMARK_SOURCE
            $previousLabel = $env:ICOD_BENCHMARK_LABEL
            $previousCommit = $env:ICOD_BENCHMARK_COMMIT
            $previousMetadata = $env:ICOD_BENCHMARK_METADATA_PATH
            $previousInventory = $env:ICOD_REFERENCE_INVENTORY_PATH
            try {
                $env:ICOD_BENCHMARK_SOURCE = 'PhysicalReference'
                $env:ICOD_BENCHMARK_LABEL = $Label
                $env:ICOD_BENCHMARK_COMMIT = $Commit
                $env:ICOD_BENCHMARK_METADATA_PATH = Join-Path $variantOutput 'metadata.json'
                $env:ICOD_REFERENCE_INVENTORY_PATH = $inventoryPath

                Push-Location $Root
                try {
                    $bdnArtifacts = Join-Path $Root 'BenchmarkDotNet.Artifacts'
                    if (Test-Path -LiteralPath $bdnArtifacts) {
                        Remove-Item -LiteralPath $bdnArtifacts -Recurse -Force
                    }

                    dotnet restore benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj
                    if (0 -ne $LASTEXITCODE) {
                        throw "$Label benchmark restore failed."
                    }

                    dotnet run --project benchmarks/Grep.Benchmarks/Icod.Grep.Benchmarks.csproj -c Release -- --filter $Filter
                    if (0 -ne $LASTEXITCODE) {
                        throw "$Label benchmark run failed."
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
            }
        }

        Invoke-IcodBenchmarkVariant -Root $baselineRoot -Label 'baseline-1.5.0' -Commit $baselineCommit
        Invoke-IcodBenchmarkVariant -Root $repoRoot -Label 'candidate' -Commit $candidateCommit

        [PSCustomObject]@{
            SchemaVersion = 1
            BaselineCommit = $baselineCommit
            CandidateCommit = $candidateCommit
            Filter = $Filter
            HardwareInventorySha256 = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
            CollectedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'comparison.json') -Encoding utf8NoBOM

        Write-Host "Reference comparison written to $outputRoot"
    } finally {
        git worktree remove --force $baselineRoot 2>$null
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
} finally {
    Pop-Location
}
