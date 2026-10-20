# Shared by the installed Windows server supervisors and their health checks.
function Get-ShinshiDungeonSlots {
    param([Parameter(Mandatory = $true)] $Config)

    $count = 4
    if ($null -ne $Config.PSObject.Properties['DungeonCount']) {
        if (![int]::TryParse([string]$Config.DungeonCount, [ref]$count) -or $count -lt 1 -or $count -gt 8) {
            throw 'DungeonCount must be between 1 and 8.'
        }
    }
    $baseInstance = [guid]$Config.DungeonInstance
    if ($baseInstance -eq [guid]::Empty) { throw 'DungeonInstance must not be empty.' }
    for ($slot = 1; $slot -le $count; ++$slot) {
        $instance = $baseInstance
        if ($slot -gt 1) {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("$($baseInstance.ToString('D'))/dungeon/$slot")) }
            finally { $sha.Dispose() }
            $bytes = New-Object byte[] 16
            [Array]::Copy($hash, $bytes, 16)
            $instance = [guid]::new($bytes)
        }
        [pscustomobject]@{
            Slot = $slot
            InstanceId = $instance.ToString('D')
            Port = 7779 + $slot
            MetricsPort = 9319 + $slot
            PoolSize = $count
        }
    }
}
