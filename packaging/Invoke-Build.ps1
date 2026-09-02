param(
    [ValidateSet('all','clean','restore','build','test','pack','validate')]
    [string]$Section = 'all',

    [ValidateSet('Debug','Staging','Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'Icod.Grep.sln'
$projectPath = Join-Path $repositoryRoot 'Icod.Grep.csproj'
$artifactDirectory = Join-Path $repositoryRoot 'artifacts/package'

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Write-Host "> dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if (0 -ne $LASTEXITCODE) {
        throw "dotnet exited with status $LASTEXITCODE."
    }
}

function Invoke-Section {
    param([Parameter(Mandatory = $true)][string]$Name)

    switch ($Name) {
        'clean' {
            Invoke-DotNet -Arguments @('clean', $solutionPath, '-c', $Configuration)
        }
        'restore' {
            Invoke-DotNet -Arguments @('restore', $solutionPath)
        }
        'build' {
            Invoke-DotNet -Arguments @('build', $solutionPath, '-c', $Configuration, '--no-restore')
        }
        'test' {
            Invoke-DotNet -Arguments @('test', $solutionPath, '-c', $Configuration, '--no-build', '--no-restore')
        }
        'pack' {
            if (Test-Path -LiteralPath $artifactDirectory) {
                Remove-Item -LiteralPath $artifactDirectory -Recurse -Force
            }
            New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
            Invoke-DotNet -Arguments @(
                'pack', $projectPath,
                '-c', $Configuration,
                '--no-build',
                '--no-restore',
                '-o', $artifactDirectory
            )
        }
        'validate' {
            & (Join-Path $PSScriptRoot 'VerifyPackageArtifact.ps1') `
                -ArtifactDirectory $artifactDirectory `
                -Configuration $Configuration
        }
    }
}

Push-Location $repositoryRoot
try {
    if ('all' -eq $Section) {
        foreach ($name in @('clean','restore','build','test','pack','validate')) {
            Invoke-Section -Name $name
        }
    } else {
        Invoke-Section -Name $Section
    }
} finally {
    Pop-Location
}
