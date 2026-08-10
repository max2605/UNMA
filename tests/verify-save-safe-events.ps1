param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,

    [Parameter(Mandatory = $true)]
    [string]$ManagedPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$runtimeTypeName = "UNMA.Runtime.UnmaRuntime"
$configurationTypeName = "UNMA.Domain.UnmaConfiguration"
$timingMemoryPolicyTypeName = "UNMA.Domain.AlarmTimingMemoryPolicy"
$escalationPolicyTypeName = "UNMA.Domain.AlarmEscalationPolicy"
$attentionQueuePolicyTypeName = "UNMA.Domain.AlarmAttentionQueuePolicy"
$attentionRequestTypeName = "UNMA.Domain.AlarmAttentionRequest"
$forecastPolicyTypeName = "UNMA.Domain.InstrumentForecastPolicy"
$forecastResultTypeName = "UNMA.Domain.InstrumentForecastResult"
$alarmAreaPolicyTypeName = "UNMA.Domain.AlarmAreaPolicy"
$alarmAreaDefinitionTypeName = "UNMA.Domain.AlarmAreaDefinition"
$alarmAreaFilterTypeName = "UNMA.Domain.AlarmAreaFilter"
$alarmAreaFilterKindTypeName = "UNMA.Domain.AlarmAreaFilterKind"
$alarmViewTypeName = "UNMA.Domain.AlarmView"
$alarmHistoryTypeName = "UNMA.Domain.AlarmHistoryDefinition"
$alarmIncidentPolicyTypeName = "UNMA.Domain.AlarmIncidentPolicy"
$alarmIncidentActiveSampleTypeName =
    "UNMA.Domain.AlarmIncidentActiveSample"
$alarmOccurrenceSignalTypeName = "UNMA.Domain.AlarmOccurrenceSignal"
$alarmIncidentSnapshotTypeName = "UNMA.Domain.AlarmIncidentSnapshot"
$panelProjectionTypeName = "UNMA.Domain.PanelSlotProjection"
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

$multiLangLibPath = Join-Path `
    (Split-Path (Split-Path $resolvedAssemblyPath -Parent) -Parent) `
    "MultiLangLib\MultiLangLib.dll"
if (Test-Path -LiteralPath $multiLangLibPath -PathType Leaf) {
    try {
        [void][System.Reflection.Assembly]::LoadFrom(
            $multiLangLibPath)
    } catch {
        # MultiLangLib is not part of the inspected event path.
    }
}

# Loading the bytes avoids LoadFrom's path cache. Re-running build.ps1 in the
# same PowerShell process therefore always inspects the newly deployed DLL.
$assembly = [System.Reflection.Assembly]::Load(
    [System.IO.File]::ReadAllBytes($resolvedAssemblyPath))
$runtimeType = $assembly.GetType($runtimeTypeName, $true, $false)
$configurationType = $assembly.GetType(
    $configurationTypeName,
    $true,
    $false)
$attentionRequestType = $assembly.GetType(
    $attentionRequestTypeName,
    $true,
    $false)
$forecastResultType = $assembly.GetType(
    $forecastResultTypeName,
    $true,
    $false)
$alarmAreaDefinitionType = $assembly.GetType(
    $alarmAreaDefinitionTypeName,
    $true,
    $false)
$alarmAreaFilterType = $assembly.GetType(
    $alarmAreaFilterTypeName,
    $true,
    $false)
$alarmAreaFilterKindType = $assembly.GetType(
    $alarmAreaFilterKindTypeName,
    $true,
    $false)
$alarmViewType = $assembly.GetType(
    $alarmViewTypeName,
    $true,
    $false)
$alarmHistoryType = $assembly.GetType(
    $alarmHistoryTypeName,
    $true,
    $false)
$alarmIncidentActiveSampleType = $assembly.GetType(
    $alarmIncidentActiveSampleTypeName,
    $true,
    $false)
$alarmOccurrenceSignalType = $assembly.GetType(
    $alarmOccurrenceSignalTypeName,
    $true,
    $false)
$alarmIncidentSnapshotType = $assembly.GetType(
    $alarmIncidentSnapshotTypeName,
    $true,
    $false)
$alarmIncidentPolicyType = $assembly.GetType(
    $alarmIncidentPolicyTypeName,
    $true,
    $false)
