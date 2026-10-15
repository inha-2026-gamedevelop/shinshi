<#
.SYNOPSIS
Runs the local release stack: PostgreSQL (Docker), the backend API, one Windows town server, dungeon servers, and Windows clients.
.DESCRIPTION
After checking out the release branch, run this script once to play the full client flow
(Lobby -> CharacterSelect -> CharacterCreate -> ShinshiTown) against a real dedicated server.

What it does, in order:
  1. Optionally builds Builds/windows-server and Builds/windows-client (-Build). The Unity editor must be closed.
  2. Reads protocolVersion/contentHash/buildId from each build-identity.json and checks that both builds match.
  3. Creates local secrets once under %LOCALAPPDATA%\Shinshi\release-stack (database password, auth hash key,
     server service token). They are never printed and never written inside the repository.
  4. Starts (or reuses) the Docker container shinshi-release-db, applies SQL migrations, and opens the
     service state (maintenance off, allowed build ids = dev-local + both build ids).
  5. Starts the API, then the town server (API join-token verification, server pool registration).
  6. Starts the dungeon servers (-DungeonCount, default 1) under tools/Watch-ReleaseDungeons.ps1, a hidden
     background watcher. A dungeon server exits by design after one expedition is settled, so the watcher starts
     a new process for the same instance id within a few seconds (the next expedition can then reserve it).
     Each dungeon slot has its own instance id, game port (-DungeonPort + slot - 1) and a log file per process.
  7. Starts the requested number of development clients. In each client use the Lobby development login:
     enter an id, password, and email, then press the sign-up button the first time.

