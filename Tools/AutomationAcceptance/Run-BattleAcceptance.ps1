param(
    [string[]]$Scenario = @('two-client-join', 'two-client-basic-move', 'two-client-disconnect', 'two-client-reconnect'),
    [string]$UnityExePath = '',
    [string]$DotnetExePath = '',
    [string]$AuthServerAddress = '127.0.0.1',
    [int]$AuthServerPort = 20001,
    [string]$BattleServerAddress = '127.0.0.1',
    [int]$BattleServerPort = 20101,
    [int]$MongoPort = 27017,
    [string]$ClientAUserName = 'battle-auto-client-a',
    [string]$ClientBUserName = 'battle-auto-client-b',
    [string]$TestAccountPassword = 'BattleAutomation-20260809!',
    [int]$ClientTimeoutSeconds = 120,
    [string]$Bridge = 'puerts',
    [string]$ControllerScriptPath = '',
    [switch]$EnableNetworkSimulation,
    [int]$NetSimUplinkDelayMs = -1,
    [int]$NetSimDownlinkDelayMs = -1,
    [int]$NetSimUplinkJitterMs = -1,
    [int]$NetSimDownlinkJitterMs = -1,
    [int]$NetSimUplinkLossPercent = -1,
    [int]$NetSimDownlinkLossPercent = -1,
    [UInt64]$NetSimSeed = 20260804,
    [switch]$InteractiveEditor,
    [switch]$SkipCloneCreation,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$unityProjectPath = Join-Path $repoRoot 'UnityProject'
$serverProjectPath = Join-Path $repoRoot 'GameServer\Server\Main\Main.csproj'
$editorExecuteMethod = 'RealClientAutomationEditor.RunAutomationClient'
$cloneExecuteMethod = 'RealClientAutomationEditor.EnsureParrelSyncClone'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runRoot = Join-Path $repoRoot "_codex_tmp\battle-automation\$timestamp"
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$defaultMinimumPlayerCount = '2'
$defaultMovementDistanceThreshold = '1.0'
$defaultMovementFrames = '90'
$defaultSettleFrames = '30'
$defaultDisconnectFrame = '60'
$defaultExpectedBuffId = '9001'
$defaultBuffApplyDelayFrames = '30'
$defaultBuffDurationFrames = '45'

function Resolve-UnityExePath {
    param([string]$PreferredPath)

    if ($PreferredPath -and (Test-Path -LiteralPath $PreferredPath)) {
        return $PreferredPath
    }

    $candidates = @(
        'C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe'
    )

    $hubCandidates = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'Editor\Unity.exe' }
    $candidates += $hubCandidates

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw "Unity.exe was not found. Pass it explicitly with -UnityExePath."
}

function Resolve-DotnetExePath {
    param([string]$PreferredPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($PreferredPath)) {
        $candidates += $PreferredPath
    }

    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $candidates += (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')
    }

    $pathDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $pathDotnet) {
        $candidates += $pathDotnet.Source
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate)) {
            continue
        }

        $sdkList = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and ($sdkList | Where-Object { $_ -match '^9\.' })) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw '.NET 9 SDK was not found. Pass a dotnet executable with -DotnetExePath.'
}

function Invoke-Dotnet {
    param([string[]]$Arguments)

    & $script:dotnetExe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

function ConvertTo-CustomArgsString {
    param([hashtable]$Values)

    $pairs = foreach ($key in ($Values.Keys | Sort-Object)) {
        "{0}={1}" -f $key, [Uri]::EscapeDataString([string]$Values[$key])
    }

    return '-CustomArgs:' + ($pairs -join ';')
}

function Get-UnityModeArguments {
    if ($InteractiveEditor) {
        return @()
    }

    return @('-batchmode', '-nographics')
}

function Start-UnityProcess {
    param([string[]]$ArgumentList)

    if ($InteractiveEditor) {
        return Start-Process -FilePath $script:unityExe -ArgumentList $ArgumentList -PassThru
    }

    return Start-Process `
        -FilePath $script:unityExe `
        -ArgumentList $ArgumentList `
        -PassThru `
        -WindowStyle Hidden
}

function Test-PortListening {
    param([int]$Port)

    $pattern = '^\s*(?:TCP|UDP)\s+\S+:{0}\s+' -f $Port
    return [bool](netstat -ano | Select-String -Pattern $pattern)
}

function Assert-MongoReady {
    if (-not (Test-PortListening -Port $MongoPort)) {
        throw "MongoDB is not listening on port $MongoPort. Start mongod before running Unity acceptance."
    }
}

function Wait-ServerReady {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ServerLogPath,
        [string]$ServerErrorPath
    )

    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            throw "Server startup failed because the process exited early. Check: $ServerLogPath / $ServerErrorPath"
        }

        if ((Test-PortListening -Port $AuthServerPort) -and
            (Test-PortListening -Port $BattleServerPort)) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Server readiness timed out. Expected Auth:$AuthServerPort and Battle:$BattleServerPort. Check: $ServerLogPath / $ServerErrorPath"
}