$bindingFlags = [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::Public -bor
    [System.Reflection.BindingFlags]::NonPublic -bor
    [System.Reflection.BindingFlags]::DeclaredOnly

$constructors = @($runtimeType.GetConstructors($bindingFlags))
$methods = @($runtimeType.GetMethods($bindingFlags))
$initialize = @($methods | Where-Object Name -eq "Initialize")
$dispose = @($methods | Where-Object Name -eq "Dispose")
$restoreConfiguration = @(
    $methods | Where-Object Name -eq "RestoreConfiguration")
$restoreAlarmTimingStates = @(
    $methods | Where-Object Name -eq "RestoreAlarmTimingStates")
$advanceRuleTiming = @(
    $methods | Where-Object Name -eq "AdvanceRuleTiming")
$setAlarm = @($methods | Where-Object Name -eq "SetAlarm")
$tryTakeAttentionRequest = @(
    $methods | Where-Object Name -eq "TryTakeAttentionRequest")
$shouldEnqueueAttention = @(
    $methods | Where-Object Name -eq "ShouldEnqueueAttentionRequest")
$tryGetInstrumentForecast = @(
    $methods | Where-Object Name -eq "TryGetInstrumentForecast")
$captureInstrumentValues = @(
    $methods | Where-Object Name -eq "CaptureInstrumentValues")
$forecastWindowHelper = @(
    $methods | Where-Object Name -eq "IsInstrumentForecastSampleInWindow")
$instrumentClockRollbackHelper = @(
    $methods | Where-Object Name -eq "DidInstrumentClockRollBack")
$replaceAlarmAreas = @(
    $methods | Where-Object Name -eq "ReplaceAlarmAreas")
$updatePanelSettings = @(
    $methods | Where-Object Name -eq "UpdatePanelSettings")
$tryGetDashboardViews = @(
    $methods | Where-Object Name -eq "TryGetDashboardViews")
$tryGetAlarmIncidentSnapshot = @(
    $methods | Where-Object Name -eq "TryGetAlarmIncidentSnapshot")
$createAlarmIncidentActiveSample = @(
    $methods | Where-Object Name -eq "CreateAlarmIncidentActiveSample")
$getAlarmIncidentHistoryCapture = @(
    $methods |
        Where-Object Name -eq "GetAlarmIncidentHistoryCapture")
$buildAlarmIncidentHistoryCapture = @(
    $methods |
        Where-Object Name -eq "BuildAlarmIncidentHistoryCapture")
$tryCaptureAlarmAreaProjection = @(
    $methods | Where-Object Name -eq "TryCaptureAlarmAreaProjection")
$tryAcknowledgeDashboard = @(
    $methods | Where-Object Name -eq "TryAcknowledgeDashboard")
$tryGetNextDashboardUnacknowledged = @(
    $methods | Where-Object Name -eq "TryGetNextDashboardUnacknowledged")
$projectActiveDashboardArea = @(
    $methods | Where-Object Name -eq "ProjectActiveDashboardArea")
$canAcknowledgeFilteredDashboardAlarm = @(
    $methods | Where-Object Name -eq "CanAcknowledgeFilteredDashboardAlarm")
$isExactAlarmAreaFilter = @(
    $methods | Where-Object Name -eq "IsExactAlarmAreaFilter")
Assert-Condition ($initialize.Count -eq 1) "Initialize was not found exactly once."
Assert-Condition ($dispose.Count -eq 1) "Dispose was not found exactly once."
Assert-Condition `
    ($restoreConfiguration.Count -eq 1) `
    "RestoreConfiguration was not found exactly once."
Assert-Condition `
    ($restoreAlarmTimingStates.Count -eq 1) `
    "RestoreAlarmTimingStates was not found exactly once."
Assert-Condition `
    ($advanceRuleTiming.Count -eq 1) `
    "AdvanceRuleTiming was not found exactly once."
Assert-Condition `
    ($setAlarm.Count -eq 1) `
    "SetAlarm was not found exactly once."
Assert-Condition `
    ($tryTakeAttentionRequest.Count -eq 1) `
    "TryTakeAttentionRequest was not found exactly once."
Assert-Condition `
    ($shouldEnqueueAttention.Count -eq 1) `
    "ShouldEnqueueAttentionRequest was not found exactly once."
Assert-Condition `
    ($tryGetInstrumentForecast.Count -eq 2) `
    "TryGetInstrumentForecast must expose exactly two overloads."
Assert-Condition `
    ($captureInstrumentValues.Count -eq 1) `
    "CaptureInstrumentValues was not found exactly once."
Assert-Condition `
    ($forecastWindowHelper.Count -eq 1) `
    "Forecast window helper was not found exactly once."
Assert-Condition `
    ($instrumentClockRollbackHelper.Count -eq 1) `
    "Instrument clock rollback helper was not found exactly once."
Assert-Condition `
    ($replaceAlarmAreas.Count -eq 1) `
    "ReplaceAlarmAreas was not found exactly once."
Assert-Condition `
    ($updatePanelSettings.Count -eq 2) `
    "UpdatePanelSettings must expose exactly two overloads."
Assert-Condition `
    ($tryGetDashboardViews.Count -eq 1) `
    "TryGetDashboardViews was not found exactly once."
Assert-Condition `
    ($tryGetAlarmIncidentSnapshot.Count -eq 1) `
    "TryGetAlarmIncidentSnapshot was not found exactly once."
Assert-Condition `
    ($createAlarmIncidentActiveSample.Count -eq 1) `
    "CreateAlarmIncidentActiveSample was not found exactly once."
Assert-Condition `
    ($getAlarmIncidentHistoryCapture.Count -eq 1) `
    "GetAlarmIncidentHistoryCapture was not found exactly once."
Assert-Condition `
    ($buildAlarmIncidentHistoryCapture.Count -eq 1) `
    "BuildAlarmIncidentHistoryCapture was not found exactly once."
Assert-Condition `
    ($tryCaptureAlarmAreaProjection.Count -eq 1) `
    "TryCaptureAlarmAreaProjection was not found exactly once."
Assert-Condition `
    ($tryAcknowledgeDashboard.Count -eq 1) `
    "TryAcknowledgeDashboard was not found exactly once."
Assert-Condition `
    ($tryGetNextDashboardUnacknowledged.Count -eq 1) `
    "TryGetNextDashboardUnacknowledged was not found exactly once."
Assert-Condition `
    ($projectActiveDashboardArea.Count -eq 1) `
    "ProjectActiveDashboardArea was not found exactly once."
Assert-Condition `
    ($canAcknowledgeFilteredDashboardAlarm.Count -eq 1) `
    "CanAcknowledgeFilteredDashboardAlarm was not found exactly once."
Assert-Condition `
    ($isExactAlarmAreaFilter.Count -eq 1) `
    "IsExactAlarmAreaFilter was not found exactly once."

$restoredStageSelectorCalls = @(
    Read-MethodInstructions $restoreAlarmTimingStates[0] | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $timingMemoryPolicyTypeName `
            "FindRestoredSystemStageIndex"
    })
Assert-Condition `
    ($restoredStageSelectorCalls.Count -eq 1) `
    "RestoreAlarmTimingStates must use the strict stage selector exactly once."
Write-Host `
    "UNMA restored-stage bootstrap IL regression passed."

# Escalation is deliberately split into pure domain policy plus a runtime-only
# UI hand-off. Verify the compiled call graph and public dequeue contract so a
# later refactor cannot accidentally bypass the one-shot latch or execute a
# game/system mutation while consuming presentation intent.
$attentionParameters = @($tryTakeAttentionRequest[0].GetParameters())
Assert-Condition `
    ($tryTakeAttentionRequest[0].IsPublic -and
        $tryTakeAttentionRequest[0].ReturnType -eq [bool]) `
    "TryTakeAttentionRequest must remain a public bool API."
Assert-Condition `
    ($attentionParameters.Count -eq 1 -and
        $attentionParameters[0].IsOut -and
        $attentionParameters[0].ParameterType.IsByRef -and
        $attentionParameters[0].ParameterType.GetElementType().FullName -eq
            $attentionRequestTypeName) `
    "TryTakeAttentionRequest must expose one out AlarmAttentionRequest."

$attentionProperties = @{}
foreach ($property in $attentionRequestType.GetProperties(
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::Public -bor
        [System.Reflection.BindingFlags]::DeclaredOnly)) {
    $attentionProperties[$property.Name] = $property.PropertyType.FullName
}
foreach ($requiredProperty in @{
        PanelId = "System.String"
        SlotId = "System.String"
        OperatorAction = "UNMA.Domain.AlarmOperatorAction"
    }.GetEnumerator()) {
    Assert-Condition `
        ($attentionProperties.ContainsKey($requiredProperty.Key) -and
            $attentionProperties[$requiredProperty.Key] -eq
                $requiredProperty.Value) `
        "AlarmAttentionRequest.$($requiredProperty.Key) has the wrong type."
}

$advanceEscalationCalls = @(
    Read-MethodInstructions $advanceRuleTiming[0] | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $escalationPolicyTypeName `
            "Evaluate"
    })
Assert-Condition `
    ($advanceEscalationCalls.Count -eq 1) `
    "AdvanceRuleTiming must evaluate escalation exactly once."

$enqueueAttentionCalls = @(
    Read-MethodInstructions $setAlarm[0] | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $attentionQueuePolicyTypeName `
            "TryEnqueue"
    })
Assert-Condition `
    ($enqueueAttentionCalls.Count -eq 1) `
    "SetAlarm must enqueue attention through the bounded policy exactly once."
$enqueueGuardCalls = @(
    Read-MethodInstructions $setAlarm[0] | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $runtimeTypeName `
            "ShouldEnqueueAttentionRequest"
    })
Assert-Condition `
    ($enqueueGuardCalls.Count -eq 1) `
    "SetAlarm must guard attention hand-off exactly once."
Assert-Condition `
    (-not [bool]$shouldEnqueueAttention[0].Invoke(
        $null,
        @($false, $true))) `
    "Initial activation must not enqueue operator attention."
Assert-Condition `
    (-not [bool]$shouldEnqueueAttention[0].Invoke(
        $null,
        @($true, $false))) `
    "An unchanged active occurrence must not enqueue operator attention."
Assert-Condition `
    ([bool]$shouldEnqueueAttention[0].Invoke(
        $null,
        @($true, $true))) `
    "Only an active alarm entering a new occurrence may enqueue attention."

$takeAttentionInstructions = @(
    Read-MethodInstructions $tryTakeAttentionRequest[0])
$takeBestCalls = @($takeAttentionInstructions | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $attentionQueuePolicyTypeName `
            "TryTakeBest"
    })
