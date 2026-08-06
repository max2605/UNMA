param(
    [string]$Configuration = "Release",
    [string]$GameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry"
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "source\UNMA.csproj"
if (-not (Test-Path -LiteralPath $GameRoot)) {
    throw "Captain of Industry wurde nicht gefunden: $GameRoot"
}

dotnet build $projectPath -c $Configuration "/p:COI_ROOT=$GameRoot"

if ($LASTEXITCODE -ne 0) {
    throw "UNMA-Build fehlgeschlagen (Exitcode $LASTEXITCODE)."
}

Write-Host "UNMA-Build erfolgreich: $(Join-Path $PSScriptRoot 'UNMA.dll')"
