param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$PackagePath,

    [string]$AssemblyPath,

    [switch]$SkipGitRevisionCheck
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path `
        $repositoryRoot `
        "dist\UNMA-$Version-COI.zip"
}
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path `
        $repositoryRoot `
        "source\bin\Release\UNMA.dll"
}

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

function Assert-Matches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not [Text.RegularExpressions.Regex]::IsMatch(
            $Text,
            $Pattern,
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw "$Description does not declare version $Version."
    }
}

function Get-StreamSha256 {
    param([Parameter(Mandatory = $true)][IO.Stream]$Stream)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha256.ComputeHash($Stream))).Replace("-", "")
    } finally {
        $sha256.Dispose()
    }
}

function Add-ExpectedFile {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.Dictionary[string, string]]$Files,

        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$ArchivePath
    )

    $resolvedSourcePath = Get-CanonicalPath `
        -Path $SourcePath `
        -BasePath $repositoryRoot
    if (-not (Test-Path -LiteralPath $resolvedSourcePath -PathType Leaf)) {
        throw "Expected package source was not found: $resolvedSourcePath"
    }
    $normalizedArchivePath = $ArchivePath.Replace("\", "/")
    if ($Files.ContainsKey($normalizedArchivePath)) {
        throw "Duplicate expected package entry: $normalizedArchivePath"
    }
    $Files.Add($normalizedArchivePath, $resolvedSourcePath)
}

$resolvedPackagePath = Get-CanonicalPath `
    -Path $PackagePath `
    -BasePath $repositoryRoot
$resolvedAssemblyPath = Get-CanonicalPath `
    -Path $AssemblyPath `
    -BasePath $repositoryRoot
$expectedPackageName = "UNMA-$Version-COI.zip"
Assert-EqualText `
    -Actual ([IO.Path]::GetFileName($resolvedPackagePath)) `
    -Expected $expectedPackageName `
    -Description "Package filename"
if (-not (Test-Path -LiteralPath $resolvedPackagePath -PathType Leaf)) {
    throw "Release package was not found: $resolvedPackagePath"
}
if (-not (Test-Path -LiteralPath $resolvedAssemblyPath -PathType Leaf)) {
    throw "Release assembly was not found: $resolvedAssemblyPath"
}

$manifestPath = Join-Path $repositoryRoot "manifest.json"
$manifestText = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath $manifestPath
$manifest = $manifestText | ConvertFrom-Json
Assert-EqualText `
    -Actual ([string]$manifest.version) `
    -Expected $Version `
    -Description "manifest.json version"
if (-not (@($manifest.primary_dlls) -contains "UNMA.dll")) {
    throw "manifest.json does not declare UNMA.dll as a primary DLL."
}

[xml]$project = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "source\UNMA.csproj")
$projectVersionNode = $project.SelectSingleNode(
    "/Project/PropertyGroup/Version")
$projectFileVersionNode = $project.SelectSingleNode(
    "/Project/PropertyGroup/FileVersion")
$projectAssemblyVersionNode = $project.SelectSingleNode(
    "/Project/PropertyGroup/AssemblyVersion")
if ($null -eq $projectVersionNode -or
    $null -eq $projectFileVersionNode -or
    $null -eq $projectAssemblyVersionNode) {
    throw (
        "UNMA.csproj does not declare Version, FileVersion, and " +
        "AssemblyVersion.")
}
$projectVersion = [string]$projectVersionNode.InnerText
$projectFileVersion = [string]$projectFileVersionNode.InnerText
$projectAssemblyVersion = [string]$projectAssemblyVersionNode.InnerText
Assert-EqualText `
    -Actual $projectVersion `
    -Expected $Version `
    -Description "UNMA.csproj Version"
Assert-EqualText `
    -Actual $projectFileVersion `
    -Expected "$Version.0" `
    -Description "UNMA.csproj FileVersion"

$readmeText = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "readme.txt")
Assert-Matches `
    -Text $readmeText `
    -Pattern ("(?m)^Version:\s*" + [Regex]::Escape($Version) + "\s*$") `
    -Description "readme.txt"

$changeLogText = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "CHANGELOG.md")
Assert-Matches `
    -Text $changeLogText `
    -Pattern (
        "\A(?:\uFEFF)?#\s+Changelog\s+##\s+" +
        [Regex]::Escape($Version) + "(?:\s|$)") `
    -Description "CHANGELOG.md first entry"

$modHubChangeLogText = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "changelog.txt")
Assert-Matches `
    -Text $modHubChangeLogText `
    -Pattern ("\A(?:\uFEFF)?v" + [Regex]::Escape($Version) + "(?:\s|\|)") `
    -Description "changelog.txt first entry"

$guideEnglishText = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "USER_GUIDE_EN.md")
Assert-Matches `
    -Text $guideEnglishText `
    -Pattern ("\A[\s\S]{0,500}UNMA\s+" + [Regex]::Escape($Version)) `
    -Description "USER_GUIDE_EN.md header"

$guideGermanText = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "USER_GUIDE_DE.md")
Assert-Matches `
    -Text $guideGermanText `
    -Pattern ("\A[\s\S]{0,500}UNMA\s+" + [Regex]::Escape($Version)) `
    -Description "USER_GUIDE_DE.md header"

$announcementText = Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "ANNOUNCEMENT_EN.md")
Assert-Matches `
    -Text $announcementText `
    -Pattern ("\A[\s\S]{0,250}v?" + [Regex]::Escape($Version)) `
    -Description "ANNOUNCEMENT_EN.md heading"

$assemblyVersionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo(
    $resolvedAssemblyPath)