function Get-ListeningProcessIds {
    param([int]$Port)

    $pattern = '^\s*(?:TCP|UDP)\s+\S+:{0}\s+.*\s+(\d+)\s*$' -f $Port
    $matches = netstat -ano | Select-String -Pattern $pattern
    $processIds = New-Object System.Collections.Generic.HashSet[int]
    foreach ($match in $matches) {
        if ($match.Matches.Count -eq 0) {
            continue
        }

        $processId = [int]$match.Matches[0].Groups[1].Value
        if ($processId -gt 0) {
            [void]$processIds.Add($processId)
        }
    }

    return @($processIds)
}

function Stop-ExistingBattleServer {
    param([string]$ScenarioName)

    $processIds = Get-ListeningProcessIds -Port $BattleServerPort
    if ($processIds.Count -eq 0) {
        return
    }

    Write-Warning "Scenario '$ScenarioName' is cleaning up existing battle server processes on port ${BattleServerPort}: $($processIds -join ', ')"
    foreach ($processId in $processIds) {
        try {
            Stop-Process -Id $processId -Force -ErrorAction Stop
        }
        catch {
            Write-Warning ("Failed to stop existing battle server process {0}: {1}" -f $processId, $_.Exception.Message)
        }
    }

    Start-Sleep -Seconds 2
}

