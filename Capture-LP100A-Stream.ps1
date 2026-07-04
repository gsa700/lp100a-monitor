<#
.SYNOPSIS  Passively parse the LP-100A free-running frame stream during TX.
.DESCRIPTION
    Sends "P" once (and re-pokes periodically) then continuously reads raw
    bytes, framing on the ';' delimiter (frames have NO CR/LF). This avoids
    any DiscardInBuffer/stale-read timing issues. Logs every frame and reports
    the full range of every numeric field so we can see what moves under TX.
#>
[CmdletBinding()]
param(
    [string]$Port = 'COM3',
    [int]$Seconds = 20
)

$log = "$PSScriptRoot\stream-capture-$(Get-Date -Format yyyyMMdd-HHmmss).log"
$fieldNames = @('Fwd','Z','Phase','Alarm','Call','f5','f6','dBm','SWR')

$sp = New-Object System.IO.Ports.SerialPort $Port, 115200, ([System.IO.Ports.Parity]::None), 8, ([System.IO.Ports.StopBits]::One)
$sp.Handshake = [System.IO.Ports.Handshake]::None
$sp.ReadTimeout = 200; $sp.WriteTimeout = 500
$sp.Open()
$poke = [System.Text.Encoding]::ASCII.GetBytes('P')
$sp.Write($poke, 0, $poke.Length)

Write-Host "Streaming $Seconds s on $Port -- put panel on WATTS screen, then KEY UP a steady carrier..." -ForegroundColor Green

$script:frames = New-Object System.Collections.Generic.List[object]
$script:last = ''
$acc = ''                      # rolling text accumulator
$deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
$nextPoke = [DateTime]::UtcNow.AddMilliseconds(500)
$last = ''

function Emit($frame) {
    if ($frame -notmatch ',') { return }
    $f = $frame.Split(',')
    $script:frames.Add([pscustomobject]@{ raw = $frame; f = $f })
    Add-Content -Path $script:log -Value ("{0:HH:mm:ss.fff}  ;{1}" -f (Get-Date), $frame)
    $line = "Fwd={0,-9} Z={1,-7} Ph={2,-7} dBm={3,-7} SWR={4,-6} f5={5} f6={6}" -f `
        $f[0], $f[1], $f[2], ($(if($f.Count -gt 7){$f[7]}else{'?'})), ($(if($f.Count -gt 8){$f[8]}else{'?'})), $f[5], $f[6]
    if ($line -ne $script:last) { Write-Host "  $line"; $script:last = $line }
}

try {
    while ([DateTime]::UtcNow -lt $deadline) {
        if ([DateTime]::UtcNow -ge $nextPoke) {
            $sp.Write($poke, 0, $poke.Length)
            $nextPoke = [DateTime]::UtcNow.AddMilliseconds(500)
        }
        if ($sp.BytesToRead -gt 0) {
            $tmp = New-Object byte[] $sp.BytesToRead
            $n = $sp.Read($tmp, 0, $tmp.Length)
            $acc += [System.Text.Encoding]::ASCII.GetString($tmp, 0, $n)
            # split on ';' delimiter; last piece may be incomplete -> keep it
            $parts = $acc.Split(';')
            for ($i = 0; $i -lt $parts.Count - 1; $i++) {
                $p = $parts[$i].Trim()
                if ($p) { Emit $p }
            }
            $acc = $parts[$parts.Count - 1]
        } else {
            Start-Sleep -Milliseconds 5
        }
    }
} finally {
    if ($sp.IsOpen) { $sp.Close() }
    $sp.Dispose()
}

Write-Host ""
Write-Host "=== ANALYSIS ($($frames.Count) frames) ===" -ForegroundColor Cyan
if ($frames.Count -eq 0) { Write-Host "No frames."; return }
for ($i = 0; $i -lt 9; $i++) {
    $vals = $frames | Where-Object { $_.f.Count -gt $i } | ForEach-Object { $_.f[$i] } | Sort-Object -Unique
    $name = $fieldNames[$i]
    if ($vals.Count -le 6) {
        "  [{0}] {1,-6} : {2}" -f $i, $name, ($vals -join ', ') | Write-Host
    } else {
        # numeric range summary for high-cardinality fields
        $nums = $frames | Where-Object { $_.f.Count -gt $i } | ForEach-Object { $x=0.0; if([double]::TryParse($_.f[$i].Trim(),[ref]$x)){$x}else{$null} } | Where-Object { $_ -ne $null }
        "  [{0}] {1,-6} : {2} distinct, min={3} max={4}" -f $i, $name, $vals.Count, ($nums|Measure-Object -Minimum).Minimum, ($nums|Measure-Object -Maximum).Maximum | Write-Host
    }
}
Write-Host ""
Write-Host "Log: $log" -ForegroundColor DarkGray