Assert-EqualText `
    -Actual ([string]$assemblyVersionInfo.FileVersion) `
    -Expected "$Version.0" `
    -Description "UNMA.dll FileVersion"
if (-not ([string]$assemblyVersionInfo.ProductVersion).StartsWith(
        $Version,
        [StringComparison]::Ordinal)) {
    throw "UNMA.dll ProductVersion does not start with $Version."
}
$binaryAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName(
    $resolvedAssemblyPath).Version.ToString()
Assert-EqualText `
    -Actual $binaryAssemblyVersion `
    -Expected $projectAssemblyVersion `
    -Description "UNMA.dll AssemblyVersion"

if (-not $SkipGitRevisionCheck) {
    $headRevision = (& git -C $repositoryRoot rev-parse HEAD 2>&1 |
        Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read the Git HEAD revision: $headRevision"
    }
    $productVersion = [string]$assemblyVersionInfo.ProductVersion
    $revisionSeparator = $productVersion.IndexOf("+")
    if ($revisionSeparator -ge 0) {
        $assemblyRevision = $productVersion.Substring(
            $revisionSeparator + 1).Split(".")[0]
        Assert-EqualText `
            -Actual $assemblyRevision `
            -Expected $headRevision `
            -Description "UNMA.dll source revision"
    }
}

$expectedFiles = New-Object `
    'System.Collections.Generic.Dictionary[string,string]' `
    ([StringComparer]::OrdinalIgnoreCase)
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
    Add-ExpectedFile `
        -Files $expectedFiles `
        -SourcePath (Join-Path $repositoryRoot $rootFile) `
        -ArchivePath "UNMA/$rootFile"
}
Add-ExpectedFile `
    -Files $expectedFiles `
    -SourcePath $resolvedAssemblyPath `
    -ArchivePath "UNMA/UNMA.dll"

$languageDirectory = Join-Path $repositoryRoot "lang"
foreach ($languageFile in Get-ChildItem `
        -LiteralPath $languageDirectory `
        -Filter "*.json" `
        -File | Sort-Object Name) {
    [void](Get-Content `
        -Raw `
        -Encoding UTF8 `
        -LiteralPath $languageFile.FullName | ConvertFrom-Json)
    Add-ExpectedFile `
        -Files $expectedFiles `
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
    Add-ExpectedFile `
        -Files $expectedFiles `
        -SourcePath $soundFile.FullName `
        -ArchivePath "UNMA/Sounds/$relativeSoundPath"
}

[void](Get-Content `
    -Raw `
    -Encoding UTF8 `
    -LiteralPath (Join-Path $repositoryRoot "config.json") |
    ConvertFrom-Json)

Add-Type -AssemblyName System.IO.Compression
$fixedTimestamp = [DateTime]::new(2000, 1, 1, 0, 0, 0)
$packageStream = $null
$archive = $null
try {
    $packageStream = [IO.File]::OpenRead($resolvedPackagePath)
    $archive = New-Object IO.Compression.ZipArchive(
        $packageStream,
        [IO.Compression.ZipArchiveMode]::Read,
        $false)
    if ($archive.Entries.Count -ne $expectedFiles.Count) {
        throw (
            "Package contains $($archive.Entries.Count) entries; expected " +
            "$($expectedFiles.Count).")
    }

    $seenEntries = New-Object `
        'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $entryPath = $entry.FullName
        if ($entryPath.Contains("\")) {
            throw "Package entry contains a backslash: $entryPath"
        }
        if (-not $entryPath.StartsWith(
                "UNMA/",
                [StringComparison]::Ordinal)) {
            throw "Package entry is outside the UNMA folder: $entryPath"
        }
        if ($entryPath.StartsWith("/", [StringComparison]::Ordinal) -or
            $entryPath.Contains("../") -or
            $entryPath.EndsWith("/", [StringComparison]::Ordinal)) {
            throw "Package entry path is unsafe or non-canonical: $entryPath"
        }
        if (-not $seenEntries.Add($entryPath)) {
            throw "Package contains a duplicate entry: $entryPath"
        }
        if (-not $expectedFiles.ContainsKey($entryPath)) {
            throw "Package contains an unexpected entry: $entryPath"
        }
        if ($entry.LastWriteTime.DateTime -ne $fixedTimestamp) {
            throw "Package entry has a non-deterministic timestamp: $entryPath"
        }

        $sourceHash = (Get-FileHash `
            -Algorithm SHA256 `
            -LiteralPath $expectedFiles[$entryPath]).Hash
        $entryStream = $entry.Open()
        try {
            $entryHash = Get-StreamSha256 -Stream $entryStream
        } finally {
            $entryStream.Dispose()
        }
        Assert-EqualText `
            -Actual $entryHash `
            -Expected $sourceHash `
            -Description "SHA256 for $entryPath"
    }

    foreach ($expectedEntryPath in $expectedFiles.Keys) {
        if (-not $seenEntries.Contains($expectedEntryPath)) {
            throw "Package is missing an entry: $expectedEntryPath"
        }
    }
} finally {
    if ($null -ne $archive) {
        $archive.Dispose()
    }
    if ($null -ne $packageStream) {
        $packageStream.Dispose()
    }
}

$packageHash = Get-FileHash `
    -Algorithm SHA256 `
    -LiteralPath $resolvedPackagePath
$packageInfo = Get-Item -LiteralPath $resolvedPackagePath
Write-Output "RELEASE_VERIFICATION=PASSED"
Write-Output "RELEASE_VERSION=$Version"
Write-Output "PACKAGE_PATH=$($packageInfo.FullName)"
Write-Output "PACKAGE_SHA256=$($packageHash.Hash)"
Write-Output "PACKAGE_SIZE=$($packageInfo.Length)"
Write-Output "PACKAGE_ENTRIES=$($expectedFiles.Count)"