function Stop-StaleAutomationClients {
    $automationProcesses = Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and (
                $_.CommandLine.IndexOf($editorExecuteMethod, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                $_.CommandLine.IndexOf('battleAutomation=1', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            )
        }

    foreach ($process in $automationProcesses) {
        Write-Warning "Stopping stale automation Unity process: PID=$($process.ProcessId)"
        try {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Failed to stop Unity automation process $($process.ProcessId): $($_.Exception.Message)"
        }
    }

    if ($automationProcesses) {
        Start-Sleep -Seconds 2
    }
}

function Get-ScenarioCustomArgs {
    param([string]$ScenarioName)

    $netsimEnabled = $EnableNetworkSimulation.IsPresent
    $uplinkDelayMs = 0
    $downlinkDelayMs = 0
    $uplinkJitterMs = 0
    $downlinkJitterMs = 0
    $uplinkLossPercent = 0
    $downlinkLossPercent = 0

    switch ($ScenarioName) {
        'two-client-weaknet-delay' {
            $netsimEnabled = $true
            $uplinkDelayMs = 100
            $downlinkDelayMs = 100
            $uplinkJitterMs = 30
            $downlinkJitterMs = 30
        }
        'two-client-weaknet-uplink-loss' {
            $netsimEnabled = $true
            $uplinkLossPercent = 20
        }
        'two-client-weaknet-downlink-loss' {
            $netsimEnabled = $true
            $downlinkLossPercent = 20
        }
        'two-client-rtt-probe' {
            # Server-authoritative RTT measurement under mild bidirectional delay.
            $netsimEnabled = $true
            $uplinkDelayMs = 100
            $downlinkDelayMs = 100
        }
        'two-client-knockback' {
            # Fixed S8 calibration: symmetric delay, no jitter, no packet loss.
            $netsimEnabled = $true
            $uplinkDelayMs = 100
            $downlinkDelayMs = 100
        }

    }

    $overrides = @(
        @{ Name = 'NetSimUplinkDelayMs'; Value = $NetSimUplinkDelayMs; Maximum = [int]::MaxValue },
        @{ Name = 'NetSimDownlinkDelayMs'; Value = $NetSimDownlinkDelayMs; Maximum = [int]::MaxValue },
        @{ Name = 'NetSimUplinkJitterMs'; Value = $NetSimUplinkJitterMs; Maximum = [int]::MaxValue },
        @{ Name = 'NetSimDownlinkJitterMs'; Value = $NetSimDownlinkJitterMs; Maximum = [int]::MaxValue },
        @{ Name = 'NetSimUplinkLossPercent'; Value = $NetSimUplinkLossPercent; Maximum = 100 },
        @{ Name = 'NetSimDownlinkLossPercent'; Value = $NetSimDownlinkLossPercent; Maximum = 100 }
    )
    foreach ($override in $overrides) {
        if ($override.Value -lt -1 -or $override.Value -gt $override.Maximum) {
            throw "$($override.Name) is out of range: $($override.Value)"
        }
    }

    if ($NetSimUplinkDelayMs -ge 0) { $uplinkDelayMs = $NetSimUplinkDelayMs; $netsimEnabled = $true }
    if ($NetSimDownlinkDelayMs -ge 0) { $downlinkDelayMs = $NetSimDownlinkDelayMs; $netsimEnabled = $true }
    if ($NetSimUplinkJitterMs -ge 0) { $uplinkJitterMs = $NetSimUplinkJitterMs; $netsimEnabled = $true }
    if ($NetSimDownlinkJitterMs -ge 0) { $downlinkJitterMs = $NetSimDownlinkJitterMs; $netsimEnabled = $true }
    if ($NetSimUplinkLossPercent -ge 0) { $uplinkLossPercent = $NetSimUplinkLossPercent; $netsimEnabled = $true }
    if ($NetSimDownlinkLossPercent -ge 0) { $downlinkLossPercent = $NetSimDownlinkLossPercent; $netsimEnabled = $true }

    $args = @{
        minimumPlayerCount = $defaultMinimumPlayerCount
        movementDistanceThreshold = $defaultMovementDistanceThreshold
        movementFrames = $defaultMovementFrames
        settleFrames = $defaultSettleFrames
        expectedBuffId = $defaultExpectedBuffId
        buffApplyDelayFrames = $defaultBuffApplyDelayFrames
        buffDurationFrames = $defaultBuffDurationFrames
        disconnectFrame = $defaultDisconnectFrame
        netsimEnabled = $(if ($netsimEnabled) { '1' } else { '0' })
        netsimUplinkDelayMs = [string]$uplinkDelayMs
        netsimDownlinkDelayMs = [string]$downlinkDelayMs
        netsimUplinkJitterMs = [string]$uplinkJitterMs
        netsimDownlinkJitterMs = [string]$downlinkJitterMs
        netsimUplinkLossPercent = [string]$uplinkLossPercent
        netsimDownlinkLossPercent = [string]$downlinkLossPercent
        netsimSeed = [string]$NetSimSeed
    }

    return $args
}

function Resolve-ControllerScriptPath {
    param(
        [string]$ScenarioName,
        [string]$BridgeMode
    )

    if ($BridgeMode -ne 'puerts') {
        return ''
    }

    if (-not [string]::IsNullOrWhiteSpace($ControllerScriptPath)) {
        return $ControllerScriptPath
    }

    $scriptFileName = "$ScenarioName.js.txt"
    $scriptPath = Join-Path $unityProjectPath "Assets\StreamingAssets\BattleAutomation\Puerts\$scriptFileName"

    if (Test-Path -LiteralPath $scriptPath) {
        return $scriptPath
    }

    if ($ScenarioName.StartsWith('two-client-weaknet-', [System.StringComparison]::OrdinalIgnoreCase)) {
        $weakNetworkScriptPath = Join-Path $unityProjectPath 'Assets\StreamingAssets\BattleAutomation\Puerts\weaknet-controller.js.txt'
        if (Test-Path -LiteralPath $weakNetworkScriptPath) {
            return $weakNetworkScriptPath
        }
    }

    return ''
}

function Invoke-UnityMethod {
    param(
        [string]$ProjectPath,
        [string]$ExecuteMethod,
        [string]$LogPath,
        [hashtable]$CustomArgs
    )

    $argumentList = [string[]]@(
        (Get-UnityModeArguments),
        '-projectPath', $ProjectPath,
        '-executeMethod', $ExecuteMethod,
        '-logFile', $LogPath,
        (ConvertTo-CustomArgsString -Values $CustomArgs)
    ).Where({ $_ -ne $null -and $_ -ne '' })

    $process = Start-UnityProcess -ArgumentList $argumentList
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Unity executeMethod failed. method=$ExecuteMethod exitCode=$($process.ExitCode) log=$LogPath"
    }
}