Development builds are used on purpose: release (non-development) clients only accept the launcher session.
Stop everything with -Stop (it stops the dungeon watcher first, then every dungeon process). Add -RemoveDatabase
to also delete the container and its data. Logs and pid files are under %LOCALAPPDATA%\Shinshi\release-stack:
town.log, dungeon-<slot>-<time>.log (newest 5 per slot), dungeon-watcher.log.
.EXAMPLE
.\tools\Start-ReleaseStack.ps1 -Build
.EXAMPLE
.\tools\Start-ReleaseStack.ps1 -ClientCount 2
.EXAMPLE
.\tools\Start-ReleaseStack.ps1 -PublicAddress 192.168.0.10 -ClientCount 0
.EXAMPLE
.\tools\Start-ReleaseStack.ps1 -Stop -RemoveDatabase
#>
[CmdletBinding()]
param(
    [switch] $Build,
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe',
    [string] $ClientCount = '1',
    [string] $PublicAddress = '127.0.0.1',
    [string] $ApiPort = '5080',
    [string] $GamePort = '7777',
    [string] $DungeonCount = '1',
    [string] $DungeonPort = '7780',
    [string] $DatabasePort = '55440',
    [string] $ContentVersion = '0.1.0',
    [switch] $Stop,
    [switch] $RemoveDatabase,
    [string] $StateDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ContainerName = 'shinshi-release-db'
$DatabaseName = 'shinshi'
$DatabaseUser = 'shinshi'
$DevBuildId = 'dev-local'
$StartupTimeoutSeconds = 60

$projectRoot = Split-Path -Parent $PSScriptRoot
$stateDirectory = if ([string]::IsNullOrEmpty($StateDirectory)) { Join-Path $env:LOCALAPPDATA 'Shinshi\release-stack' } else { $StateDirectory }
$watcherScript = Join-Path $PSScriptRoot 'Watch-ReleaseDungeons.ps1'
$serverExecutable = Join-Path $projectRoot 'Builds\windows-server\Shinshi.exe'
$clientExecutable = Join-Path $projectRoot 'Builds\windows-client\Shinshi.exe'

function Read-IntegerSetting {
    param([string] $Value, [string] $Name, [int] $Minimum, [int] $Maximum)

    $parsed = 0
    if (($Value -notmatch '\A[0-9]+\z') -or (-not [int]::TryParse($Value, [ref] $parsed)) -or
        ($parsed -lt $Minimum) -or ($parsed -gt $Maximum)) {
        throw "$Name must be an integer between $Minimum and $Maximum."
    }
    return $parsed
}

function New-RandomBytes {
    param([int] $Count)

    $bytes = New-Object byte[] $Count
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return ,$bytes
}

function New-Secret {
    param([int] $ByteCount)

    # URL-safe text so the value can sit in a connection string without escaping
    return [Convert]::ToBase64String((New-RandomBytes $ByteCount)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Read-OrCreateSecret {
    param([string] $Name, [scriptblock] $Factory)

    $path = Join-Path $stateDirectory $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        [IO.File]::WriteAllText($path, (& $Factory))
    }
    return [IO.File]::ReadAllText($path).Trim()
}

function Stop-SavedProcess {
    param([string] $Name)

    $pidPath = Join-Path $stateDirectory "$Name.pid"
    if (-not (Test-Path -LiteralPath $pidPath -PathType Leaf)) {
        return
    }

    foreach ($line in Get-Content -LiteralPath $pidPath) {
        $parts = $line.Split(' ')
        $process = Get-Process -Id ([int]$parts[0]) -ErrorAction SilentlyContinue
        # The saved start time avoids killing an unrelated process after PID reuse
        if (($null -ne $process) -and ($process.StartTime.ToFileTimeUtc().ToString() -eq $parts[1])) {
            # dotnet run starts the API as a child process, so stop the whole tree
            & taskkill /PID $process.Id /T /F 2>$null | Out-Null
            $process.WaitForExit(5000) | Out-Null
        }
    }
    Remove-Item -LiteralPath $pidPath
}

function Stop-DungeonProcesses {
    # The watcher goes first; otherwise it would start a new dungeon right after the old one is killed.
    # dungeon.pid is the name used before the watcher existed (a hand-started dungeon).
    Stop-SavedProcess 'dungeon-watcher'
    $names = New-Object 'System.Collections.Generic.List[string]'
    $names.Add('dungeon')
    foreach ($file in Get-ChildItem -LiteralPath $stateDirectory -Filter 'dungeon-*.pid' -File -ErrorAction SilentlyContinue) {
        $names.Add([IO.Path]::GetFileNameWithoutExtension($file.Name))
    }
    foreach ($name in $names) {
        Stop-SavedProcess $name
    }
}

function Test-PortInUse {
    param([int] $Port)

    $properties = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties()
    foreach ($endpoint in $properties.GetActiveUdpListeners()) {
        if ($endpoint.Port -eq $Port) { return $true }
    }
    foreach ($endpoint in $properties.GetActiveTcpListeners()) {
        if ($endpoint.Port -eq $Port) { return $true }
    }
    return $false
}

function Save-ProcessId {
    param([string] $Name, [System.Diagnostics.Process] $Process)

    Add-Content -LiteralPath (Join-Path $stateDirectory "$Name.pid") `
        -Value ("{0} {1}" -f $Process.Id, $Process.StartTime.ToFileTimeUtc())
}

function Read-BuildIdentity {
    param([string] $Executable)

    $folder = Split-Path -Parent $Executable
    $name = [IO.Path]::GetFileNameWithoutExtension($Executable)
    $path = Join-Path $folder ($name + '_Data\StreamingAssets\build-identity.json')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "build-identity.json is missing next to $Executable. Rebuild with -Build."
    }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Invoke-UnityBuild {
    param([string] $Target)

    $log = Join-Path $stateDirectory "build-$Target.log"
    $arguments = @('-batchmode', '-quit', '-projectPath', "`"$projectRoot`"", '-logFile', "`"$log`"",
        '-executeMethod', 'Shinshi.Editor.ShinshiBuild.BuildFromCommandLine', '-shinshiTarget', $Target)
    Write-Output "Building $Target (this takes several minutes)..."
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Unity build $Target failed (exit $($process.ExitCode)). Inspect $log"
    }
}

