param(
    [string[]]$Scenario = @('two-client-join', 'two-client-basic-move', 'two-client-disconnect'),
    [string]$UnityExePath = '',
    [string]$DotnetExePath = '',
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

$repoRoot = 'D:\unity\Tencent\TEngine'
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

function Test-PortListening {
    param([int]$Port)

    return [bool](netstat -ano | Select-String -Pattern (':{0}\s' -f $Port))
}

function Get-ListeningProcessIds {
    param([int]$Port)

    $pattern = ':{0}\s+.*LISTENING\s+(\d+)$' -f $Port
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

    $processIds = Get-ListeningProcessIds -Port 20101
    if ($processIds.Count -eq 0) {
        return
    }

    Write-Warning "Scenario '$ScenarioName' is cleaning up existing battle server processes on port 20101: $($processIds -join ', ')"
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

    $process = Start-Process -FilePath $script:unityExe -ArgumentList $argumentList -PassThru -WindowStyle Hidden
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
    Stop-ExistingBattleServer -ScenarioName $ScenarioName

    # 通过环境变量传递 automation 配置，避免与 Fantasy 框架的 CommandLine.Parser 冲突
    $serverScenarioName = $ScenarioName
    if ($ScenarioName -eq 'two-client-weaknet-downlink-loss') {
        $serverScenarioName = 'two-client-buff-lifecycle'
    }

    $escapedDotnetExe = $script:dotnetExe.Replace("'", "''")
    $serverCommand = @(
        '$env:BATTLE_AUTOMATION_SCENARIO = ''' + $serverScenarioName + ''';',
        '$env:BATTLE_AUTOMATION_MINIMUM_PLAYER_COUNT = ''' + $scenarioArgs.minimumPlayerCount + ''';',
        '$env:BATTLE_AUTOMATION_BUFF_ID = ''' + $scenarioArgs.expectedBuffId + ''';',
        '$env:BATTLE_AUTOMATION_BUFF_APPLY_DELAY_FRAMES = ''' + $scenarioArgs.buffApplyDelayFrames + ''';',
        '$env:BATTLE_AUTOMATION_BUFF_DURATION_FRAMES = ''' + $scenarioArgs.buffDurationFrames + ''';',
        '& ''' + $escapedDotnetExe + ''' run',
        '--project "' + $serverProjectPath + '"',
        $(if ($NoBuild) { '--no-build' } else { '' }),
        '-- -m Develop',
        '1>> "' + $serverLogPath + '"',
        '2>> "' + $serverErrPath + '"'
    ) -join ' '

    $process = Start-Process `
        -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' `
        -ArgumentList @('-NoProfile', '-Command', $serverCommand) `
        -PassThru `
        -WindowStyle Hidden

    Start-Sleep -Seconds 6
    if ($process.HasExited) {
        throw "Server startup failed because the process exited early. Check: $serverLogPath / $serverErrPath"
    }

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
        [string]$ClientId
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
        battleServerAddress = '127.0.0.1'
        battleServerPort = '20101'
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

    $process = Start-Process -FilePath $script:unityExe -ArgumentList $argumentList -PassThru -WindowStyle Hidden
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

    if (-not $ScenarioName.StartsWith('two-client-weaknet-', [System.StringComparison]::OrdinalIgnoreCase)) {
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

if (-not $NoBuild) {
    Invoke-Dotnet -Arguments @('build', (Join-Path $repoRoot 'GameServer\Server\Server.sln'), '-c', 'Debug', '-v', 'minimal', '-m:1')
    Invoke-Dotnet -Arguments @('build', (Join-Path $repoRoot 'UnityProject\GameLogic.csproj'), '-c', 'Debug', '-v', 'minimal', '-m:1')
    Invoke-Dotnet -Arguments @('build', (Join-Path $repoRoot 'UnityProject\Assembly-CSharp-Editor.csproj'), '-c', 'Debug', '-v', 'minimal', '-m:1')
}

$cloneProjectPath = Ensure-ParrelSyncClone
$scenarioResults = New-Object System.Collections.Generic.List[object]

foreach ($scenarioName in $Scenario) {
    $serverState = $null
    try {
        Stop-StaleAutomationClients
        $serverState = Start-ServerProcess -ScenarioName $scenarioName
        $clientA = Start-AutomationClient -ProjectPath $unityProjectPath -ScenarioName $scenarioName -ClientId 'client-a'
        $clientB = Start-AutomationClient -ProjectPath $cloneProjectPath -ScenarioName $scenarioName -ClientId 'client-b'

        $bothReports = Wait-BothClients -ClientA $clientA -ClientB $clientB
        $clientAReport = $bothReports.ReportA
        $clientBReport = $bothReports.ReportB

        $networkEvidence = Get-NetworkSimulationEvidence `
            -ScenarioName $scenarioName `
            -ClientAReport $clientAReport `
            -ClientBReport $clientBReport `
            -ServerLogPath $serverState.LogPath
        $passed = [bool]$clientAReport.passed -and `
            [bool]$clientBReport.passed -and `
            -not $serverState.Process.HasExited -and `
            [bool]$networkEvidence.Passed
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