function Ensure-ParrelSyncClone {
    if ($SkipCloneCreation) {
        $existing = Get-ChildItem -LiteralPath (Split-Path $unityProjectPath -Parent) -Directory |
            Where-Object { $_.Name -like 'TEngine_clone_*' } |
            Select-Object -First 1
        if ($null -eq $existing) {
            throw 'SkipCloneCreation was specified, but no TEngine_clone_* project was found.'
        }

        return $existing.FullName
    }

    $clonePathFile = Join-Path $runRoot 'clone-path.txt'
    $cloneLogPath = Join-Path $runRoot 'ensure-clone.log'
    Invoke-UnityMethod `
        -ProjectPath $unityProjectPath `
        -ExecuteMethod $cloneExecuteMethod `
        -LogPath $cloneLogPath `
        -CustomArgs @{ clonePathFile = $clonePathFile }

    if (!(Test-Path -LiteralPath $clonePathFile)) {
        throw "ParrelSync clone path file was not generated: $clonePathFile"
    }

    $clonePath = (Get-Content -LiteralPath $clonePathFile -Encoding UTF8 | Select-Object -First 1).Trim()
    if ([string]::IsNullOrWhiteSpace($clonePath) -or !(Test-Path -LiteralPath $clonePath)) {
        throw "ParrelSync clone path is invalid: $clonePath"
    }

    return $clonePath
}

function Start-ServerProcess {
    param([string]$ScenarioName)

    $serverLogPath = Join-Path $runRoot "$ScenarioName\server.log"
    $serverErrPath = Join-Path $runRoot "$ScenarioName\server.err.log"
    $serverDir = Split-Path $serverLogPath -Parent
    New-Item -ItemType Directory -Path $serverDir -Force | Out-Null

    $scenarioArgs = Get-ScenarioCustomArgs -ScenarioName $ScenarioName
    Assert-MongoReady
    Stop-ExistingBattleServer -ScenarioName $ScenarioName

    if (Test-PortListening -Port $BattleServerPort) {
        throw "Battle port $BattleServerPort is still occupied after cleanup."
    }

    if (Test-PortListening -Port $AuthServerPort) {
        throw "Auth port $AuthServerPort is occupied by another process."
    }

    # 通过环境变量传递 automation 配置，避免与 Fantasy 框架的 CommandLine.Parser 冲突
    $serverScenarioName = $ScenarioName
    if ($ScenarioName -eq 'two-client-weaknet-downlink-loss') {
        $serverScenarioName = 'two-client-buff-lifecycle'
    }

    $serverOutputDirectory = Join-Path (Split-Path $serverProjectPath -Parent) 'bin\Debug\net9.0'
    $serverDllPath = Join-Path $serverOutputDirectory 'Main.dll'
    if (-not (Test-Path -LiteralPath $serverDllPath)) {
        throw "Built server entry was not found: $serverDllPath. Run without -NoBuild first."
    }

    $environmentOverrides = [ordered]@{
        BATTLE_AUTOMATION_SCENARIO = $serverScenarioName
        BATTLE_AUTOMATION_MINIMUM_PLAYER_COUNT = $scenarioArgs.minimumPlayerCount
        BATTLE_AUTOMATION_BUFF_ID = $scenarioArgs.expectedBuffId
        BATTLE_AUTOMATION_BUFF_APPLY_DELAY_FRAMES = $scenarioArgs.buffApplyDelayFrames
        BATTLE_AUTOMATION_BUFF_DURATION_FRAMES = $scenarioArgs.buffDurationFrames
    }
    if ($ScenarioName -eq 'two-client-rtt-probe') {
        $environmentOverrides['BATTLE_RTT_PROBE'] = '1'
        $environmentOverrides['BATTLE_RTT_AUTHORITATIVE_LEAD'] = '1'
    }

    $previousEnvironment = @{}
    try {
        foreach ($name in $environmentOverrides.Keys) {
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, [string]$environmentOverrides[$name], 'Process')
        }

        $process = Start-Process `
            -FilePath $script:dotnetExe `
            -ArgumentList @("`"$serverDllPath`"", '-m', 'Develop') `
            -WorkingDirectory $serverOutputDirectory `
            -RedirectStandardOutput $serverLogPath `
            -RedirectStandardError $serverErrPath `
            -PassThru `
            -WindowStyle Hidden
    }
    finally {
        foreach ($name in $environmentOverrides.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
        }
    }

    Wait-ServerReady -Process $process -ServerLogPath $serverLogPath -ServerErrorPath $serverErrPath

    return @{
        Process = $process
        LogPath = $serverLogPath
        ErrorPath = $serverErrPath
        ReusedExisting = $false
    }
}

function Stop-ServerProcess {
    param($ServerState)

    if ($null -eq $ServerState -or $null -eq $ServerState.Process) {
        return
    }

    if (!$ServerState.Process.HasExited) {
        Stop-Process -Id $ServerState.Process.Id -Force
        $ServerState.Process.WaitForExit()
    }
}

