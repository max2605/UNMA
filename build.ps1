param(
    [string]$Configuration = "Release",
    [string]$GameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry",
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "source\UNMA.csproj"
if (-not (Test-Path -LiteralPath $GameRoot)) {
    throw "Captain of Industry wurde nicht gefunden: $GameRoot"
}

$deployValue = if ($Deploy) { "true" } else { "false" }
dotnet build $projectPath -c $Configuration "/p:COI_ROOT=$GameRoot" "/p:DeployToModRoot=$deployValue"

if ($LASTEXITCODE -ne 0) {
    throw "UNMA-Build fehlgeschlagen (Exitcode $LASTEXITCODE)."
}

if ($Configuration -eq "Release") {
    $builtAssemblyPath = Join-Path `
        $PSScriptRoot `
        "source\bin\Release\UNMA.dll"
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
        -AssemblyPath $builtAssemblyPath `
        -ManagedPath $managedPath
    if ($LASTEXITCODE -ne 0) {
        throw "UNMA-Save-Event-Prüfung fehlgeschlagen " +
            "(Exitcode $LASTEXITCODE)."
    }
}

$outputAssembly = if ($Deploy) {
    Join-Path $PSScriptRoot "UNMA.dll"
} else {
    Join-Path $PSScriptRoot "source\bin\$Configuration\UNMA.dll"
}
Write-Host "UNMA-Build erfolgreich: $outputAssembly"
