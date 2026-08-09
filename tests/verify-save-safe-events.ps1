param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,

    [Parameter(Mandatory = $true)]
    [string]$ManagedPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$runtimeTypeName = "UNMA.Runtime.UnmaRuntime"
$entityTypeName = "Mafi.Core.Entities.IEntity"
$entitiesManagerTypeName = "Mafi.Core.Entities.IEntitiesManager"
$nonSaveableEventTypeName = "Mafi.IEventNonSaveable``1"

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw "UNMA save-event IL regression failed: $Message"
    }
}

function Get-GenericTypeDefinitionName {
    param([Type]$Type)

    if ($null -eq $Type) {
        return $null
    }
    if ($Type.IsGenericType) {
        return $Type.GetGenericTypeDefinition().FullName
    }
    return $Type.FullName
}

function Get-FirstGenericArgumentName {
    param([System.Reflection.MethodBase]$Method)

    if ($null -eq $Method -or -not $Method.IsGenericMethod) {
        return $null
    }
    $arguments = @($Method.GetGenericArguments())
    if ($arguments.Count -eq 0) {
        return $null
    }
    return $arguments[0].FullName
}

function Test-SameField {
    param(
        [System.Reflection.FieldInfo]$Left,
        [System.Reflection.FieldInfo]$Right
    )

    return $null -ne $Left -and
        $null -ne $Right -and
        $Left.MetadataToken -eq $Right.MetadataToken -and
        $Left.Module.ModuleVersionId -eq $Right.Module.ModuleVersionId
}

function Get-OperandSize {
    param(
        [System.Reflection.Emit.OpCode]$OpCode,
        [byte[]]$Bytes,
        [int]$OperandOffset
    )

    switch ($OpCode.OperandType.ToString()) {
        "InlineNone" { return 0 }
        "ShortInlineBrTarget" { return 1 }
        "ShortInlineI" { return 1 }
        "ShortInlineVar" { return 1 }
        "InlineVar" { return 2 }
        "InlineBrTarget" { return 4 }
        "InlineField" { return 4 }
        "InlineI" { return 4 }
        "InlineMethod" { return 4 }
        "InlineSig" { return 4 }
        "InlineString" { return 4 }
        "InlineTok" { return 4 }
        "InlineType" { return 4 }
        "ShortInlineR" { return 4 }
        "InlineI8" { return 8 }
        "InlineR" { return 8 }
        "InlineSwitch" {
            Assert-Condition `
                ($OperandOffset + 4 -le $Bytes.Length) `
                "Truncated switch operand."
            $caseCount = [BitConverter]::ToInt32($Bytes, $OperandOffset)
            Assert-Condition ($caseCount -ge 0) "Invalid switch case count."
            return 4 + (4 * $caseCount)
        }
        default {
            throw "Unsupported IL operand type '$($OpCode.OperandType)'."
        }
    }
}

$script:opCodesByValue = @{}
foreach ($field in [System.Reflection.Emit.OpCodes].GetFields(
        [System.Reflection.BindingFlags]::Public -bor
        [System.Reflection.BindingFlags]::Static)) {
    if ($field.FieldType -ne [System.Reflection.Emit.OpCode]) {
        continue
    }
    $opCode = [System.Reflection.Emit.OpCode]$field.GetValue($null)
    $key = [int]$opCode.Value
    if ($key -lt 0) {
        $key += 65536
    }
    $script:opCodesByValue[$key] = $opCode
}

function Read-MethodInstructions {
    param([System.Reflection.MethodBase]$Method)

    $body = $Method.GetMethodBody()
    if ($null -eq $body) {
        return @()
    }

    [byte[]]$bytes = $body.GetILAsByteArray()
    [Type[]]$typeArguments = @()
    [Type[]]$methodArguments = @()
    if ($Method.DeclaringType.IsGenericType) {
        $typeArguments = $Method.DeclaringType.GetGenericArguments()
    }
    if ($Method.IsGenericMethod) {
        $methodArguments = $Method.GetGenericArguments()
    }

    $instructions = New-Object System.Collections.Generic.List[object]
    $offset = 0
    while ($offset -lt $bytes.Length) {
        $instructionOffset = $offset
        $firstByte = [int]$bytes[$offset]
        $offset++
        if ($firstByte -eq 0xFE) {
            Assert-Condition `
                ($offset -lt $bytes.Length) `
                "Truncated two-byte opcode in $($Method.Name)."
            $opCodeKey = 0xFE00 -bor [int]$bytes[$offset]
            $offset++
        } else {
            $opCodeKey = $firstByte
        }

        Assert-Condition `
            $script:opCodesByValue.ContainsKey($opCodeKey) `
            ("Unknown IL opcode 0x{0:X4} in {1}." -f
                $opCodeKey,
                $Method.Name)
        $opCode = $script:opCodesByValue[$opCodeKey]
        $operandOffset = $offset
        $operandSize = Get-OperandSize $opCode $bytes $operandOffset
        Assert-Condition `
            ($operandOffset + $operandSize -le $bytes.Length) `
            "Truncated operand in $($Method.Name)."

        $operand = $null
        if ($opCode.OperandType -eq
            [System.Reflection.Emit.OperandType]::InlineMethod) {
            $token = [BitConverter]::ToInt32($bytes, $operandOffset)
            try {
                $operand = $Method.Module.ResolveMethod(
                    $token,
                    $typeArguments,
                    $methodArguments)
            } catch [TypeLoadException] {
                # An unrelated optional dependency may not be loadable. The
                # Mafi event methods asserted below resolve normally.
            }
        } elseif ($opCode.OperandType -eq
            [System.Reflection.Emit.OperandType]::InlineField) {
            $token = [BitConverter]::ToInt32($bytes, $operandOffset)
            try {
                $operand = $Method.Module.ResolveField(
                    $token,
                    $typeArguments,
                    $methodArguments)
            } catch [TypeLoadException] {
                # See the ResolveMethod note above.
            }
        }

        $instructions.Add([pscustomobject]@{
            Offset = $instructionOffset
            OpCode = $opCode
            Operand = $operand
        })
        $offset += $operandSize
    }

    return $instructions.ToArray()
}