function Start-AutomationClient {
    param(
        [string]$ProjectPath,
        [string]$ScenarioName,
        [string]$ClientId,
        [string]$AuthUserName
    )

    $clientDir = Join-Path $runRoot $ScenarioName
    New-Item -ItemType Directory -Path $clientDir -Force | Out-Null

    $reportPath = Join-Path $clientDir "$ClientId-report.json"
    $eventLogPath = Join-Path $clientDir "$ClientId-events.log"
    $unityLogPath = Join-Path $clientDir "$ClientId-unity.log"
    if (Test-Path -LiteralPath $reportPath) {
        Remove-Item -LiteralPath $reportPath -Force
    }

    $resolvedControllerScript = Resolve-ControllerScriptPath -ScenarioName $ScenarioName -BridgeMode $Bridge

    $lingerSeconds = '30'
    if ($ScenarioName -eq 'two-client-disconnect') {
        $lingerSeconds = '0'
    }

    $scenarioArgs = Get-ScenarioCustomArgs -ScenarioName $ScenarioName

    $customArgs = @{
        battleAutomation = '1'
        authServerAddress = $AuthServerAddress
        authServerPort = [string]$AuthServerPort
        authUserName = $AuthUserName
        authPassword = $TestAccountPassword
        battleServerAddress = $BattleServerAddress
        battleServerPort = [string]$BattleServerPort
        autoOpenBattleUi = '1'
        autoCloseAfterFinish = '1'
        bridge = $Bridge
        clientId = $ClientId
        controllerScriptPath = $resolvedControllerScript
        eventLogPath = $eventLogPath
        lingerSeconds = $lingerSeconds
        minimumPlayerCount = $scenarioArgs.minimumPlayerCount
        movementDistanceThreshold = $scenarioArgs.movementDistanceThreshold
        movementFrames = $scenarioArgs.movementFrames
        reportPath = $reportPath
        scenario = $ScenarioName
        settleFrames = $scenarioArgs.settleFrames
        disconnectFrame = $scenarioArgs.disconnectFrame
        expectedBuffId = $scenarioArgs.expectedBuffId
        buffApplyDelayFrames = $scenarioArgs.buffApplyDelayFrames
        buffDurationFrames = $scenarioArgs.buffDurationFrames
        netsimEnabled = $scenarioArgs.netsimEnabled
        netsimUplinkDelayMs = $scenarioArgs.netsimUplinkDelayMs
        netsimDownlinkDelayMs = $scenarioArgs.netsimDownlinkDelayMs
        netsimUplinkJitterMs = $scenarioArgs.netsimUplinkJitterMs
        netsimDownlinkJitterMs = $scenarioArgs.netsimDownlinkJitterMs
        netsimUplinkLossPercent = $scenarioArgs.netsimUplinkLossPercent
        netsimDownlinkLossPercent = $scenarioArgs.netsimDownlinkLossPercent
        netsimSeed = $scenarioArgs.netsimSeed
        timeoutSeconds = $ClientTimeoutSeconds.ToString()
    }

    $argumentList = [string[]]@(
        (Get-UnityModeArguments),
        '-projectPath', $ProjectPath,
        '-executeMethod', $editorExecuteMethod,
        '-logFile', $unityLogPath,
        (ConvertTo-CustomArgsString -Values $customArgs)
    ).Where({ $_ -ne $null -and $_ -ne '' })

    $process = Start-UnityProcess -ArgumentList $argumentList
    return @{
        ClientId = $ClientId
        Process = $process
        ReportPath = $reportPath
        EventLogPath = $eventLogPath
        UnityLogPath = $unityLogPath
    }
}

function Wait-ClientProcess {
    param($ClientState)

    $deadline = (Get-Date).AddSeconds($ClientTimeoutSeconds + 180)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $ClientState.ReportPath) {
            return Get-Content -LiteralPath $ClientState.ReportPath -Encoding UTF8 | ConvertFrom-Json
        }

        if ($ClientState.Process.HasExited) {
            throw "Client process exited before producing a report: $($ClientState.ClientId) exit=$($ClientState.Process.ExitCode)"
        }

        Start-Sleep -Seconds 2
    }

    throw "Client report was not generated before timeout: $($ClientState.ClientId) path=$($ClientState.ReportPath)"
}

function Stop-ClientProcess {
    param($ClientState)

    if ($null -eq $ClientState -or $null -eq $ClientState.Process) {
        return
    }

    if (!$ClientState.Process.HasExited) {
        Stop-Process -Id $ClientState.Process.Id -Force
        $ClientState.Process.WaitForExit()
    }
}