Assert-Condition `
    ($takeBestCalls.Count -eq 1) `
    "TryTakeAttentionRequest must prune/select through TryTakeBest exactly once."
foreach ($instruction in $takeAttentionInstructions) {
    if ($instruction.Operand -isnot [System.Reflection.MethodBase]) {
        continue
    }
    $declaringTypeName = $instruction.Operand.DeclaringType.FullName
    Assert-Condition `
        (-not ($declaringTypeName -like "Mafi*" -or
            $declaringTypeName -like "UnityEngine*")) `
        "TryTakeAttentionRequest must not call game or Unity APIs."
}

$restoredEscalationCalls = @(
    Read-MethodInstructions $restoreAlarmTimingStates[0] | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $escalationPolicyTypeName `
            "IsEscalatedOccurrenceId"
    })
Assert-Condition `
    ($restoredEscalationCalls.Count -eq 1) `
    "RestoreAlarmTimingStates must use the exact escalation occurrence ID."
Write-Host `
    "UNMA escalation runtime IL/reflection regression passed."

# Historian consumers use one runtime query rather than independently reading
# range, current value, and samples. Keep its window-aware contract and pure
# policy hand-off stable for the UI.
$windowForecast = @($tryGetInstrumentForecast | Where-Object {
    $parameters = @($_.GetParameters())
    $parameters.Count -eq 3 -and
        $parameters[0].ParameterType -eq [string] -and
        $parameters[1].ParameterType -eq [int] -and
        $parameters[2].IsOut -and
        $parameters[2].ParameterType.IsByRef -and
        $parameters[2].ParameterType.GetElementType() -eq
            $forecastResultType
})
$defaultForecast = @($tryGetInstrumentForecast | Where-Object {
    $parameters = @($_.GetParameters())
    $parameters.Count -eq 2 -and
        $parameters[0].ParameterType -eq [string] -and
        $parameters[1].IsOut -and
        $parameters[1].ParameterType.IsByRef -and
        $parameters[1].ParameterType.GetElementType() -eq
            $forecastResultType
})
Assert-Condition `
    ($windowForecast.Count -eq 1 -and
        $windowForecast[0].IsPublic -and
        $windowForecast[0].ReturnType -eq [bool]) `
    "Window-aware TryGetInstrumentForecast contract changed."
Assert-Condition `
    ($defaultForecast.Count -eq 1 -and
        $defaultForecast[0].IsPublic -and
        $defaultForecast[0].ReturnType -eq [bool]) `
    "Default TryGetInstrumentForecast contract changed."
$forecastPolicyCalls = @(
    Read-MethodInstructions $windowForecast[0] | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $forecastPolicyTypeName `
            "TryAnalyze"
    })
Assert-Condition `
    ($forecastPolicyCalls.Count -eq 1) `
    "Window-aware forecast query must call the pure policy exactly once."
$forecastInstructions = @(
    Read-MethodInstructions $windowForecast[0])
$defaultForecastInstructions = @(
    Read-MethodInstructions $defaultForecast[0])
$defaultDelegations = @($defaultForecastInstructions | Where-Object {
    Test-IsMethodInstruction `
        $_ `
        $runtimeTypeName `
        "TryGetInstrumentForecast"
})
Assert-Condition `
    ($defaultDelegations.Count -eq 1) `
    "Default forecast overload must delegate exactly once."
$defaultPolicyCalls = @($defaultForecastInstructions | Where-Object {
    Test-IsMethodInstruction `
        $_ `
        $forecastPolicyTypeName `
        "TryAnalyze"
})
Assert-Condition `
    ($defaultPolicyCalls.Count -eq 0) `
    "Default forecast overload must not analyze independently."
$configurationGateField = $runtimeType.GetField(
    "m_configurationGate",
    $bindingFlags)
$configurationGateLoads = @($forecastInstructions | Where-Object {
    $_.Operand -is [System.Reflection.FieldInfo] -and
        (Test-SameField $_.Operand $configurationGateField)
})
Assert-Condition `
    ($configurationGateLoads.Count -eq 0) `
    "Forecast query must not acquire or read the configuration lock."
$instrumentValuesGateField = $runtimeType.GetField(
    "m_instrumentValuesGate",
    $bindingFlags)
$requiredForecastSnapshotFields = @(
    $instrumentValuesGateField,
    $runtimeType.GetField("m_instrumentForecastRanges", $bindingFlags),
    $runtimeType.GetField("m_lastInstrumentValues", $bindingFlags),
    $runtimeType.GetField("m_instrumentHistory", $bindingFlags),
    $runtimeType.GetField(
        "m_lastInstrumentCaptureTimestampTicks",
        $bindingFlags))
foreach ($snapshotField in $requiredForecastSnapshotFields) {
    Assert-Condition `
        ($null -ne $snapshotField) `
        "A required forecast snapshot field is missing."
    $snapshotFieldLoads = @($forecastInstructions | Where-Object {
        $_.Operand -is [System.Reflection.FieldInfo] -and
            (Test-SameField $_.Operand $snapshotField)
    })
    Assert-Condition `
        ($snapshotFieldLoads.Count -ge 1) `
        "Forecast query no longer reads '$($snapshotField.Name)'."
}
$monitorEnterCalls = @($forecastInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Enter"
})
$monitorExitCalls = @($forecastInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Exit"
})
Assert-Condition `
    ($monitorEnterCalls.Count -eq 1 -and $monitorExitCalls.Count -eq 1) `
    "Forecast snapshot must use exactly one balanced monitor section."
Assert-Condition `
    ($forecastPolicyCalls[0].Offset -gt $monitorExitCalls[0].Offset) `
    "Forecast policy must run only after leaving the snapshot monitor."
$forecastWindowCalls = @($forecastInstructions | Where-Object {
    Test-IsMethodInstruction `
        $_ `
        $runtimeTypeName `
        "IsInstrumentForecastSampleInWindow"
})
Assert-Condition `
    ($forecastWindowCalls.Count -eq 1) `
    "Forecast query must apply the shared inclusive window helper."
