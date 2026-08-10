param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$AssemblyPath = (Join-Path `
        $PSScriptRoot `
        "source\bin\Release\UNMA.dll"),

    [string]$OutputDirectory = (Join-Path $PSScriptRoot "dist"),

    [switch]$Force,

    [switch]$SkipGitChecks
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-CanonicalPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$BasePath
    )

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Assert-EqualText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not [string]::Equals(
            $Actual,
            $Expected,
            [StringComparison]::Ordinal)) {
        throw "$Description is '$Actual'; expected '$Expected'."
    }
}

function Add-PackageFile {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Files,

        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$ArchivePath
    )

    $resolvedSourcePath = Get-CanonicalPath `
        -Path $SourcePath `
        -BasePath $PSScriptRoot
    if (-not (Test-Path -LiteralPath $resolvedSourcePath -PathType Leaf)) {
        throw "Required package file was not found: $resolvedSourcePath"
    }

    $normalizedArchivePath = $ArchivePath.Replace("\", "/")
    if (-not $normalizedArchivePath.StartsWith(
            "UNMA/",
            [StringComparison]::Ordinal)) {
        throw "Package entry must be below UNMA/: $normalizedArchivePath"
    }

    $Files.Add([pscustomobject]@{
        SourcePath = $resolvedSourcePath
        ArchivePath = $normalizedArchivePath
    })
}

function Get-GitOutput {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & git -C $repositoryRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git -C $repositoryRoot $($Arguments -join ' ') failed: $output"
    }
    return ($output | Out-String).Trim()
}

$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$resolvedAssemblyPath = Get-CanonicalPath `
    -Path $AssemblyPath `
    -BasePath $repositoryRoot