function Get-NetworkSimulationEvidence {
    param(
        [string]$ScenarioName,
        $ClientAReport,
        $ClientBReport,
        [string]$ServerLogPath
    )

    $isWeakNetworkScenario = $ScenarioName.StartsWith('two-client-weaknet-', [System.StringComparison]::OrdinalIgnoreCase)
    $isKnockbackScenario = $ScenarioName.Equals('two-client-knockback', [System.StringComparison]::OrdinalIgnoreCase)
    if (-not $isWeakNetworkScenario -and -not $isKnockbackScenario) {
        return @{ Passed = $true; Details = 'not-a-weaknet-scenario' }
    }

    $snapshots = @($ClientAReport.snapshot, $ClientBReport.snapshot)
    foreach ($snapshot in $snapshots) {
        if ($null -eq $snapshot -or -not [bool]$snapshot.networkSimulationEnabled) {
            return @{ Passed = $false; Details = 'networkSimulationEnabled was false' }
        }

        if ([UInt64]$snapshot.networkSeed -ne $NetSimSeed) {
            return @{ Passed = $false; Details = "seed mismatch expected=$NetSimSeed actual=$($snapshot.networkSeed)" }
        }
    }

    switch ($ScenarioName) {
        'two-client-weaknet-delay' {
            $passed = ($snapshots | Where-Object {
                [int]$_.leadFrames -le 3 -or [int]$_.networkMaxQueueDepth -le 0
            }).Count -eq 0
            $details = "lead=$($snapshots[0].leadFrames)/$($snapshots[1].leadFrames) maxQueue=$($snapshots[0].networkMaxQueueDepth)/$($snapshots[1].networkMaxQueueDepth)"
        }
        'two-client-weaknet-uplink-loss' {
            $passed = ($snapshots | Where-Object { [long]$_.networkUplinkDropped -le 0 }).Count -eq 0
            $details = "uplinkDropped=$($snapshots[0].networkUplinkDropped)/$($snapshots[1].networkUplinkDropped)"
        }
        'two-client-weaknet-downlink-loss' {
            $passed = ($snapshots | Where-Object { [long]$_.networkDownlinkDropped -le 0 }).Count -eq 0
            $details = "downlinkDropped=$($snapshots[0].networkDownlinkDropped)/$($snapshots[1].networkDownlinkDropped)"
        }
        'two-client-knockback' {
            $passed = ($snapshots | Where-Object {
                [int]$_.networkUplinkDelayMs -ne 100 -or
                [int]$_.networkDownlinkDelayMs -ne 100 -or
                [int]$_.networkUplinkJitterMs -ne 0 -or
                [int]$_.networkDownlinkJitterMs -ne 0 -or
                [int]$_.networkUplinkLossPercent -ne 0 -or
                [int]$_.networkDownlinkLossPercent -ne 0 -or
                [int]$_.dashCount -lt 2 -or
                [int]$_.knockbackTriggerCount -le 0 -or
                [int]$_.stateMismatchCount -gt 3 -or
                [int]$_.contactMismatchFrames -gt 6
            }).Count -eq 0
            $details = "dash=$($snapshots[0].dashCount)/$($snapshots[1].dashCount) " +
                "knockback=$($snapshots[0].knockbackTriggerCount)/$($snapshots[1].knockbackTriggerCount) " +
                "stateMismatch=$($snapshots[0].stateMismatchCount)/$($snapshots[1].stateMismatchCount) " +
                "positionMismatch=$($snapshots[0].positionMismatchCount)/$($snapshots[1].positionMismatchCount) " +
                "contactMismatch=$($snapshots[0].contactMismatchFrames)/$($snapshots[1].contactMismatchFrames)"
        }
        default {
            $passed = $false
            $details = "unknown weaknet scenario: $ScenarioName"
        }
    }

    if ($passed -and $ScenarioName -ne 'two-client-weaknet-downlink-loss' -and (Test-Path -LiteralPath $ServerLogPath)) {
        $hashMismatch = Select-String -LiteralPath $ServerLogPath -Pattern '\[Battle\]\[HashMismatch\]' -ErrorAction SilentlyContinue
        if ($hashMismatch) {
            $passed = $false
            $details += '; unexpected HashMismatch in zero-downlink-loss path'
        }
    }

    return @{ Passed = $passed; Details = $details }
}