$forecastGameCalls = @($forecastInstructions | Where-Object {
    if ($_.Operand -isnot [System.Reflection.MethodBase] -or
        $null -eq $_.Operand.DeclaringType) {
        return $false
    }
    $declaringName = $_.Operand.DeclaringType.FullName
    return $declaringName -like "Mafi.*" -or
        $declaringName -like "UnityEngine.*"
})
Assert-Condition `
    ($forecastGameCalls.Count -eq 0) `
    "Forecast query must not call Mafi or Unity APIs while snapshotting."
$windowMethod = $forecastWindowHelper[0]
Assert-Condition `
    ([bool]$windowMethod.Invoke(
        $null,
        @([double]60d, [double]100d, [int]40))) `
    "Forecast lower window bound must be inclusive."
Assert-Condition `
    (-not [bool]$windowMethod.Invoke(
        $null,
        @([double]59.999d, [double]100d, [int]40))) `
    "Forecast sample below the lower bound must be excluded."
Assert-Condition `
    ([bool]$windowMethod.Invoke(
        $null,
        @([double]100d, [double]100d, [int]40))) `
    "Forecast current-tick bound must be inclusive."
Assert-Condition `
    (-not [bool]$windowMethod.Invoke(
        $null,
        @([double]100.001d, [double]100d, [int]40))) `
    "Forecast future samples must be excluded."
Assert-Condition `
    ([bool]$windowMethod.Invoke(
        $null,
        @([double]-1000d, [double]100d, [int]0))) `
    "Full-history forecast must retain older samples."

$rollbackMethod = $instrumentClockRollbackHelper[0]
Assert-Condition `
    ([bool]$rollbackMethod.Invoke(
        $null,
        @([double]5d, [double]10d))) `
    "A rewind inside the sampling interval must start a new epoch."
Assert-Condition `
    ([bool]$rollbackMethod.Invoke(
        $null,
        @([double]0d, [double]10d))) `
    "A rewind onto the last sample tick must start a new epoch."
Assert-Condition `
    (-not [bool]$rollbackMethod.Invoke(
        $null,
        @([double]10d, [double]10d))) `
    "An unchanged capture tick must not start a new epoch."
$rollbackCalls = @(
    Read-MethodInstructions $captureInstrumentValues[0] |
        Where-Object {
            Test-IsMethodInstruction `
                $_ `
                $runtimeTypeName `
                "DidInstrumentClockRollBack"
        })
Assert-Condition `
    ($rollbackCalls.Count -eq 1) `
    "CaptureInstrumentValues must evaluate clock rollback exactly once."
Write-Host `
    "UNMA instrument forecast runtime IL/reflection regression passed."

# Alarm-area dashboard filtering is a presentation scope over existing panel
# visibility. Keep the public atomic APIs stable, reject stale area filters,
# exclude gone latches from filtered dashboards, and preserve full rollback for
# configuration mutations.
$replaceParameters = @($replaceAlarmAreas[0].GetParameters())
Assert-Condition `
    ($replaceAlarmAreas[0].IsPublic -and
        $replaceAlarmAreas[0].ReturnType -eq [bool] -and
        $replaceParameters.Count -eq 2 -and
        (Get-GenericTypeDefinitionName ($replaceParameters[0].ParameterType)) -eq
            "System.Collections.Generic.IReadOnlyList``1" -and
        $replaceParameters[0].ParameterType.GetGenericArguments()[0] -eq
            $alarmAreaDefinitionType -and
        $replaceParameters[1].IsOut -and
        $replaceParameters[1].ParameterType.IsByRef -and
        $replaceParameters[1].ParameterType.GetElementType() -eq [int]) `
    "ReplaceAlarmAreas public contract changed."

$dashboardViewParameters = @($tryGetDashboardViews[0].GetParameters())
$dashboardViewOutType =
    $dashboardViewParameters[1].ParameterType.GetElementType()
Assert-Condition `
    ($tryGetDashboardViews[0].IsPublic -and
        $tryGetDashboardViews[0].ReturnType -eq [bool] -and
        $dashboardViewParameters.Count -eq 2 -and
        $dashboardViewParameters[0].ParameterType -eq $alarmAreaFilterType -and
        $dashboardViewParameters[1].IsOut -and
        $dashboardViewOutType.IsGenericType -and
        (Get-GenericTypeDefinitionName $dashboardViewOutType) -eq
            "System.Collections.Generic.IReadOnlyList``1" -and
        $dashboardViewOutType.GetGenericArguments()[0] -eq $alarmViewType) `
    "TryGetDashboardViews public contract changed."

$dashboardAckParameters = @($tryAcknowledgeDashboard[0].GetParameters())
Assert-Condition `
    ($tryAcknowledgeDashboard[0].IsPublic -and
        $tryAcknowledgeDashboard[0].ReturnType -eq [bool] -and
        $dashboardAckParameters.Count -eq 3 -and
        $dashboardAckParameters[0].ParameterType -eq $alarmAreaFilterType -and
        (Get-GenericTypeDefinitionName `
            ($dashboardAckParameters[1].ParameterType)) -eq
            "System.Collections.Generic.IEnumerable``1" -and
        $dashboardAckParameters[1].ParameterType.GetGenericArguments()[0] -eq
            [string] -and
        $dashboardAckParameters[2].IsOut -and
        $dashboardAckParameters[2].ParameterType.GetElementType() -eq [int]) `
    "TryAcknowledgeDashboard public contract changed."

$dashboardNextParameters = @(
    $tryGetNextDashboardUnacknowledged[0].GetParameters())
Assert-Condition `
    ($tryGetNextDashboardUnacknowledged[0].IsPublic -and
        $tryGetNextDashboardUnacknowledged[0].ReturnType -eq [bool] -and
        $dashboardNextParameters.Count -eq 3 -and
        $dashboardNextParameters[0].ParameterType -eq $alarmAreaFilterType -and
        $dashboardNextParameters[1].ParameterType -eq [string] -and
        $dashboardNextParameters[2].IsOut -and
        $dashboardNextParameters[2].ParameterType.GetElementType() -eq
            $alarmViewType) `
    "TryGetNextDashboardUnacknowledged public contract changed."

$legacyPanelSettings = @($updatePanelSettings | Where-Object {
    @($_.GetParameters()).Count -eq 6
})
$areaPanelSettings = @($updatePanelSettings | Where-Object {
    @($_.GetParameters()).Count -eq 7
})
Assert-Condition `
    ($legacyPanelSettings.Count -eq 1 -and
        $areaPanelSettings.Count -eq 1 -and
        $areaPanelSettings[0].GetParameters()[6].ParameterType -eq [string]) `
    "UpdatePanelSettings area overload changed."
$legacyPanelSettingsDelegations = @(
    Read-MethodInstructions $legacyPanelSettings[0] | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $runtimeTypeName `
            "UpdatePanelSettings"
    })
Assert-Condition `
    ($legacyPanelSettingsDelegations.Count -eq 1) `
    "Legacy UpdatePanelSettings must delegate exactly once."

