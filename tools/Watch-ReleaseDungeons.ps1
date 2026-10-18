<#
.SYNOPSIS
Keeps the dungeon servers of the local release stack running: one process per slot, replaced when it exits.
.DESCRIPTION
A dungeon server ends its process after one expedition is settled (exit code 0, the "recycle" contract), and the
backend then marks that server pool row Draining. The stack therefore needs a supervisor that starts a new
process for the same instance id; the backend lets a new boot take over a Draining dungeon row once no
expedition is open on it. Start-ReleaseStack.ps1 starts this script as a hidden background process.

The number of slots is the number of parties that can be on an expedition at the same time; the other parties wait
in the town sortie queue until a slot is free again.

Per slot N (1-based):
  - instance id   %LOCALAPPDATA%\Shinshi\release-stack\dungeon_instance_id (slot 1) or dungeon_instance_id_N
  - game port     FirstPort + N - 1
  - pid file      dungeon-N.pid (process id and start time, same format as the other pid files)
  - log           dungeon-N-<yyyyMMdd-HHmmss>.log, one file per process; the newest KeepLogs files stay
The watcher writes its own events to dungeon-watcher.log and its pid to dungeon-watcher.pid.
A slot whose pid file points to a live Shinshi process is adopted instead of started again.
A process that exits within 20 seconds of its start counts as a failed start; the restart delay then grows
(5, 10, 20, 40, 60 seconds) so a broken build does not spin. Secrets are never printed; the service token is
passed to the server as a file path only.

Stop with Start-ReleaseStack.ps1 -Stop (it stops this watcher first, then every dungeon process).
.EXAMPLE
.\tools\Watch-ReleaseDungeons.ps1 -StateDirectory "$env:LOCALAPPDATA\Shinshi\release-stack" -ServerExecutable .\Builds\windows-server\Shinshi.exe -ContentVersion 0.1.4
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $StateDirectory,
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $ServerExecutable,
    [string] $DungeonCount = '4',
    [string] $FirstPort = '7780',
    [string] $ApiBaseUrl = 'http://127.0.0.1:5080',
    [string] $ContentVersion = '0.1.4',
    [string] $PublicAddress = '127.0.0.1',
    [string] $PollSeconds = '2',
    [string] $KeepLogs = '5'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$QuickExitSeconds = 20
$RestartDelaySeconds = 2
$MaxBackoffSeconds = 60
$WatcherLogLimitBytes = 1048576

function Read-IntegerSetting {
    param([string] $Value, [string] $Name, [int] $Minimum, [int] $Maximum)

    $parsed = 0
    if (($Value -notmatch '\A[0-9]+\z') -or (-not [int]::TryParse($Value, [ref] $parsed)) -or
        ($parsed -lt $Minimum) -or ($parsed -gt $Maximum)) {
        throw "$Name must be an integer between $Minimum and $Maximum."
    }
    return $parsed
}

$slotTotal = Read-IntegerSetting $DungeonCount 'DungeonCount' 1 8
$firstPortNumber = Read-IntegerSetting $FirstPort 'FirstPort' 1 65535
$pollSeconds = Read-IntegerSetting $PollSeconds 'PollSeconds' 1 60
$keepLogCount = Read-IntegerSetting $KeepLogs 'KeepLogs' 1 100
if (($firstPortNumber + $slotTotal - 1) -gt 65535) {
    throw 'FirstPort + DungeonCount exceeds 65535.'
}
if (-not (Test-Path -LiteralPath $ServerExecutable -PathType Leaf)) {
    throw "Server executable not found: $ServerExecutable"
}

$serviceTokenPath = Join-Path $StateDirectory 'service_token'
$watcherLog = Join-Path $StateDirectory 'dungeon-watcher.log'
$watcherPidPath = Join-Path $StateDirectory 'dungeon-watcher.pid'

function Write-WatcherLog {
    param([string] $Message)

    try {
        if ((Test-Path -LiteralPath $watcherLog -PathType Leaf) -and ((Get-Item -LiteralPath $watcherLog).Length -gt $WatcherLogLimitBytes)) {
            Move-Item -LiteralPath $watcherLog -Destination ($watcherLog + '.old') -Force
        }
        Add-Content -LiteralPath $watcherLog -Value ('{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $Message) -Encoding UTF8
    }
    catch {
        # Logging must never stop the supervisor
    }
}

function Get-InstanceId {
    param([int] $Slot)

    $suffix = if ($Slot -eq 1) { '' } else { "_$Slot" }
    $path = Join-Path $StateDirectory "dungeon_instance_id$suffix"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Instance id file is missing: $path"
    }
    return [IO.File]::ReadAllText($path).Trim()
}

function Get-SavedProcess {
    param([string] $PidPath)

    if (-not (Test-Path -LiteralPath $PidPath -PathType Leaf)) {
        return $null
    }
    foreach ($line in Get-Content -LiteralPath $PidPath) {
        $parts = $line.Split(' ')
        if ($parts.Count -lt 2) { continue }
        $process = Get-Process -Id ([int]$parts[0]) -ErrorAction SilentlyContinue
        # The saved start time avoids adopting an unrelated process after PID reuse
        if (($null -ne $process) -and ($process.StartTime.ToFileTimeUtc().ToString() -eq $parts[1])) {
            return $process
        }
    }
    return $null
}