function Wait-Until {
    param([scriptblock] $Condition, [string] $Failure, [System.Diagnostics.Process] $Process, [int] $TimeoutSeconds)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (($null -ne $Process) -and $Process.HasExited) {
            throw $Failure
        }
        if (& $Condition) {
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw $Failure
}

function Invoke-Psql {
    param([string] $Sql)

    $output = & docker exec $ContainerName psql -U $DatabaseUser -d $DatabaseName -v ON_ERROR_STOP=1 -Atc $Sql
    if ($LASTEXITCODE -ne 0) {
        throw 'A database command failed.'
    }
    return $output
}

if ($env:OS -ne 'Windows_NT') {
    throw 'This script runs Windows builds only. Use deploy/ for Linux servers.'
}

New-Item -ItemType Directory -Force -Path $stateDirectory | Out-Null

if ($Stop) {
    Stop-SavedProcess 'clients'
    Stop-DungeonProcesses
    Stop-SavedProcess 'town'
    Stop-SavedProcess 'api'
    if ($RemoveDatabase) {
        & docker rm -f $ContainerName 2>$null | Out-Null
        Remove-Item -LiteralPath (Join-Path $stateDirectory 'db_password') -ErrorAction SilentlyContinue
    }
    Write-Output 'Release stack stopped.'
    return
}

$clientTotal = Read-IntegerSetting $ClientCount 'ClientCount' 0 8
$apiPortNumber = Read-IntegerSetting $ApiPort 'ApiPort' 1 65535
$gamePortNumber = Read-IntegerSetting $GamePort 'GamePort' 1 65535
$dungeonTotal = Read-IntegerSetting $DungeonCount 'DungeonCount' 0 8
$dungeonPortNumber = Read-IntegerSetting $DungeonPort 'DungeonPort' 1 65535
$databasePortNumber = Read-IntegerSetting $DatabasePort 'DatabasePort' 1 65535
if ([Uri]::CheckHostName($PublicAddress) -eq [UriHostNameType]::Unknown) {
    throw 'PublicAddress must be a host name or IP address that clients can reach.'
}

foreach ($tool in @('docker', 'dotnet')) {
    if ($null -eq (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is required. Install Docker Desktop and the .NET 10 SDK."
    }
}

if ($dungeonTotal -gt 0) {
    $dungeonLast = $dungeonPortNumber + $dungeonTotal - 1
    if ($dungeonLast -gt 65535) {
        throw 'DungeonPort + DungeonCount exceeds 65535.'
    }
    foreach ($used in @($gamePortNumber, $apiPortNumber, $databasePortNumber)) {
        if (($used -ge $dungeonPortNumber) -and ($used -le $dungeonLast)) {
            throw "Dungeon ports $dungeonPortNumber-$dungeonLast collide with port $used. Change -DungeonPort."
        }
    }
}

# A previous run would keep the ports busy
Stop-SavedProcess 'clients'
Stop-DungeonProcesses
Stop-SavedProcess 'town'
Stop-SavedProcess 'api'

if ($Build) {
    $lockPath = Join-Path $projectRoot 'Temp\UnityLockfile'
    if (Test-Path -LiteralPath $lockPath) {
        $lock = $null
        try { $lock = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None') } catch { }
        if ($null -eq $lock) {
            throw 'The Unity editor has this project open. Close it before -Build, or build from the Shinshi/Build menu.'
        }
        $lock.Dispose()
    }
    if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
        throw "Unity was not found at $UnityPath. Pass -UnityPath."
    }
    Invoke-UnityBuild 'windows-server'
    Invoke-UnityBuild 'windows-client'
}

if ((-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf)) -or
    (($clientTotal -gt 0) -and (-not (Test-Path -LiteralPath $clientExecutable -PathType Leaf)))) {
    throw 'Windows builds are missing. Run with -Build, or use Shinshi/Build/Windows Server and Windows Client (Development).'
}

$serverIdentity = Read-BuildIdentity $serverExecutable
$allowedBuilds = New-Object 'System.Collections.Generic.List[string]'
$allowedBuilds.Add($DevBuildId)
$allowedBuilds.Add([string]$serverIdentity.buildId)
if ($clientTotal -gt 0) {
    $clientIdentity = Read-BuildIdentity $clientExecutable
    if (($clientIdentity.contentHash -ne $serverIdentity.contentHash) -or
        ($clientIdentity.protocolVersion -ne $serverIdentity.protocolVersion)) {
        throw 'Client and server builds have different content or protocol. Rebuild both with -Build.'
    }
    $allowedBuilds.Add([string]$clientIdentity.buildId)
}

$databasePassword = Read-OrCreateSecret 'db_password' { New-Secret 24 }
$authHashKey = Read-OrCreateSecret 'auth_hash_key' { [Convert]::ToBase64String((New-RandomBytes 32)) }
$adminTotpKey = Read-OrCreateSecret 'admin_totp_key' { [Convert]::ToBase64String((New-RandomBytes 32)) }
$serviceToken = Read-OrCreateSecret 'service_token' { New-Secret 32 }
$serviceId = Read-OrCreateSecret 'service_id' { [Guid]::NewGuid().ToString() }
$instanceId = Read-OrCreateSecret 'instance_id' { [Guid]::NewGuid().ToString() }
# One fixed service/instance id pair per dungeon slot. The server pool lets a new boot of the same instance id take
# over a Draining dungeon whose expedition is settled, so ids are reused across restarts. Slot 1 keeps the
# original file names (dungeon_instance_id, dungeon_service_id); slot N > 1 adds the suffix _N.
$dungeonSlots = @()
for ($i = 1; $i -le $dungeonTotal; ++$i) {
    $suffix = if ($i -eq 1) { '' } else { "_$i" }
    $dungeonSlots += @{
        Service  = Read-OrCreateSecret "dungeon_service_id$suffix" { [Guid]::NewGuid().ToString() }
        Instance = Read-OrCreateSecret "dungeon_instance_id$suffix" { [Guid]::NewGuid().ToString() }
    }
}
$serviceTokenPath = Join-Path $stateDirectory 'service_token'
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $serviceTokenHash = [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($serviceToken)))
}
finally {
    $sha.Dispose()
}