foreach ($mutationContract in @(
        [pscustomobject]@{
            Method = $replaceAlarmAreas[0]
            PolicyMethod = "ValidateReplacement"
        },
        [pscustomobject]@{
            Method = $areaPanelSettings[0]
            PolicyMethod = "TryAssign"
        })) {
    $mutationInstructions = @(
        Read-MethodInstructions $mutationContract.Method)
    foreach ($runtimeCall in @(
            "CloneConfiguration",
            "SaveConfiguration",
            "RestoreConfiguration",
            "RestoreConfigurationAlarmSnapshots")) {
        $calls = @($mutationInstructions | Where-Object {
            Test-IsMethodInstruction $_ $runtimeTypeName $runtimeCall
        })
        Assert-Condition `
            ($calls.Count -eq 1) `
            "$($mutationContract.Method.Name) must call $runtimeCall exactly once."
    }
    $policyCalls = @($mutationInstructions | Where-Object {
        Test-IsMethodInstruction `
            $_ `
            $alarmAreaPolicyTypeName `
            $mutationContract.PolicyMethod
    })
    Assert-Condition `
        ($policyCalls.Count -eq 1) `
        "$($mutationContract.Method.Name) must use $($mutationContract.PolicyMethod) exactly once."
}

$dashboardViewInstructions = @(
    Read-MethodInstructions $tryGetDashboardViews[0])
foreach ($requiredCall in @(
        "TryCaptureAlarmAreaProjection",
        "GetViews",
        "ProjectActiveDashboardArea")) {
    $calls = @($dashboardViewInstructions | Where-Object {
        Test-IsMethodInstruction $_ $runtimeTypeName $requiredCall
    })
    Assert-Condition `
        ($calls.Count -eq 1) `
        "TryGetDashboardViews must call $requiredCall exactly once."
}
$dashboardViewMonitorEnter = @($dashboardViewInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Enter"
})
$dashboardViewMonitorExit = @($dashboardViewInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Exit"
})
Assert-Condition `
    ($dashboardViewMonitorEnter.Count -eq 1 -and
        $dashboardViewMonitorExit.Count -eq 1) `
    "Filtered dashboard alarm snapshot must use one balanced monitor section."
$dashboardProjectionCalls = @($dashboardViewInstructions | Where-Object {
    Test-IsMethodInstruction `
        $_ `
        $runtimeTypeName `
        "ProjectActiveDashboardArea"
})
Assert-Condition `
    ($dashboardProjectionCalls[0].Offset -gt
        $dashboardViewMonitorExit[0].Offset) `
    "Area visibility and projection must run after leaving the alarm lock."

$captureAreaInstructions = @(
    Read-MethodInstructions $tryCaptureAlarmAreaProjection[0])
$captureAreaMonitorEnter = @($captureAreaInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Enter"
})
$captureAreaMonitorExit = @($captureAreaInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Exit"
})
$alarmGateField = $runtimeType.GetField("m_gate", $bindingFlags)
$captureAlarmGateLoads = @($captureAreaInstructions | Where-Object {
    $_.Operand -is [System.Reflection.FieldInfo] -and
        (Test-SameField $_.Operand $alarmGateField)
})
Assert-Condition `
    ($captureAreaMonitorEnter.Count -eq 1 -and
        $captureAreaMonitorExit.Count -eq 1 -and
        $captureAlarmGateLoads.Count -eq 0) `
    "Area membership snapshot must use only the configuration monitor."
foreach ($instruction in $dashboardViewInstructions) {
    if ($instruction.Operand -isnot [System.Reflection.MethodBase] -or
        $null -eq $instruction.Operand.DeclaringType) {
        continue
    }
    $declaringTypeName = $instruction.Operand.DeclaringType.FullName
    Assert-Condition `
        (-not ($declaringTypeName -like "Mafi*" -or
            $declaringTypeName -like "UnityEngine*")) `
        "TryGetDashboardViews must not call game or Unity APIs."
}

$projectAreaInstructions = @(
    Read-MethodInstructions $projectActiveDashboardArea[0])