function Get-ReconnectEvidence {
    param(
        [string]$ScenarioName,
        $ClientAReport,
        $ClientBReport
    )

    if (-not $ScenarioName.Equals('two-client-reconnect', [System.StringComparison]::OrdinalIgnoreCase)) {
        return @{ Passed = $true; Details = 'not-a-reconnect-scenario' }
    }

    $observer = $ClientAReport.snapshot
    $actor = $ClientBReport.snapshot
    if ($null -eq $observer -or $null -eq $actor) {
        return @{ Passed = $false; Details = 'missing reconnect snapshot' }
    }

    $actorPlayerId = [long]$actor.selfPlayerId
    $playerIdBeforeDisconnect = [long]$actor.playerIdBeforeAutomationDisconnect
    $mismatchGrowth = [Math]::Max(
        0,
        [int]$actor.stateMismatchCount - [int]$actor.stateMismatchCountAtReconnect)
    $rollbackGrowth = [Math]::Max(
        0,
        [int]$actor.rollbackCount - [int]$actor.rollbackCountAtReconnect)
    $observerHasActor = @($observer.players | Where-Object { [long]$_.playerId -eq $actorPlayerId }).Count -eq 1

    $passed = [int]$actor.reconnectCount -eq 1 -and
        [int]$actor.automationControlledDisconnectCount -eq 1 -and
        $actorPlayerId -gt 0 -and
        $actorPlayerId -eq $playerIdBeforeDisconnect -and
        -not [bool]$actor.awaitingFullSnapshot -and
        [int]$actor.framesToConvergeAfterReconnect -ge 0 -and
        [int]$actor.framesToConvergeAfterReconnect -le 30 -and
        $mismatchGrowth -le 3 -and
        $rollbackGrowth -le 3 -and
        [int]$actor.gameplayInputMessagesSentWhileAwaitingFullSnapshot -eq 0 -and
        [int]$observer.activePlayerCount -ge 2 -and
        $observerHasActor

    $details = "playerId=$actorPlayerId/$playerIdBeforeDisconnect " +
        "reconnectCount=$($actor.reconnectCount) " +
        "converge=$($actor.framesToConvergeAfterReconnect)/30 " +
        "mismatchGrowth=$mismatchGrowth/3 rollbackGrowth=$rollbackGrowth/3 " +
        "recoveryInputs=$($actor.gameplayInputMessagesSentWhileAwaitingFullSnapshot) " +
        "observerPlayers=$($observer.activePlayerCount) observerHasActor=$observerHasActor"
    return @{ Passed = $passed; Details = $details }
}

function Wait-BothClients {
    param($ClientA, $ClientB)

    $reportA = $null
    $reportB = $null
    $errorA = $null
    $errorB = $null
    $deadline = (Get-Date).AddSeconds($ClientTimeoutSeconds + 180)

    while ((Get-Date) -lt $deadline) {
        if ($null -eq $reportA -and (Test-Path -LiteralPath $ClientA.ReportPath)) {
            $reportA = Get-Content -LiteralPath $ClientA.ReportPath -Encoding UTF8 | ConvertFrom-Json
        }

        if ($null -eq $reportB -and (Test-Path -LiteralPath $ClientB.ReportPath)) {
            $reportB = Get-Content -LiteralPath $ClientB.ReportPath -Encoding UTF8 | ConvertFrom-Json
        }

        if ($null -ne $reportA -and $null -ne $reportB) {
            Stop-ClientProcess $ClientA
            Stop-ClientProcess $ClientB
            return @{ ReportA = $reportA; ReportB = $reportB }
        }

        if ($null -eq $reportA -and $ClientA.Process.HasExited) {
            $errorA = "Client A exited without report: exit=$($ClientA.Process.ExitCode)"
        }

        if ($null -eq $reportB -and $ClientB.Process.HasExited) {
            $errorB = "Client B exited without report: exit=$($ClientB.Process.ExitCode)"
        }

        if ($null -ne $errorA -and $null -ne $errorB) {
            throw "$errorA; $errorB"
        }

        Start-Sleep -Seconds 2
    }

    Stop-ClientProcess $ClientA
    Stop-ClientProcess $ClientB
    throw "Timeout waiting for both clients"
}

function Write-CombinedReports {
    param([object[]]$ScenarioResults)

    $jsonPath = Join-Path $runRoot 'combined-report.json'
    $markdownPath = Join-Path $runRoot 'combined-report.md'
    $payload = [ordered]@{
        generatedAt = (Get-Date).ToString('o')
        passed = ($ScenarioResults | Where-Object { -not $_.Passed }).Count -eq 0
        scenarios = $ScenarioResults
    }

    $payload | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# Real Client Automation Report')
    $lines.Add('')
    $lines.Add('- Generated At: ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'))
    $lines.Add('- Result: ' + $(if ($payload.passed) { 'PASS' } else { 'FAIL' }))
    $lines.Add('')
    $lines.Add('## Scenario Results')
    $lines.Add('')

    foreach ($result in $ScenarioResults) {
        $lines.Add('- ' + $result.Scenario + ': ' + $(if ($result.Passed) { 'PASS' } else { 'FAIL' }))
        $lines.Add('  serverLog: ' + $result.ServerLogPath)
        $lines.Add('  clientAReport: ' + $result.ClientAReportPath)
        $lines.Add('  clientBReport: ' + $result.ClientBReportPath)
    }

    Set-Content -LiteralPath $markdownPath -Value $lines -Encoding UTF8
    return @{
        JsonPath = $jsonPath
        MarkdownPath = $markdownPath
        Passed = $payload.passed
    }
}

