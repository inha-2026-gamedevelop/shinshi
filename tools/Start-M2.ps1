<#
.SYNOPSIS
Starts one Windows dedicated server and N Windows clients with separate Unity logs.
.DESCRIPTION
Build the current code with Shinshi/Build/Windows Server (Development) and
Shinshi/Build/Windows Client (Development) first. This script never builds Unity.
It uses the Builds/windows-server and Builds/windows-client outputs from ShinshiBuild.
Linux .x86_64 servers cannot run here; use Linux for those builds and M3 verification.

The server must write its ServerBoot ready message before clients are started.
Client auto-connect is a development-build feature. Check every client log for
connection success; process launch alone does not establish M2 verification.
ClientCount 1 is M1; 2 or more is M2. DurationSeconds 0 leaves processes running.
ContentVersion must exactly match the GameDB.ContentVersion included in both builds.
Server settings and conventional credential variables are removed from client environments.
Arguments, environment values, and original process exceptions are never logged.
.EXAMPLE
.\tools\Start-M2.ps1 -BackendBaseUrl http://127.0.0.1:5000 -ContentVersion 0.1.0 -DungeonPoolSize 1 -VisibleClients
.EXAMPLE
.\tools\Start-M2.ps1 -BackendBaseUrl http://127.0.0.1:5000 -ContentVersion 0.1.0 -DungeonPoolSize 1 -ClientCount 4 -Role dungeon -DurationSeconds 60
.EXAMPLE
.\tools\Start-M2.ps1 -BackendBaseUrl http://127.0.0.1:5000 -ContentVersion 0.1.0 -DungeonPoolSize 1 -ValidateOnly
#>
[CmdletBinding()]
param(
    [string] $BackendBaseUrl,
    [string] $ContentVersion,
    [string] $DungeonPoolSize,
    [string] $ClientCount = '2',
    [string] $Role = 'town',
    [string] $Port = '7777',
    [string] $Instance = 'm2-server',
    [string] $ServerStartupTimeoutSeconds = '30',
    [string] $DurationSeconds = '0',
    [switch] $VisibleClients,
    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-IntegerSetting {
    param([string] $Value, [string] $Name, [int] $Minimum, [int] $Maximum)

    $parsed = 0
    if (($Value -notmatch '\A[0-9]+\z') -or
        (-not [int]::TryParse($Value, [ref] $parsed)) -or
        ($parsed -lt $Minimum) -or ($parsed -gt $Maximum)) {
        throw "$Name must be an integer between $Minimum and $Maximum."
    }
    return $parsed
}

function Assert-IdentifierSetting {
    param([string] $Value, [string] $Name)

    if ($Value -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._+-]{0,127}\z') {
        throw "$Name is required and must be a 1-128 character ASCII identifier."
    }
}

function ConvertTo-WindowsArgumentString {
    param([string[]] $Values)

    $quoted = foreach ($value in $Values) {
        # Start-Process joins ArgumentList; quote explicitly for the Windows argv parser.
        $escaped = [regex]::Replace($value, '(\\*)"', '${1}${1}\"')
        $escaped = [regex]::Replace($escaped, '(\\+)$', '${1}${1}')
        '"' + $escaped + '"'
    }
    return $quoted -join ' '
}