$projectActiveCalls = @($projectAreaInstructions | Where-Object {
    Test-IsMethodInstruction `
        $_ `
        $panelProjectionTypeName `
        "ProjectActive"
})
Assert-Condition `
    ($projectActiveCalls.Count -eq 1) `
    "Filtered area projection must deduplicate through ProjectActive exactly once."

$activeAcknowledged = [Activator]::CreateInstance($alarmViewType)
$activeAcknowledged.Key = "active-acknowledged"
$activeAcknowledged.SlotId = "shared-slot"
$activeAcknowledged.IsActive = $true
$activeAcknowledged.IsAcknowledged = $true
$activeAcknowledged.Sequence = [long]1
$activeDuplicate = [Activator]::CreateInstance($alarmViewType)
$activeDuplicate.Key = "active-duplicate"
$activeDuplicate.SlotId = "shared-slot"
$activeDuplicate.IsActive = $true
$activeDuplicate.IsAcknowledged = $true
$activeDuplicate.Sequence = [long]2
$goneUnacknowledged = [Activator]::CreateInstance($alarmViewType)
$goneUnacknowledged.Key = "gone-unacknowledged"
$goneUnacknowledged.SlotId = "shared-slot"
$goneUnacknowledged.IsGoneUnacknowledged = $true
$goneUnacknowledged.Sequence = [long]999
$projectionCandidates = [Array]::CreateInstance($alarmViewType, 3)
$projectionCandidates.SetValue($activeAcknowledged, 0)
$projectionCandidates.SetValue($activeDuplicate, 1)
$projectionCandidates.SetValue($goneUnacknowledged, 2)
$projectionArguments = [object[]]::new(1)
$projectionArguments[0] = $projectionCandidates
$projectedAreaViews = @(
    $projectActiveDashboardArea[0].Invoke($null, $projectionArguments))
Assert-Condition `
    ($projectedAreaViews.Count -eq 1 -and
        $projectedAreaViews[0].IsActive -and
        $projectedAreaViews[0].IsAcknowledged -and
        -not $projectedAreaViews[0].RequiresAcknowledgement) `
    "Filtered area projection must exclude gone latches before deduplication."

$activeUnacknowledged = [Activator]::CreateInstance($alarmViewType)
$activeUnacknowledged.IsActive = $true
$activeUnacknowledged.IsAcknowledged = $false
Assert-Condition `
    ([bool]$canAcknowledgeFilteredDashboardAlarm[0].Invoke(
        $null,
        @($activeUnacknowledged))) `
    "Filtered dashboard must acknowledge active unacknowledged alarms."
Assert-Condition `
    (-not [bool]$canAcknowledgeFilteredDashboardAlarm[0].Invoke(
        $null,
        @($activeAcknowledged))) `
    "Filtered dashboard must not acknowledge already acknowledged alarms."
Assert-Condition `
    (-not [bool]$canAcknowledgeFilteredDashboardAlarm[0].Invoke(
        $null,
        @($goneUnacknowledged))) `
    "Filtered dashboard must never acknowledge gone latches."

$areaKind = [Enum]::Parse($alarmAreaFilterKindType, "Area")
$allFilter = $alarmAreaFilterType.GetProperty("All").GetValue($null)
$unknownAreaFilter = [Activator]::CreateInstance(
    $alarmAreaFilterType,
    [object[]]@($areaKind, "missing-area"))
Assert-Condition `
    (-not [bool]$isExactAlarmAreaFilter[0].Invoke(
        $null,
        @($unknownAreaFilter, $allFilter))) `
    "A stale area filter must never fall back to ALL."

$dashboardAckInstructions = @(
    Read-MethodInstructions $tryAcknowledgeDashboard[0])
$dashboardAckMonitorEnter = @($dashboardAckInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Enter"
})
$dashboardAckMonitorExit = @($dashboardAckInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Exit"
})
$persistenceGateField = $runtimeType.GetField(
    "m_persistenceGate",
    $bindingFlags)
$ackPersistenceGateLoads = @($dashboardAckInstructions | Where-Object {
    $_.Operand -is [System.Reflection.FieldInfo] -and
        (Test-SameField $_.Operand $persistenceGateField)
})
$ackConfigurationGateLoads = @($dashboardAckInstructions | Where-Object {
    $_.Operand -is [System.Reflection.FieldInfo] -and
        (Test-SameField $_.Operand $configurationGateField)
})
Assert-Condition `
    ($dashboardAckMonitorEnter.Count -eq 3 -and
        $dashboardAckMonitorExit.Count -eq 3 -and
        $ackPersistenceGateLoads.Count -ge 1 -and
        $ackConfigurationGateLoads.Count -eq 0) `
    "Scoped acknowledgement lock order changed or nested the configuration gate."
$globalAckCalls = @($dashboardAckInstructions | Where-Object {
    Test-IsMethodInstruction $_ $runtimeTypeName "AcknowledgeAll"
})
Assert-Condition `
    ($globalAckCalls.Count -eq 0) `
    "Scoped dashboard acknowledgement must never call AcknowledgeAll."
$areaPersistCalls = @($dashboardAckInstructions | Where-Object {
    Test-IsMethodInstruction $_ $runtimeTypeName "PersistAlarmState"
})
Assert-Condition `
    ($areaPersistCalls.Count -eq 1) `
    "Scoped dashboard acknowledgement must expose one persistence hand-off."
$nextInstructions = @(
    Read-MethodInstructions $tryGetNextDashboardUnacknowledged[0])
$nextViewCalls = @($nextInstructions | Where-Object {
    Test-IsMethodInstruction $_ $runtimeTypeName "TryGetDashboardViews"
})
$nextMutationCalls = @($nextInstructions | Where-Object {
    $_.Operand -is [System.Reflection.MethodBase] -and
        $_.Operand.DeclaringType.FullName -eq $runtimeTypeName -and
        ($_.Operand.Name -like "Acknowledge*" -or
            $_.Operand.Name -eq "PersistAlarmState")
})
Assert-Condition `
    ($nextViewCalls.Count -eq 1 -and $nextMutationCalls.Count -eq 0) `
    "Next alarm navigation must be a read-only view query."
Write-Host `
    "UNMA alarm-area runtime IL/reflection regression passed."

# Incident Lens is a read-only derivation over the exact dashboard scope and a
# bounded history capture. Preserve its public query contract, exact sequence
# join, fallback for active tiles without usable history time, lock boundary,
# and absence of acknowledgement/persistence/game side effects.
$incidentSnapshotParameters = @(
    $tryGetAlarmIncidentSnapshot[0].GetParameters())
Assert-Condition `
    ($tryGetAlarmIncidentSnapshot[0].IsPublic -and
        $tryGetAlarmIncidentSnapshot[0].ReturnType -eq [bool] -and
        $incidentSnapshotParameters.Count -eq 2 -and
        $incidentSnapshotParameters[0].ParameterType -eq
            $alarmAreaFilterType -and
        $incidentSnapshotParameters[1].IsOut -and
        $incidentSnapshotParameters[1].ParameterType.IsByRef -and
        $incidentSnapshotParameters[1].ParameterType.GetElementType() -eq
            $alarmIncidentSnapshotType) `
    "TryGetAlarmIncidentSnapshot public contract changed."

$incidentInstructions = @(
    Read-MethodInstructions $tryGetAlarmIncidentSnapshot[0])
$incidentDashboardCalls = @($incidentInstructions | Where-Object {
    Test-IsMethodInstruction $_ $runtimeTypeName "TryGetDashboardViews"
})
$incidentClockCalls = @($incidentInstructions | Where-Object {
    Test-IsMethodInstruction $_ $runtimeTypeName "get_CurrentGameTicks"
})
$incidentCaptureCalls = @($incidentInstructions | Where-Object {
    Test-IsMethodInstruction `
        $_ `
        $runtimeTypeName `
        "GetAlarmIncidentHistoryCapture"
})
$incidentSampleCalls = @($incidentInstructions | Where-Object {
    Test-IsMethodInstruction `
        $_ `
        $runtimeTypeName `
        "CreateAlarmIncidentActiveSample"
})
$incidentAnalyzeCalls = @($incidentInstructions | Where-Object {
    Test-IsMethodInstruction $_ $alarmIncidentPolicyTypeName "Analyze"
})
Assert-Condition `
    ($incidentDashboardCalls.Count -eq 1 -and
        $incidentClockCalls.Count -eq 1 -and
        $incidentCaptureCalls.Count -eq 1 -and
        $incidentSampleCalls.Count -eq 1 -and
        $incidentAnalyzeCalls.Count -eq 1) `
    "Incident query must capture clock, scoped dashboard, history, members, and analysis exactly once."
Assert-Condition `
    ($incidentClockCalls[0].Offset -lt $incidentDashboardCalls[0].Offset) `
    "Incident query must capture the game clock before taking UNMA snapshots."

$incidentMonitorEnter = @($incidentInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Enter"
})
$incidentMonitorExit = @($incidentInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Exit"
})
$incidentAlarmGateLoads = @($incidentInstructions | Where-Object {
    $_.Operand -is [System.Reflection.FieldInfo] -and
        (Test-SameField $_.Operand $alarmGateField)
})
$incidentConfigurationGateLoads = @($incidentInstructions | Where-Object {
    $_.Operand -is [System.Reflection.FieldInfo] -and
        (Test-SameField $_.Operand $configurationGateField)
})
Assert-Condition `
    ($incidentMonitorEnter.Count -eq 0 -and
        $incidentMonitorExit.Count -eq 0 -and
        $incidentAlarmGateLoads.Count -eq 0 -and
        $incidentConfigurationGateLoads.Count -eq 0) `
    "Incident query must delegate its cache monitor and never nest alarm/configuration locks."
Assert-Condition `
    ($incidentAnalyzeCalls[0].Offset -gt $incidentCaptureCalls[0].Offset) `
    "Incident policy analysis must run only after the history cache query returns."