# Database
$state = (& docker ps -a --filter "name=^/$ContainerName$" --format '{{.State}}')
if ([string]::IsNullOrEmpty($state)) {
    Write-Output "Starting PostgreSQL container $ContainerName on 127.0.0.1:$databasePortNumber..."
    & docker run -d --name $ContainerName -e "POSTGRES_USER=$DatabaseUser" -e "POSTGRES_DB=$DatabaseName" `
        -e "POSTGRES_PASSWORD=$databasePassword" -p "127.0.0.1:${databasePortNumber}:5432" postgres:18 | Out-Null
}
elseif ($state -ne 'running') {
    & docker start $ContainerName | Out-Null
}
if ($LASTEXITCODE -ne 0) {
    throw 'The PostgreSQL container could not be started. Check Docker Desktop and the database port.'
}
Wait-Until { & docker exec $ContainerName pg_isready -U $DatabaseUser -d $DatabaseName *> $null; $LASTEXITCODE -eq 0 } `
    'PostgreSQL did not become ready.' $null $StartupTimeoutSeconds

$connection = "Host=127.0.0.1;Port=$databasePortNumber;Username=$DatabaseUser;Password=$databasePassword;Database=$DatabaseName"
$apiProject = Join-Path $projectRoot 'Backend\src\Shinshi.Api'
$env:ConnectionStrings__PostgresMigrator = $connection
try {
    & dotnet run --project $apiProject -- --migrate | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Database migration failed. Run dotnet build Backend and retry.'
    }
}
finally {
    Remove-Item Env:ConnectionStrings__PostgresMigrator -ErrorAction SilentlyContinue
}
$buildArray = ($allowedBuilds | Select-Object -Unique | ForEach-Object { '"' + $_ + '"' }) -join ','
Invoke-Psql "update service_state set maintenance = false, allowed_build_ids = '{$buildArray}' where id = 1" | Out-Null