function Start-M2Process {
    param([string] $Executable, [string[]] $Arguments, [bool] $Client, [bool] $Visible)

    $savedEnvironment = @{}
    try {
        if ($Client) {
            $serverSettings = @('SERVER_ROLE', 'GAME_PORT', 'INSTANCE_ID',
                'BACKEND_BASE_URL', 'CONTENT_VERSION', 'DUNGEON_POOL_SIZE')
            $environment = [Environment]::GetEnvironmentVariables('Process')
            foreach ($name in $environment.Keys) {
                # Remove server settings and conventional credential variables without reading them into output.
                if (($serverSettings -contains $name) -or
                    ($name -match '^(ENV_SERVER_|SERVER_|BACKEND_|DATABASE_|DB_|POSTGRES_)') -or
                    ($name -match '(TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|CONNECTION_?STRING|(^|_)KEY($|_)|ACCESS_KEY|PRIVATE_KEY)')) {
                    $savedEnvironment[$name] = $environment[$name]
                    [Environment]::SetEnvironmentVariable($name, $null, 'Process')
                }
            }
        }
        $windowStyle = if ($Visible) { 'Normal' } else { 'Hidden' }
        return Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) `
            -ArgumentList (ConvertTo-WindowsArgumentString $Arguments) -WindowStyle $windowStyle -PassThru
    }
    catch {
        throw 'A test process could not be started. Check the Windows Development build and file access.'
    }
    finally {
        foreach ($name in $savedEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
        }
    }
}

function Stop-M2Processes {
    param([System.Collections.Generic.List[System.Diagnostics.Process]] $Processes)

    foreach ($process in $Processes) {
        try {
            # The retained process handle avoids killing an unrelated process after PID reuse.
            if (-not $process.HasExited) {
                $process.Kill()
                $process.WaitForExit(5000) | Out-Null
            }
        }
        catch {
            Write-Warning 'A test process could not be stopped; check the reported test process IDs.'
        }
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'This script supports Windows server and client builds only. Linux builds require a Linux runner.'
}

if ($Role -notin @('town', 'dungeon')) {
    throw 'Role must be town or dungeon.'
}
Assert-IdentifierSetting $Instance 'Instance'
Assert-IdentifierSetting $ContentVersion 'ContentVersion'
$clientTotal = Read-IntegerSetting $ClientCount 'ClientCount' 1 100
$gamePort = Read-IntegerSetting $Port 'Port' 1 65535
$poolTotal = Read-IntegerSetting $DungeonPoolSize 'DungeonPoolSize' 1 ([int]::MaxValue)
$startupTimeout = Read-IntegerSetting $ServerStartupTimeoutSeconds 'ServerStartupTimeoutSeconds' 1 300
$duration = Read-IntegerSetting $DurationSeconds 'DurationSeconds' 0 ([int]::MaxValue)

$backendUri = $null
if ([string]::IsNullOrWhiteSpace($BackendBaseUrl) -or
    ($BackendBaseUrl -match '[\s\p{Cc}]') -or
    (-not [Uri]::TryCreate($BackendBaseUrl, [UriKind]::Absolute, [ref] $backendUri)) -or
    ($backendUri.Scheme -notin @('http', 'https')) -or
    (-not [string]::IsNullOrEmpty($backendUri.UserInfo)) -or
    (-not [string]::IsNullOrEmpty($backendUri.Query)) -or
    (-not [string]::IsNullOrEmpty($backendUri.Fragment))) {
    throw 'BackendBaseUrl is required and must be an absolute HTTP(S) URL without credentials, query, fragment, or whitespace.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$serverExecutable = Join-Path $projectRoot 'Builds/windows-server/Shinshi.exe'
$clientExecutable = Join-Path $projectRoot 'Builds/windows-client/Shinshi.exe'
if ((-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf)) -or
    (-not (Test-Path -LiteralPath $clientExecutable -PathType Leaf))) {
    throw 'Windows builds are missing. Use Shinshi/Build/Windows Server (Development) and Windows Client (Development). A Linux server build cannot run on Windows.'
}

$grade = if ($clientTotal -eq 1) { 'M1' } else { 'M2' }
if ($ValidateOnly) {
    Write-Output "$grade launch inputs and Windows build paths validated. No process was started."
    return
}

$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$logDirectory = Join-Path $PSScriptRoot (Join-Path 'Logs/M2' $runName)
try {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
}
catch {
    throw 'The test log directory could not be created.'
}

$processes = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$serverLog = Join-Path $logDirectory 'server.log'
$serverArguments = @('-batchmode', '-nographics', '-role', $Role, '-port', "$gamePort",
    '-instance', $Instance, '-backend-base-url', $BackendBaseUrl, '-content-version', $ContentVersion,
    '-dungeon-pool-size', "$poolTotal", '-logFile', $serverLog)
$clientLogs = New-Object 'System.Collections.Generic.List[string]'
$leaveRunning = $false

try {
    $server = Start-M2Process $serverExecutable $serverArguments $false $false
    $processes.Add($server)
    $deadline = [DateTime]::UtcNow.AddSeconds($startupTimeout)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($server.HasExited) {
            throw 'The server exited before it became ready. Inspect server.log.'
        }
        if (Test-Path -LiteralPath $serverLog -PathType Leaf) {
            # Match the exact ServerBootstrap success marker; Unicode escapes keep PS 5.1 source ASCII.
            $ready = Select-String -LiteralPath $serverLog -Encoding UTF8 `
                -Pattern '\[ServerBoot\] \uC11C\uBC84 \uC2DC\uC791 - ' -Quiet
            if ($ready) { break }
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) {
        throw 'The server did not become ready before the startup timeout. Inspect server.log.'
    }

    for ($index = 1; $index -le $clientTotal; ++$index) {
        if ($server.HasExited) {
            throw 'The server exited while clients were starting. Inspect server.log.'
        }
        $clientLog = Join-Path $logDirectory ("client-{0:D2}.log" -f $index)
        $clientLogs.Add($clientLog)
        $clientArguments = @('-connect', '127.0.0.1', '-connect-role', $Role, '-port', "$gamePort", '-logFile', $clientLog,
            '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0')
        $client = Start-M2Process $clientExecutable $clientArguments $true $VisibleClients.IsPresent
        $processes.Add($client)
    }

    Write-Output "$grade processes launched. Connection and gameplay verification remain in the Unity logs."
    Write-Output ([pscustomobject] @{
        ServerProcessId = $server.Id
        ClientProcessIds = @($processes | Select-Object -Skip 1 | ForEach-Object { $_.Id })
        ServerLog = $serverLog
        ClientLogs = $clientLogs.ToArray()
    })

    if ($duration -gt 0) {
        $deadline = [DateTime]::UtcNow.AddSeconds($duration)
        while ([DateTime]::UtcNow -lt $deadline) {
            foreach ($process in $processes) {
                if ($process.HasExited) {
                    throw 'A test process exited during the timed run. Inspect its Unity log.'
                }
            }
            Start-Sleep -Milliseconds 250
        }
        Write-Output 'The timed run ended. Stopping the test processes.'
    }
    else {
        $leaveRunning = $true
    }
}
finally {
    if (-not $leaveRunning) {
        Stop-M2Processes $processes
    }
    foreach ($process in $processes) {
        $process.Dispose()
    }
}