$installedAssemblyPath = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot "UNMA.dll"))
if ([string]::Equals(
        $resolvedAssemblyPath,
        $installedAssemblyPath,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw (
        "Refusing to package the active mod-root UNMA.dll. " +
        "Use a non-deploying build output such as " +
        "source\bin\Release\UNMA.dll.")
}
if (-not (Test-Path -LiteralPath $resolvedAssemblyPath -PathType Leaf)) {
    throw "Release assembly was not found: $resolvedAssemblyPath"
}

$manifestPath = Join-Path $repositoryRoot "manifest.json"
$manifest = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath $manifestPath | ConvertFrom-Json
Assert-EqualText `
    -Actual ([string]$manifest.version) `
    -Expected $Version `
    -Description "manifest.json version"

[xml]$project = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "source\UNMA.csproj")
$projectVersionNode = $project.SelectSingleNode(
    "/Project/PropertyGroup/Version")
$projectFileVersionNode = $project.SelectSingleNode(
    "/Project/PropertyGroup/FileVersion")
if ($null -eq $projectVersionNode -or
    $null -eq $projectFileVersionNode) {
    throw "UNMA.csproj does not declare Version and FileVersion."
}
$projectVersion = [string]$projectVersionNode.InnerText
$projectFileVersion = [string]$projectFileVersionNode.InnerText
Assert-EqualText `
    -Actual $projectVersion `
    -Expected $Version `
    -Description "UNMA.csproj Version"
Assert-EqualText `
    -Actual $projectFileVersion `
    -Expected "$Version.0" `
    -Description "UNMA.csproj FileVersion"

$assemblyVersionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo(
    $resolvedAssemblyPath)
Assert-EqualText `
    -Actual ([string]$assemblyVersionInfo.FileVersion) `
    -Expected "$Version.0" `
    -Description "UNMA.dll FileVersion"
if (-not ([string]$assemblyVersionInfo.ProductVersion).StartsWith(
        $Version,
        [StringComparison]::Ordinal)) {
    throw (
        "UNMA.dll ProductVersion is " +
        "'$($assemblyVersionInfo.ProductVersion)'; expected '$Version'.")
}

if (-not $SkipGitChecks) {
    $gitRoot = Get-GitOutput -Arguments @("rev-parse", "--show-toplevel")
    if (-not [string]::Equals(
            [IO.Path]::GetFullPath($gitRoot),
            $repositoryRoot.TrimEnd("\", "/"),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "package.ps1 must run from the UNMA repository checkout."
    }

    $gitStatus = Get-GitOutput -Arguments @("status", "--porcelain")
    if (-not [string]::IsNullOrWhiteSpace($gitStatus)) {
        throw (
            "Refusing to package a dirty working tree. Commit or stash " +
            "the changes, or use -SkipGitChecks only for local diagnostics.")
    }

    $headRevision = Get-GitOutput -Arguments @("rev-parse", "HEAD")
    $productVersion = [string]$assemblyVersionInfo.ProductVersion
    $revisionSeparator = $productVersion.IndexOf("+")
    if ($revisionSeparator -ge 0) {
        $assemblyRevision = $productVersion.Substring(
            $revisionSeparator + 1).Split(".")[0]
        if (-not [string]::Equals(
                $assemblyRevision,
                $headRevision,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw (
                "UNMA.dll was built from revision '$assemblyRevision', " +
                "but HEAD is '$headRevision'. Rebuild without deploying " +
                "before packaging.")
        }
    }
}

$packageFiles = New-Object 'System.Collections.Generic.List[object]'
$rootFiles = @(
    "manifest.json",
    "config.json",
    "changelog.txt",
    "readme.txt",
    "LICENSE",
    "README.md",
    "USER_GUIDE_EN.md",
    "USER_GUIDE_DE.md"
)
foreach ($rootFile in $rootFiles) {
    Add-PackageFile `
        -Files $packageFiles `
        -SourcePath (Join-Path $repositoryRoot $rootFile) `
        -ArchivePath "UNMA/$rootFile"
}
Add-PackageFile `
    -Files $packageFiles `
    -SourcePath $resolvedAssemblyPath `
    -ArchivePath "UNMA/UNMA.dll"

$languageDirectory = Join-Path $repositoryRoot "lang"
foreach ($languageFile in Get-ChildItem `
        -LiteralPath $languageDirectory `
        -Filter "*.json" `
        -File | Sort-Object Name) {
    Add-PackageFile `
        -Files $packageFiles `
        -SourcePath $languageFile.FullName `
        -ArchivePath "UNMA/lang/$($languageFile.Name)"
}

$soundsDirectory = Join-Path $repositoryRoot "Sounds"
foreach ($soundFile in Get-ChildItem `
        -LiteralPath $soundsDirectory `
        -File `
        -Recurse | Sort-Object FullName) {
    $relativeSoundPath = $soundFile.FullName.Substring(
        $soundsDirectory.Length).TrimStart("\", "/").Replace("\", "/")
    Add-PackageFile `
        -Files $packageFiles `
        -SourcePath $soundFile.FullName `
        -ArchivePath "UNMA/Sounds/$relativeSoundPath"
}

$duplicates = @($packageFiles |
    Group-Object { $_.ArchivePath.ToUpperInvariant() } |
    Where-Object Count -gt 1)
if ($duplicates.Count -ne 0) {
    throw "Duplicate package entries were detected."
}
$orderedPackageFiles = @($packageFiles | Sort-Object ArchivePath)

$resolvedOutputDirectory = Get-CanonicalPath `
    -Path $OutputDirectory `
    -BasePath $repositoryRoot
[void][IO.Directory]::CreateDirectory($resolvedOutputDirectory)
$packageName = "UNMA-$Version-COI.zip"
$outputPath = Join-Path $resolvedOutputDirectory $packageName
if ((Test-Path -LiteralPath $outputPath) -and -not $Force) {
    throw (
        "Package already exists: $outputPath. " +
        "Pass -Force only when replacing this exact version intentionally.")
}

$temporaryPath = Join-Path `
    $resolvedOutputDirectory `
    (".$packageName." + [guid]::NewGuid().ToString("N") + ".tmp")
$fixedTimestamp = [DateTimeOffset]::new(
    2000,
    1,
    1,
    0,
    0,
    0,
    [TimeSpan]::Zero)

Add-Type -AssemblyName System.IO.Compression
try {
    $temporaryStream = $null
    $archive = $null
    try {
        $temporaryStream = [IO.File]::Open(
            $temporaryPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
        $archive = New-Object IO.Compression.ZipArchive(
            $temporaryStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $true)

        foreach ($packageFile in $orderedPackageFiles) {
            $entry = $archive.CreateEntry(
                $packageFile.ArchivePath,
                [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $entry.ExternalAttributes = 0

            $sourceStream = [IO.File]::OpenRead($packageFile.SourcePath)
            $entryStream = $entry.Open()
            try {
                $sourceStream.CopyTo($entryStream)
            } finally {
                $entryStream.Dispose()
                $sourceStream.Dispose()
            }
        }
    } finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
        if ($null -ne $temporaryStream) {
            $temporaryStream.Dispose()
        }
    }

    $readStream = [IO.File]::OpenRead($temporaryPath)
    $readArchive = New-Object IO.Compression.ZipArchive(
        $readStream,
        [IO.Compression.ZipArchiveMode]::Read,
        $false)
    try {
        if ($readArchive.Entries.Count -ne $orderedPackageFiles.Count) {
            throw "The completed package has an unexpected entry count."
        }
        foreach ($entry in $readArchive.Entries) {
            if ($entry.FullName.Contains("\")) {
                throw "Package entry contains a backslash: $($entry.FullName)"
            }
            if ($entry.LastWriteTime.DateTime -ne $fixedTimestamp.DateTime) {
                throw "Package entry has a non-deterministic timestamp."
            }
        }
    } finally {
        $readArchive.Dispose()
        $readStream.Dispose()
    }

    if (Test-Path -LiteralPath $outputPath) {
        $replacementBackupPath = Join-Path `
            $resolvedOutputDirectory `
            (".$packageName." + [guid]::NewGuid().ToString("N") + ".bak")
        try {
            [IO.File]::Replace(
                $temporaryPath,
                $outputPath,
                $replacementBackupPath,
                $true)
        } catch {
            if (-not (Test-Path -LiteralPath $outputPath) -and
                (Test-Path -LiteralPath $replacementBackupPath -PathType Leaf)) {
                [IO.File]::Move($replacementBackupPath, $outputPath)
            }
            throw
        }
        if (Test-Path -LiteralPath $replacementBackupPath -PathType Leaf) {
            [IO.File]::Delete($replacementBackupPath)
        }
    } else {
        [IO.File]::Move($temporaryPath, $outputPath)
    }
} finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
        [IO.File]::Delete($temporaryPath)
    }
}

$packageHash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath
$packageInfo = Get-Item -LiteralPath $outputPath
Write-Output "PACKAGE_PATH=$($packageInfo.FullName)"
Write-Output "PACKAGE_SHA256=$($packageHash.Hash)"
Write-Output "PACKAGE_SIZE=$($packageInfo.Length)"
Write-Output "PACKAGE_ENTRIES=$($orderedPackageFiles.Count)"