# API: secrets go only into the child process environment
$listenHost = if (($PublicAddress -eq '127.0.0.1') -or ($PublicAddress -eq 'localhost')) { '127.0.0.1' } else { '0.0.0.0' }
$apiBaseUrl = "http://127.0.0.1:$apiPortNumber"
$publicApiUrl = "http://${PublicAddress}:$apiPortNumber"
$apiSettings = [ordered]@{
    'ConnectionStrings__Postgres'                               = $connection
    'Auth__HashKeyBase64'                                       = $authHashKey
    'Admin__TotpKeyBase64'                                      = $adminTotpKey
    'AuthBuild__ProtocolVersion'                                = [string]$serverIdentity.protocolVersion
    'AuthBuild__ContentHash'                                    = [string]$serverIdentity.contentHash
    'ServiceState__Maintenance'                                 = 'false'
    'ServiceState__AllowedBuildIds__0'                          = $DevBuildId
    'ServerCredentials__Registrations__0__ServiceId'            = $serviceId
    'ServerCredentials__Registrations__0__InstanceId'           = $instanceId
    'ServerCredentials__Registrations__0__Role'                 = '0'
    'ServerCredentials__Registrations__0__CredentialHashBase64' = $serviceTokenHash
    'ASPNETCORE_URLS'                                           = "http://${listenHost}:$apiPortNumber"
    'ASPNETCORE_ENVIRONMENT'                                    = 'Development'
}
# Dungeon slots share the town's service token; the API matches by instance id and role (1 = dungeon)
for ($i = 0; $i -lt $dungeonSlots.Count; ++$i) {
    $registration = $i + 1
    $apiSettings["ServerCredentials__Registrations__${registration}__ServiceId"]            = $dungeonSlots[$i].Service
    $apiSettings["ServerCredentials__Registrations__${registration}__InstanceId"]           = $dungeonSlots[$i].Instance
    $apiSettings["ServerCredentials__Registrations__${registration}__Role"]                 = '1'
    $apiSettings["ServerCredentials__Registrations__${registration}__CredentialHashBase64"] = $serviceTokenHash
}
$apiLog = Join-Path $stateDirectory 'api.log'
foreach ($key in $apiSettings.Keys) { [Environment]::SetEnvironmentVariable($key, $apiSettings[$key], 'Process') }
try {
    $api = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', "`"$apiProject`"") `
        -RedirectStandardOutput $apiLog -RedirectStandardError (Join-Path $stateDirectory 'api.err.log') `
        -WindowStyle Hidden -PassThru
}
finally {
    foreach ($key in $apiSettings.Keys) { [Environment]::SetEnvironmentVariable($key, $null, 'Process') }
}
Save-ProcessId 'api' $api
Write-Output "Starting API on $publicApiUrl..."
Wait-Until {
    try { (Invoke-WebRequest -UseBasicParsing -Uri "$apiBaseUrl/v1/service-state" -TimeoutSec 2).StatusCode -eq 200 } catch { $false }
} "The API did not start. Inspect $apiLog" $api 180

# Town server
$townLog = Join-Path $stateDirectory 'town.log'
Remove-Item -LiteralPath $townLog -ErrorAction SilentlyContinue
$townArguments = @('-batchmode', '-nographics', '-logFile', "`"$townLog`"", '-role', 'town', '-port', "$gamePortNumber",
    '-instance', $instanceId, '-backend-base-url', $apiBaseUrl, '-content-version', $ContentVersion,
    '-dungeon-pool-size', "$([Math]::Max(1, $dungeonTotal))", '-backend-service-token-file', "`"$serviceTokenPath`"", '-join-verifier', 'api',
    '-public-address', $PublicAddress)
$town = Start-Process -FilePath $serverExecutable -WorkingDirectory (Split-Path -Parent $serverExecutable) `
    -ArgumentList $townArguments -WindowStyle Hidden -PassThru
Save-ProcessId 'town' $town
Write-Output "Starting town server on ${PublicAddress}:$gamePortNumber..."
# Match the ServerBootstrap success marker; Unicode escapes keep this file ASCII for Windows PowerShell 5.1
Wait-Until {
    (Test-Path -LiteralPath $townLog -PathType Leaf) -and
    (Select-String -LiteralPath $townLog -Encoding UTF8 -Pattern '\[ServerBoot\] \uC11C\uBC84 \uC2DC\uC791 - ' -Quiet)
} "The town server did not start. Inspect $townLog (ContentVersion must match GameDB)." $town $StartupTimeoutSeconds
Wait-Until {
    (Invoke-Psql "select count(*) from server_instances where instance_id = '$instanceId' and role = 0") -eq '1'
} "The town server did not register in the server pool. Inspect $townLog and $apiLog" $town $StartupTimeoutSeconds

# Dungeon servers: the watcher starts one process per slot and replaces it whenever it exits
if ($dungeonTotal -gt 0) {
    if (-not (Test-Path -LiteralPath $watcherScript -PathType Leaf)) {
        throw "Dungeon watcher script is missing: $watcherScript"
    }
    for ($i = 0; $i -lt $dungeonTotal; ++$i) {
        if (Test-PortInUse ($dungeonPortNumber + $i)) {
            throw "Dungeon port $($dungeonPortNumber + $i) is already in use. Stop the process using it or pass -DungeonPort."
        }
    }
    $watcherArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$watcherScript`"",
        '-StateDirectory', "`"$stateDirectory`"", '-ServerExecutable', "`"$serverExecutable`"",
        '-DungeonCount', "$dungeonTotal", '-FirstPort', "$dungeonPortNumber", '-ApiBaseUrl', $apiBaseUrl,
        '-ContentVersion', $ContentVersion, '-PublicAddress', $PublicAddress)
    $watcher = Start-Process -FilePath 'powershell.exe' -ArgumentList $watcherArguments -WindowStyle Hidden -PassThru
    Write-Output "Starting $dungeonTotal dungeon server(s) from port $dungeonPortNumber (watcher pid $($watcher.Id))..."
    for ($i = 0; $i -lt $dungeonTotal; ++$i) {
        $dungeonId = $dungeonSlots[$i].Instance
        Wait-Until {
            (Invoke-Psql "select count(*) from server_instances where instance_id = '$dungeonId' and role = 1 and state = 'Idle'") -eq '1'
        } "Dungeon slot $($i + 1) did not become Idle in the server pool. Inspect $stateDirectory\dungeon-watcher.log and the newest dungeon-$($i + 1)-*.log" $watcher $StartupTimeoutSeconds
    }
}

# Clients
for ($i = 0; $i -lt $clientTotal; ++$i) {
    $clientLog = Join-Path $stateDirectory "client-$($i + 1).log"
    $client = Start-Process -FilePath $clientExecutable -WorkingDirectory (Split-Path -Parent $clientExecutable) `
        -ArgumentList @('-api-base-url', $publicApiUrl, '-logFile', "`"$clientLog`"") -PassThru
    Save-ProcessId 'clients' $client
}

Write-Output ''
Write-Output 'Release stack is running.'
Write-Output "  API          $publicApiUrl"
Write-Output "  Town server  ${PublicAddress}:$gamePortNumber (instance $instanceId)"
Write-Output "  Dungeons     $dungeonTotal slot(s), ports from $dungeonPortNumber, restarted automatically after each expedition"
Write-Output "  Clients      $clientTotal started"
Write-Output "  Logs         $stateDirectory"
Write-Output 'In the client Lobby, enter an id, password and email, then press the sign-up button once.'
Write-Output 'Other PCs: run Builds\windows-client\Shinshi.exe -api-base-url <this API URL> (open the API and game ports in the firewall).'
Write-Output 'Stop with: .\tools\Start-ReleaseStack.ps1 -Stop'
