<#
.SYNOPSIS  Capture full LP-100A frames during a TX event to resolve fields [5]/[6].
#>
[CmdletBinding()]
param(
    [string]$Port = 'COM3',
    [int]$Seconds = 15,
    [int]$IntervalMs = 80
)

$log = "$PSScriptRoot\tx-capture-$(Get-Date -Format yyyyMMdd-HHmmss).log"
$fieldNames = @('Fwd','Z','Phase','Alarm','Call','f5','f6','dBm','SWR')

function Read-Available {
    param($sp, [int]$timeoutMs = 300)
    $buf = New-Object System.Collections.Generic.List[byte]
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    while ([DateTime]::UtcNow -lt $deadline -and $sp.BytesToRead -eq 0) { Start-Sleep -Milliseconds 3 }
    while ($sp.BytesToRead -gt 0) {
        $tmp = New-Object byte[] $sp.BytesToRead
        $n = $sp.Read($tmp, 0, $tmp.Length)
        for ($i = 0; $i -lt $n; $i++) { $buf.Add($tmp[$i]) }
        Start-Sleep -Milliseconds 15
    }
    return ,$buf.ToArray()
}

$sp = New-Object System.IO.Ports.SerialPort $Port, 115200, ([System.IO.Ports.Parity]::None), 8, ([System.IO.Ports.StopBits]::One)
$sp.Handshake = [System.IO.Ports.Handshake]::None
$sp.ReadTimeout = 300; $sp.WriteTimeout = 500
$sp.Open()
$cmd = [System.Text.Encoding]::ASCII.GetBytes('P')

Write-Host "Capturing $Seconds s on $Port -- KEY UP NOW into the dummy load..." -ForegroundColor Green
$frames = New-Object System.Collections.Generic.List[object]
$deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
$lastLine = ''
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        $sp.DiscardInBuffer()
        $sp.Write($cmd, 0, $cmd.Length)
        $r = Read-Available -sp $sp
        $t = ([System.Text.Encoding]::ASCII.GetString($r)).Trim()
        if ($t -match '^;' -and $t -match ',') {
            $f = $t.Substring(1).Split(',')
            $frames.Add([pscustomobject]@{ ts = [DateTime]::UtcNow; raw = $t; f = $f })
            Add-Content -Path $log -Value ("{0:HH:mm:ss.fff}  {1}" -f (Get-Date), $t)
            if ($t -ne $lastLine) {
                $fwd = $f[0]; $f5 = if($f.Count -gt 5){$f[5]}else{'?'}; $f6 = if($f.Count -gt 6){$f[6]}else{'?'}
                $dbm = if($f.Count -gt 7){$f[7]}else{'?'}; $swr = if($f.Count -gt 8){$f[8]}else{'?'}
                "  Fwd={0,-9} SWR={1,-6} dBm={2,-7} f5={3,-4} f6={4}" -f $fwd,$swr,$dbm,$f5,$f6 | Write-Host
                $lastLine = $t
            }
        }
        Start-Sleep -Milliseconds $IntervalMs
    }
} finally {
    if ($sp.IsOpen) { $sp.Close() }
    $sp.Dispose()
}

Write-Host ""
Write-Host "=== ANALYSIS ($($frames.Count) frames) ===" -ForegroundColor Cyan
if ($frames.Count -eq 0) { Write-Host "No frames captured."; return }

$fwdVals = $frames | ForEach-Object { [double]($_.f[0]) }
$tx = $frames | Where-Object { [double]($_.f[0]) -gt 0 }
Write-Host ("Fwd power W: min={0} max={1}   frames with power: {2}" -f ($fwdVals | Measure-Object -Minimum).Minimum, ($fwdVals | Measure-Object -Maximum).Maximum, $tx.Count)
Write-Host ("field[5] distinct values: {0}" -f (($frames | ForEach-Object { $_.f[5] } | Sort-Object -Unique) -join ', '))
Write-Host ("field[6] distinct values: {0}" -f (($frames | ForEach-Object { $_.f[6] } | Sort-Object -Unique) -join ', '))
if ($tx.Count -gt 0) {
    Write-Host ""
    Write-Host "Sample frames WITH power:" -ForegroundColor Yellow
    $tx | Select-Object -First 6 | ForEach-Object { "  $($_.raw)" | Write-Host }
    Write-Host ""
    Write-Host "field[5]/[6] ONLY within TX frames -> f5: $((($tx | ForEach-Object { $_.f[5] } | Sort-Object -Unique) -join ', '))   f6: $((($tx | ForEach-Object { $_.f[6] } | Sort-Object -Unique) -join ', '))"
}
Write-Host ""
Write-Host "Full log: $log" -ForegroundColor DarkGray
