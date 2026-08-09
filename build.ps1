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

if ($Configuration -eq "Release") {
    $deployedAssemblyPath = Join-Path $PSScriptRoot "UNMA.dll"
    $managedPath = Join-Path $GameRoot "Captain of Industry_Data\Managed"
    $saveEventVerificationPath = Join-Path `
        $PSScriptRoot `
        "tests\verify-save-safe-events.ps1"
    $windowsPowerShell = Join-Path `
        $env:SystemRoot `
        "System32\WindowsPowerShell\v1.0\powershell.exe"

    # Keep reflected game assemblies isolated from an interactive build shell
    # so all file handles are released immediately after verification.
    & $windowsPowerShell `
        -NoProfile `
        -NonInteractive `
        -ExecutionPolicy Bypass `
        -File $saveEventVerificationPath `
        -AssemblyPath $deployedAssemblyPath `
        -ManagedPath $managedPath
    if ($LASTEXITCODE -ne 0) {
        throw "UNMA-Save-Event-Prüfung fehlgeschlagen " +
            "(Exitcode $LASTEXITCODE)."
    }
}

Write-Host "UNMA-Build erfolgreich: $(Join-Path $PSScriptRoot 'UNMA.dll')"
