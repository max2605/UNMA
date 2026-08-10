param(
    [string]$LanguageDirectory = (Join-Path $PSScriptRoot "..\lang")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedDirectory = (Resolve-Path -LiteralPath $LanguageDirectory).Path
$referencePath = Join-Path $resolvedDirectory "en.json"
$reference = Get-Content -Raw -Encoding UTF8 -LiteralPath $referencePath |
    ConvertFrom-Json
$referenceProperties = @($reference.PSObject.Properties)
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)

foreach ($file in Get-ChildItem -LiteralPath $resolvedDirectory -Filter *.json) {
    if ($file.Name -eq "en.json") {
        continue
    }

    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName |
        ConvertFrom-Json
    $knownKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($property in $catalog.PSObject.Properties) {
        [void]$knownKeys.Add($property.Name)
    }
    $missing = @($referenceProperties | Where-Object {
        -not $knownKeys.Contains($_.Name)
    })
    if ($missing.Count -eq 0) {
        continue
    }

    $raw = [IO.File]::ReadAllText($file.FullName)
    $newLine = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
    $trimmed = $raw.TrimEnd()
    if (-not $trimmed.EndsWith("}")) {
        throw "Language file does not end with a JSON object: $($file.FullName)"
    }
    $prefix = $trimmed.Substring(0, $trimmed.Length - 1).TrimEnd()
    $hasEntries = -not $prefix.TrimEnd().EndsWith("{")
    $lines = foreach ($property in $missing) {
        $encodedKey = ConvertTo-Json -Compress -InputObject $property.Name
        $encodedValue = ConvertTo-Json -Compress -InputObject $property.Value
        "    ${encodedKey}: ${encodedValue}"
    }
    $separator = if ($hasEntries) { "," } else { "" }
    $updated = $prefix + $separator + $newLine +
        ($lines -join ("," + $newLine)) + $newLine + "}" + $newLine
    [IO.File]::WriteAllText($file.FullName, $updated, $utf8WithoutBom)
    Write-Output "$($file.Name): added $($missing.Count) English fallback keys."
}