$script:unityExe = Resolve-UnityExePath -PreferredPath $UnityExePath
$script:dotnetExe = Resolve-DotnetExePath -PreferredPath $DotnetExePath

if ([string]::IsNullOrWhiteSpace($ClientAUserName) -or
    [string]::IsNullOrWhiteSpace($ClientBUserName) -or
    [string]::IsNullOrWhiteSpace($TestAccountPassword)) {
    throw 'Both automation usernames and the shared test password must be non-empty.'
}

if ($ClientAUserName.Equals($ClientBUserName, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'ClientAUserName and ClientBUserName must be different accounts.'
}

Assert-MongoReady

$cloneProjectPath = Ensure-ParrelSyncClone

if (-not $NoBuild) {
    Invoke-Dotnet -Arguments @('build', (Join-Path $repoRoot 'GameServer\Server\Server.sln'), '-c', 'Debug', '-v', 'minimal', '-m:1')
    Invoke-Dotnet -Arguments @('build', (Join-Path $repoRoot 'UnityProject\GameLogic.csproj'), '-c', 'Debug', '-v', 'minimal', '-m:1')
    Invoke-Dotnet -Arguments @('build', (Join-Path $repoRoot 'UnityProject\Assembly-CSharp-Editor.csproj'), '-c', 'Debug', '-v', 'minimal', '-m:1')
}
$scenarioResults = New-Object System.Collections.Generic.List[object]

foreach ($scenarioName in $Scenario) {
    $serverState = $null
    try {
        Stop-StaleAutomationClients
        $serverState = Start-ServerProcess -ScenarioName $scenarioName
        $clientA = Start-AutomationClient `
            -ProjectPath $unityProjectPath `
            -ScenarioName $scenarioName `
            -ClientId 'client-a' `
            -AuthUserName $ClientAUserName
        $clientB = Start-AutomationClient `
            -ProjectPath $cloneProjectPath `
            -ScenarioName $scenarioName `
            -ClientId 'client-b' `
            -AuthUserName $ClientBUserName

        $bothReports = Wait-BothClients -ClientA $clientA -ClientB $clientB
        $clientAReport = $bothReports.ReportA
        $clientBReport = $bothReports.ReportB

        $networkEvidence = Get-NetworkSimulationEvidence `
            -ScenarioName $scenarioName `
            -ClientAReport $clientAReport `
            -ClientBReport $clientBReport `
            -ServerLogPath $serverState.LogPath
        $reconnectEvidence = Get-ReconnectEvidence `
            -ScenarioName $scenarioName `
            -ClientAReport $clientAReport `
            -ClientBReport $clientBReport
        $passed = [bool]$clientAReport.passed -and `
            [bool]$clientBReport.passed -and `
            -not $serverState.Process.HasExited -and `
            [bool]$networkEvidence.Passed -and `
            [bool]$reconnectEvidence.Passed
        $scenarioResults.Add([pscustomobject]@{
            Scenario = $scenarioName
            Passed = $passed
            ServerLogPath = $serverState.LogPath
            ServerErrorLogPath = $serverState.ErrorPath
            ClientAReportPath = $clientA.ReportPath
            ClientBReportPath = $clientB.ReportPath
            ClientAUnityLogPath = $clientA.UnityLogPath
            ClientBUnityLogPath = $clientB.UnityLogPath
            ClientAReason = $clientAReport.reason
            ClientBReason = $clientBReport.reason
            NetworkEvidence = $networkEvidence.Details
            ReconnectEvidence = $reconnectEvidence.Details
        })
    }
    finally {
        Stop-ServerProcess -ServerState $serverState
    }
}

$combined = Write-CombinedReports -ScenarioResults $scenarioResults
Write-Output "Combined JSON: $($combined.JsonPath)"
Write-Output "Combined Markdown: $($combined.MarkdownPath)"

if (-not $combined.Passed -and -not $InteractiveEditor) {
    Write-Warning 'If Unity shows LicensingClient errors or exit code 199, retry with -InteractiveEditor.'
}

if (-not $combined.Passed) {
    exit 1
}