function Save-ProcessId {
    param([string] $PidPath, [System.Diagnostics.Process] $Process)

    Set-Content -LiteralPath $PidPath -Value ('{0} {1}' -f $Process.Id, $Process.StartTime.ToFileTimeUtc()) -Encoding Ascii
}

function Remove-OldLogs {
    param([int] $Slot)

    $old = Get-ChildItem -LiteralPath $StateDirectory -Filter "dungeon-$Slot-*.log" -File -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -Skip $keepLogCount
    foreach ($file in $old) {
        Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
    }
}

function Start-DungeonProcess {
    param([int] $Slot)

    $instanceId = Get-InstanceId $Slot
    $port = $firstPortNumber + $Slot - 1
    $log = Join-Path $StateDirectory ('dungeon-{0}-{1:yyyyMMdd-HHmmss}.log' -f $Slot, (Get-Date))
    $arguments = @('-batchmode', '-nographics', '-logFile', "`"$log`"", '-role', 'dungeon', '-port', "$port",
        '-instance', $instanceId, '-backend-base-url', $ApiBaseUrl, '-content-version', $ContentVersion,
        '-dungeon-pool-size', "$slotTotal", '-backend-service-token-file', "`"$serviceTokenPath`"",
        '-join-verifier', 'api', '-public-address', $PublicAddress)
    $process = Start-Process -FilePath $ServerExecutable -WorkingDirectory (Split-Path -Parent $ServerExecutable) `
        -ArgumentList $arguments -WindowStyle Hidden -PassThru
    # Reading Handle keeps the process handle open so ExitCode stays readable after the process ends
    $null = $process.Handle
    Save-ProcessId (Join-Path $StateDirectory "dungeon-$Slot.pid") $process
    Remove-OldLogs $Slot
    Write-WatcherLog ("slot {0}: started pid {1}, port {2}, instance {3}, log {4}" -f $Slot, $process.Id, $port, $instanceId, (Split-Path -Leaf $log))
    return $process
}

# Only one watcher per state directory; the pid file is written by the watcher itself
$existingWatcher = Get-SavedProcess $watcherPidPath
if (($null -ne $existingWatcher) -and ($existingWatcher.Id -ne $PID)) {
    Write-WatcherLog "another watcher (pid $($existingWatcher.Id)) is already running, exiting"
    return
}
Save-ProcessId $watcherPidPath (Get-Process -Id $PID)
Write-WatcherLog ("watcher started: pid {0}, slots {1}, first port {2}" -f $PID, $slotTotal, $firstPortNumber)

$slots = @()
for ($i = 1; $i -le $slotTotal; ++$i) {
    $adopted = Get-SavedProcess (Join-Path $StateDirectory "dungeon-$i.pid")
    $startedAt = [DateTime]::MinValue
    if ($null -ne $adopted) {
        $null = $adopted.Handle
        $startedAt = $adopted.StartTime
        Write-WatcherLog "slot ${i}: adopted running pid $($adopted.Id)"
    }
    $slots += @{ Slot = $i; Process = $adopted; StartedAt = $startedAt; NextStart = [DateTime]::MinValue; Failures = 0 }
}

while ($true) {
    foreach ($slot in $slots) {
        $now = Get-Date
        if ($null -ne $slot.Process) {
            if (-not $slot.Process.HasExited) { continue }

            $exitCode = 'unknown'
            try { $exitCode = [string]$slot.Process.ExitCode } catch { }
            $lived = ($now - $slot.StartedAt).TotalSeconds
            if ($lived -lt $QuickExitSeconds) { $slot.Failures = $slot.Failures + 1 } else { $slot.Failures = 0 }
            $delay = $RestartDelaySeconds
            if ($slot.Failures -gt 0) {
                $delay = [Math]::Min($MaxBackoffSeconds, 5 * [Math]::Pow(2, [Math]::Min($slot.Failures - 1, 6)))
            }
            $slot.NextStart = $now.AddSeconds($delay)
            Write-WatcherLog ("slot {0}: pid {1} exited (code {2}, lived {3:N0}s, consecutive quick exits {4}), restart in {5:N0}s" -f
                $slot.Slot, $slot.Process.Id, $exitCode, $lived, $slot.Failures, $delay)
            $slot.Process.Dispose()
            $slot.Process = $null
            Remove-Item -LiteralPath (Join-Path $StateDirectory "dungeon-$($slot.Slot).pid") -ErrorAction SilentlyContinue
        }

        if ($now -ge $slot.NextStart) {
            try {
                $slot.Process = Start-DungeonProcess $slot.Slot
                $slot.StartedAt = Get-Date
            }
            catch {
                $slot.Failures = $slot.Failures + 1
                $slot.NextStart = $now.AddSeconds(10)
                Write-WatcherLog "slot $($slot.Slot): start failed: $($_.Exception.Message)"
            }
        }
    }
    Start-Sleep -Seconds $pollSeconds
}