function Test-IsMethodInstruction {
    param(
        $Instruction,
        [string]$DeclaringTypeName,
        [string]$MethodName
    )

    return $null -ne $Instruction -and
        $Instruction.Operand -is [System.Reflection.MethodBase] -and
        $Instruction.Operand.DeclaringType.FullName -eq
            $DeclaringTypeName -and
        $Instruction.Operand.Name -eq $MethodName
}

function Test-IsRuntimeOwnedEventCall {
    param(
        $Instruction,
        [string[]]$MethodNames
    )

    if ($null -eq $Instruction -or
        $Instruction.Operand -isnot [System.Reflection.MethodBase]) {
        return $false
    }
    $method = [System.Reflection.MethodBase]$Instruction.Operand
    $declaringName = Get-GenericTypeDefinitionName $method.DeclaringType
    return $MethodNames -contains $method.Name -and
        ($declaringName -like "Mafi.IEvent*" -or
            $declaringName -like "Mafi.Event*") -and
        (Get-FirstGenericArgumentName $method) -eq $runtimeTypeName
}

function Assert-EntitySubscription {
    param(
        [System.Reflection.MethodBase]$Method,
        [System.Reflection.FieldInfo]$EventField,
        [string]$RequiredOperation,
        [string]$ForbiddenOperation
    )

    $instructions = @(Read-MethodInstructions $Method)
    $fieldIndexes = @()
    for ($index = 0; $index -lt $instructions.Count; $index++) {
        $instruction = $instructions[$index]
        if ($instruction.OpCode.Name -eq "ldfld" -and
            $instruction.Operand -is [System.Reflection.FieldInfo] -and
            (Test-SameField $instruction.Operand $EventField)) {
            $fieldIndexes += $index
        }
    }
    Assert-Condition `
        ($fieldIndexes.Count -eq 1) `
        "$($Method.Name) must load the EntityRemoved event field exactly once."

    $fieldIndex = $fieldIndexes[0]
    $operationIndex = -1
    for ($index = $fieldIndex + 1;
        $index -lt $instructions.Count;
        $index++) {
        if (Test-IsRuntimeOwnedEventCall `
                $instructions[$index] `
                @($RequiredOperation, $ForbiddenOperation)) {
            $operationIndex = $index
            break
        }
    }
    Assert-Condition `
        ($operationIndex -gt $fieldIndex) `
        "$($Method.Name) has no matching EntityRemoved event operation."

    $operation = [System.Reflection.MethodBase](
        $instructions[$operationIndex].Operand)
    Assert-Condition `
        ($operation.Name -eq $RequiredOperation) `
        ("{0} uses saveable {1}; expected {2}." -f
            $Method.Name,
            $ForbiddenOperation,
            $RequiredOperation)
    Assert-Condition `
        ((Get-GenericTypeDefinitionName $operation.DeclaringType) -eq
            $nonSaveableEventTypeName) `
        "$($Method.Name) must call $nonSaveableEventTypeName."

    $handlerCount = 0
    for ($index = $fieldIndex + 1;
        $index -lt $operationIndex;
        $index++) {
        $instruction = $instructions[$index]
        if (($instruction.OpCode.Name -eq "ldftn" -or
                $instruction.OpCode.Name -eq "ldvirtftn") -and
            (Test-IsMethodInstruction `
                $instruction `
                $runtimeTypeName `
                "OnEntityRemoved")) {
            $handlerCount++
        }
    }
    Assert-Condition `
        ($handlerCount -eq 1) `
        "$($Method.Name) must bind OnEntityRemoved exactly once."
}

$resolvedAssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$resolvedManagedPath = (Resolve-Path -LiteralPath $ManagedPath).Path

# Loading assemblies for reflection does not instantiate game objects or call
# their methods. ResolveMethod still needs referenced metadata to be present.
Get-ChildItem -LiteralPath $resolvedManagedPath -Filter "*.dll" -File |
    ForEach-Object {
        try {
            [void][System.Reflection.Assembly]::LoadFrom(
                $_.FullName)
        } catch {
            # Some native or incompatible support DLLs are not metadata
            # dependencies of the inspected calls and can safely be ignored.
        }
    }

$langLibPath = Join-Path `
    (Split-Path (Split-Path $resolvedAssemblyPath -Parent) -Parent) `
    "LangLib\LangLib.dll"
if (Test-Path -LiteralPath $langLibPath -PathType Leaf) {
    try {
        [void][System.Reflection.Assembly]::LoadFrom(
            $langLibPath)
    } catch {
        # LangLib is not part of the inspected event path.
    }
}

# Loading the bytes avoids LoadFrom's path cache. Re-running build.ps1 in the
# same PowerShell process therefore always inspects the newly deployed DLL.
$assembly = [System.Reflection.Assembly]::Load(
    [System.IO.File]::ReadAllBytes($resolvedAssemblyPath))
$runtimeType = $assembly.GetType($runtimeTypeName, $true, $false)
$bindingFlags = [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::Public -bor
    [System.Reflection.BindingFlags]::NonPublic -bor
    [System.Reflection.BindingFlags]::DeclaredOnly

$constructors = @($runtimeType.GetConstructors($bindingFlags))
$methods = @($runtimeType.GetMethods($bindingFlags))
$initialize = @($methods | Where-Object Name -eq "Initialize")
$dispose = @($methods | Where-Object Name -eq "Dispose")
Assert-Condition ($initialize.Count -eq 1) "Initialize was not found exactly once."
Assert-Condition ($dispose.Count -eq 1) "Dispose was not found exactly once."

$getterLocations = @()
foreach ($constructor in $constructors) {
    $instructions = @(Read-MethodInstructions $constructor)
    for ($index = 0; $index -lt $instructions.Count; $index++) {
        if (Test-IsMethodInstruction `
                $instructions[$index] `
                $entitiesManagerTypeName `
                "get_EntityRemoved") {
            $getterLocations += [pscustomobject]@{
                Constructor = $constructor
                Instructions = $instructions
                Index = $index
            }
        }
    }
}
Assert-Condition `
    ($getterLocations.Count -eq 1) `
    "EntityRemoved must be obtained exactly once in the runtime constructor."

$getterLocation = $getterLocations[0]
$eventField = $null
for ($index = $getterLocation.Index + 1;
    $index -lt $getterLocation.Instructions.Count;
    $index++) {
    $instruction = $getterLocation.Instructions[$index]
    if ($instruction.OpCode.Name -eq "stfld" -and
        $instruction.Operand -is [System.Reflection.FieldInfo]) {
        $eventField = [System.Reflection.FieldInfo]$instruction.Operand
        break
    }
    if ($instruction.Operand -is [System.Reflection.MethodBase]) {
        break
    }
}
Assert-Condition `
    ($null -ne $eventField) `
    "EntityRemoved is not stored in a dedicated non-saveable event field."
Assert-Condition `
    ((Get-GenericTypeDefinitionName $eventField.FieldType) -eq
        $nonSaveableEventTypeName) `
    "The EntityRemoved field is not typed as $nonSaveableEventTypeName."
$eventTypeArguments = @($eventField.FieldType.GetGenericArguments())
Assert-Condition `
    ($eventTypeArguments.Count -eq 1 -and
        $eventTypeArguments[0].FullName -eq $entityTypeName) `
    "The EntityRemoved event field has the wrong payload type."

Assert-EntitySubscription `
    $initialize[0] `
    $eventField `
    "AddNonSaveable" `
    "Add"
Assert-EntitySubscription `
    $dispose[0] `
    $eventField `
    "RemoveNonSaveable" `
    "Remove"

foreach ($method in @($constructors + $methods)) {
    foreach ($instruction in @(Read-MethodInstructions $method)) {
        if (Test-IsRuntimeOwnedEventCall $instruction @("Add", "Remove")) {
            $calledMethod = [System.Reflection.MethodBase]$instruction.Operand
            throw ("UNMA save-event IL regression failed: " +
                "$runtimeTypeName.$($method.Name) calls saveable " +
                "$($calledMethod.DeclaringType.FullName)." +
                "$($calledMethod.Name)<$runtimeTypeName>.")
        }
    }
}

Write-Host (
    "UNMA save-event IL regression passed: EntityRemoved uses " +
    "AddNonSaveable/RemoveNonSaveable and no saveable runtime-owner " +
    "event registration exists.")
