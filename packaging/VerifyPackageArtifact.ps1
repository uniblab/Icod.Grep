param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [ValidateSet('Debug','Staging','Release')]
    [string]$Configuration = 'Release',

    [string]$ExpectedVersion = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $repositoryRoot $ArtifactDirectory
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory '$ArtifactDirectory' does not exist."
}

[xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Icod.Grep.csproj') -Raw
function Get-ProjectProperty {
    param([Parameter(Mandatory = $true)][string]$Name)
    $node = $project.SelectSingleNode("/Project/PropertyGroup/$Name[normalize-space(.) != '']")
    if ($null -eq $node) {
        throw "Icod.Grep.csproj does not declare '$Name'."
    }
    return $node.InnerText.Trim()
}

$packageId = Get-ProjectProperty -Name 'PackageId'
$packageVersion = Get-ProjectProperty -Name 'PackageVersion'
$targetFramework = Get-ProjectProperty -Name 'TargetFramework'
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $ExpectedVersion -ne $packageVersion) {
    throw "Project package version '$packageVersion' does not match expected '$ExpectedVersion'."
}

$packages = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Filter "$packageId.*.nupkg" -File | Sort-Object Name)
if (1 -ne $packages.Count) {
    throw "Expected exactly one $packageId .nupkg; found $($packages.Count)."
}
$package = $packages[0]
$expectedName = "$packageId.$packageVersion.nupkg"
if ($package.Name -ne $expectedName) {
    throw "Package '$($package.Name)' does not match expected '$expectedName'."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\\','/') })
    $settingsPath = "tools/$targetFramework/any/DotnetToolSettings.xml"
    foreach ($required in @(
        $settingsPath,
        "tools/$targetFramework/any/grep.dll",
        "tools/$targetFramework/any/grep.runtimeconfig.json",
        'README.md',
        'LICENSE'
    )) {
        if ($required -notin $entries) {
            throw "Package '$($package.Name)' is missing '$required'."
        }
    }

    $settingsEntry = $archive.Entries | Where-Object { $_.FullName.Replace('\\','/') -eq $settingsPath } | Select-Object -First 1
    $reader = [System.IO.StreamReader]::new($settingsEntry.Open())
    try {
        [xml]$settings = $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
    }

    $commands = @($settings.DotNetCliTool.Commands.Command)
    if (1 -ne $commands.Count) {
        throw "Package '$($package.Name)' declares $($commands.Count) tool commands; expected exactly one."
    }
    if ('grep' -ne "$($commands[0].Name)" -or 'dotnet' -ne "$($commands[0].Runner)") {
        throw "Package '$($package.Name)' does not declare the expected grep/dotnet tool command."
    }
} finally {
    $archive.Dispose()
}

Write-Host "Exact Icod.Grep package verification completed successfully for $packageVersion ($Configuration)."