$incidentCacheInstructions = @(
    Read-MethodInstructions $getAlarmIncidentHistoryCapture[0])
$incidentCacheMonitorEnter = @($incidentCacheInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Enter"
})
$incidentCacheMonitorExit = @($incidentCacheInstructions | Where-Object {
    Test-IsMethodInstruction $_ "System.Threading.Monitor" "Exit"
})
$incidentCacheBuilderCalls = @($incidentCacheInstructions | Where-Object {
    Test-IsMethodInstruction `
        $_ `
        $runtimeTypeName `
        "BuildAlarmIncidentHistoryCapture"
})
Assert-Condition `
    ($incidentCacheMonitorEnter.Count -eq 2 -and
        $incidentCacheMonitorExit.Count -eq 2 -and
        $incidentCacheBuilderCalls.Count -eq 2 -and
        $incidentCacheBuilderCalls[0].Offset -gt
            $incidentCacheMonitorExit[0].Offset -and
        $incidentCacheBuilderCalls[0].Offset -lt
            $incidentCacheMonitorEnter[1].Offset -and
        $incidentCacheBuilderCalls[1].Offset -gt
            $incidentCacheMonitorExit[1].Offset) `
    "Incident cache must build outside locks and retain an unlocked bounded-retry fallback."
$incidentCaptureAttemptLimitField = $runtimeType.GetField(
    "MaximumAlarmIncidentHistoryCaptureAttempts",
    $bindingFlags)
Assert-Condition `
    ($null -ne $incidentCaptureAttemptLimitField -and
        $incidentCaptureAttemptLimitField.IsLiteral -and
        [int]($incidentCaptureAttemptLimitField.GetRawConstantValue()) -eq 2) `
    "Incident history capture must retain a hard two-attempt progress bound."
$incidentCacheField = $runtimeType.GetField(
    "m_alarmIncidentHistoryCapture",
    $bindingFlags)
$incidentCacheRevisionField = $runtimeType.GetField(
    "m_alarmIncidentHistoryCaptureRevision",
    $bindingFlags)
foreach ($cacheField in @(
        $incidentCacheField,
        $incidentCacheRevisionField)) {
    Assert-Condition ($null -ne $cacheField) "Incident cache field is missing."
    $cacheWrites = @($incidentCacheInstructions | Where-Object {
        $_.OpCode.Name -eq "stfld" -and
            $_.Operand -is [System.Reflection.FieldInfo] -and
            (Test-SameField $_.Operand $cacheField)
    })
    Assert-Condition `
        ($cacheWrites.Count -eq 1) `
        "Incident cache may publish '$($cacheField.Name)' only on its guarded path."
}

$incidentForbiddenCalls = @($incidentInstructions | Where-Object {
    if ($_.Operand -isnot [System.Reflection.MethodBase] -or
        $null -eq $_.Operand.DeclaringType) {
        return $false
    }
    $method = [System.Reflection.MethodBase]$_.Operand
    if ($method.DeclaringType.FullName -eq $runtimeTypeName) {
        return $method.Name -eq "SetAlarm" -or
            $method.Name -like "Acknowledge*" -or
            $method.Name -like "TryAcknowledge*" -or
            $method.Name -like "Persist*" -or
            $method.Name -like "Save*"
    }
    return $method.DeclaringType.FullName -eq $alarmHistoryTypeName -and
        $method.Name -eq "SetState"
})
Assert-Condition `
    ($incidentForbiddenCalls.Count -eq 0) `
    "Incident query must not mutate alarms, acknowledgement, history, or persistence."
foreach ($instruction in $incidentInstructions) {
    if ($instruction.Operand -isnot [System.Reflection.MethodBase] -or
        $null -eq $instruction.Operand.DeclaringType) {
        continue
    }
    $declaringTypeName = $instruction.Operand.DeclaringType.FullName
    Assert-Condition `
        (-not ($declaringTypeName -like "Mafi*" -or
            $declaringTypeName -like "UnityEngine*")) `
        "Incident snapshot path must not directly call game or Unity APIs."
}
foreach ($readOnlyHelper in @(
        $getAlarmIncidentHistoryCapture[0],
        $buildAlarmIncidentHistoryCapture[0],
        $createAlarmIncidentActiveSample[0])) {
    foreach ($instruction in @(Read-MethodInstructions $readOnlyHelper)) {
        if ($instruction.Operand -isnot [System.Reflection.MethodBase] -or
            $null -eq $instruction.Operand.DeclaringType) {
            continue
        }
        $method = [System.Reflection.MethodBase]$instruction.Operand
        $declaringTypeName = $method.DeclaringType.FullName
        Assert-Condition `
            (-not ($declaringTypeName -like "Mafi*" -or
                $declaringTypeName -like "UnityEngine*")) `
            "$($readOnlyHelper.Name) must not call game or Unity APIs."
        Assert-Condition `
            (-not ($declaringTypeName -eq $runtimeTypeName -and
                ($method.Name -eq "SetAlarm" -or
                    $method.Name -like "Acknowledge*" -or
                    $method.Name -like "TryAcknowledge*" -or
                    $method.Name -like "Persist*" -or
                    $method.Name -like "Save*"))) `
            "$($readOnlyHelper.Name) must not reach an alarm or persistence mutation."
        Assert-Condition `
            (-not ($declaringTypeName -eq $alarmHistoryTypeName -and
                $method.Name -eq "SetState")) `
            "$($readOnlyHelper.Name) must not mutate history state."
    }
}

$sampleView = [Activator]::CreateInstance($alarmViewType)
$sampleView.Key = "incident-key"
$sampleView.Name = "Incident name"
$sampleView.Detail = "Incident detail"
$sampleView.Source = "system"
$sampleView.PanelId = "panel-a"
$sampleView.SlotId = "stable-slot"
$sampleView.Sequence = [long]41
$sampleView.Severity = [Enum]::Parse(
    $assembly.GetType("UNMA.Domain.AlarmSeverity", $true, $false),
    "Critical")
$sampleView.IsActive = $true
$sampleView.IsAcknowledged = $false
$exactSample = $createAlarmIncidentActiveSample[0].Invoke(
    $null,
    @($sampleView, [long]41, [double]123, [double]500))
Assert-Condition `
    ($null -ne $exactSample -and
        $exactSample.Sequence -eq [long]41 -and
        $exactSample.StableAlarmId -eq "stable-slot" -and
        $exactSample.RaisedAtTicks -eq [double]123) `
    "Incident active sample must use the exact matching history sequence and timestamp."
$missingHistorySample = $createAlarmIncidentActiveSample[0].Invoke(
    $null,
    @($sampleView, [long]42, [double]123, [double]500))
$futureHistorySample = $createAlarmIncidentActiveSample[0].Invoke(
    $null,
    @($sampleView, [long]41, [double]501, [double]500))
Assert-Condition `
    ($missingHistorySample.RaisedAtTicks -eq [double]500 -and
        $futureHistorySample.RaisedAtTicks -eq [double]500) `
    "Active tiles without an exact usable history timestamp must remain visible at the captured query tick."

$maximumOccurrenceSignalsField = $alarmIncidentPolicyType.GetField(
    "MaximumOccurrenceSignals",
    [System.Reflection.BindingFlags]::Public -bor
    [System.Reflection.BindingFlags]::Static)
Assert-Condition `
    ($null -ne $maximumOccurrenceSignalsField -and
        $maximumOccurrenceSignalsField.IsLiteral) `
    "AlarmIncidentPolicy.MaximumOccurrenceSignals must remain a public constant."
$maximumOccurrenceSignals = [int](
    $maximumOccurrenceSignalsField.GetRawConstantValue())
$historyListType = [System.Collections.Generic.List``1].MakeGenericType(
    @($alarmHistoryType))
$historyList = [Activator]::CreateInstance($historyListType)
$historyCount = $maximumOccurrenceSignals + 3
for ($index = 1;
     $index -le $historyCount;
     $index++) {
    # Intentionally interleave oldest/newest sequences. The cache must derive
    # recency from Sequence rather than trusting persisted list order.
    $sequence = if (($index % 2) -eq 1) {
        [long](($index + 1) / 2)
    } else {
        [long]($historyCount - ($index / 2) + 1)
    }
    $historyItem = [Activator]::CreateInstance($alarmHistoryType)
    $historyItem.Sequence = $sequence
    $historyItem.AlarmKey = "history-$sequence"
    $historyItem.RaisedAtTicks = [double]$sequence
    [void]$historyList.Add($historyItem)
}
$cacheRuntime = [System.Runtime.Serialization.FormatterServices]::
    GetUninitializedObject($runtimeType)
$alarmHistoryField = $runtimeType.GetField("m_alarmHistory", $bindingFlags)
$alarmHistoryRevisionField = $runtimeType.GetField(
    "m_alarmHistoryRevision",
    $bindingFlags)
Assert-Condition `
    ($null -ne $alarmGateField -and
        $null -ne $alarmHistoryField -and
        $null -ne $alarmHistoryRevisionField) `
    "Incident cache runtime fields are missing."
$alarmGateField.SetValue($cacheRuntime, (New-Object object))
$alarmHistoryField.SetValue($cacheRuntime, $historyList)
$alarmHistoryRevisionField.SetValue($cacheRuntime, [long]73)
$historyCapture = $getAlarmIncidentHistoryCapture[0].Invoke(
    $cacheRuntime,
    [object[]]@())
$reusedHistoryCapture = $getAlarmIncidentHistoryCapture[0].Invoke(
    $cacheRuntime,
    [object[]]@())
$captureType = $historyCapture.GetType()
$capturedSignals = $captureType.GetField(
    "RecentSignals",
    [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::Public).GetValue($historyCapture)
$capturedRaisedAt = $captureType.GetField(
    "RaisedAtTicksBySequence",
    [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::Public).GetValue($historyCapture)
Assert-Condition `
    ([object]::ReferenceEquals(
            $historyCapture,
            $reusedHistoryCapture) -and
        $capturedSignals.Count -eq $maximumOccurrenceSignals -and
        $capturedSignals[0].Sequence -eq
            [long]$historyCount -and
        $capturedRaisedAt.ContainsKey([long]1) -and
        $capturedRaisedAt[[long]1] -eq [double]1) `
    "Incident history cache must reuse its revision, sort shuffled newest pressure, and retain old active timestamps."
$alarmHistoryRevisionField.SetValue($cacheRuntime, [long]74)
$rebuiltHistoryCapture = $getAlarmIncidentHistoryCapture[0].Invoke(
    $cacheRuntime,
    [object[]]@())
Assert-Condition `
    (-not [object]::ReferenceEquals(
        $historyCapture,
        $rebuiltHistoryCapture)) `
    "Incident history cache must rebuild after the history revision changes."

Write-Host `
    "UNMA alarm-incident runtime IL/reflection regression passed."

# Atomic configuration rollback must not silently miss a newly added
# DataMember. Validate the compiled IL rather than maintaining a fragile
# hard-coded field count.
$configurationFields = @($configurationType.GetFields(
    [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::Public -bor
    [System.Reflection.BindingFlags]::DeclaredOnly) | Where-Object {
        $_.IsDefined(
            [System.Runtime.Serialization.DataMemberAttribute],
            $false)
    })
$restoreInstructions = @(
    Read-MethodInstructions $restoreConfiguration[0])
$restoredFieldNames = @($restoreInstructions | Where-Object {
        $_.OpCode.Name -eq "stfld" -and
        $_.Operand -is [System.Reflection.FieldInfo] -and
        $_.Operand.DeclaringType.FullName -eq $configurationTypeName
    } | ForEach-Object {
        $_.Operand.Name
    })
$loadedFieldNames = @($restoreInstructions | Where-Object {
        $_.OpCode.Name -eq "ldfld" -and
        $_.Operand -is [System.Reflection.FieldInfo] -and
        $_.Operand.DeclaringType.FullName -eq $configurationTypeName
    } | ForEach-Object {
        $_.Operand.Name
    })
foreach ($field in $configurationFields) {
    Assert-Condition `
        (@($restoredFieldNames | Where-Object { $_ -eq $field.Name }).Count -eq 1) `
        "RestoreConfiguration must assign DataMember '$($field.Name)' exactly once."
    Assert-Condition `
        (@($loadedFieldNames | Where-Object { $_ -eq $field.Name }).Count -eq 1) `
        "RestoreConfiguration must load DataMember '$($field.Name)' from the snapshot exactly once."
}

function Assert-CallsRuntimeMethodExactlyOnce {
    param(
        [System.Reflection.MethodBase]$Caller,
        [string]$CalleeName
    )

    $matchingCalls = @(Read-MethodInstructions $Caller | Where-Object {
            ($_.OpCode.Name -eq "call" -or
                $_.OpCode.Name -eq "callvirt") -and
            $_.Operand -is [System.Reflection.MethodBase] -and
            $_.Operand.DeclaringType.FullName -eq $runtimeTypeName -and
            $_.Operand.Name -eq $CalleeName
        })
    Assert-Condition `
        ($matchingCalls.Count -eq 1) `
        "$($Caller.Name) must call $CalleeName exactly once."
}
Write-Host (
    "UNMA configuration rollback IL regression passed: " +
    "$($configurationFields.Count) DataMember fields are restored atomically.")

foreach ($mutationMethodName in @(
        "UpdateRuleWithPersistenceLock",
        "UpdateSystemAlarmWithPersistenceLock",
        "SetRuleEnabledWithPersistenceLock")) {
    $mutationMethod = @(
        $methods | Where-Object Name -eq $mutationMethodName)
    Assert-Condition `
        ($mutationMethod.Count -eq 1) `
        "$mutationMethodName was not found exactly once."
    Assert-CallsRuntimeMethodExactlyOnce `
        $mutationMethod[0] `
        "CloneConfiguration"
    Assert-CallsRuntimeMethodExactlyOnce `
        $mutationMethod[0] `
        "RestoreConfiguration"
}

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
