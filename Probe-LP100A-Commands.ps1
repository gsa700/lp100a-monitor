<#
.SYNOPSIS
    Discover LP-100A serial commands beyond "P" by trying each printable letter and
    logging any response. Read-only exploration of an UNDOCUMENTED command set.

.DESCRIPTION
    We (and the WT2P app) only ever used "P" (returns the data frame). The meter may
    answer other single-character commands with extra data (band/coupler state,
    firmware, temperature, etc.). This script opens the port at 115200 8N1, sends each
    candidate command, and dumps whatever comes back as HEX + ASCII.

    ⚠ CAUTION: the command set is undocumented. A command *might* change a meter setting
    (mode, coupler, callsign) rather than just report data. By default this tries only
    UPPERCASE letters (how "P" works) and pauses so you can watch the meter's screen.
    Review the meter after running; use -IncludeLower / -IncludeDigits to widen the scan.

.EXAMPLE
    .\Probe-LP100A-Commands.ps1                 # auto-detect port, A-Z
    .\Probe-LP100A-Commands.ps1 -Port COM3
    .\Probe-LP100A-Commands.ps1 -IncludeLower -IncludeDigits
#>
[CmdletBinding()]
param(
    [string]$Port,
    [int]$ReadMs = 250,
    [switch]$IncludeLower,
    [switch]$IncludeDigits
)

function Read-Resp($sp, [int]$ms) {
    $buf = New-Object System.Collections.Generic.List[byte]
    $deadline = [DateTime]::UtcNow.AddMilliseconds($ms)
    while ([DateTime]::UtcNow -lt $deadline -and $sp.BytesToRead -eq 0) { Start-Sleep -Milliseconds 5 }
    while ($sp.BytesToRead -gt 0) {
        $tmp = New-Object byte[] $sp.BytesToRead
        $n = $sp.Read($tmp, 0, $tmp.Length)
        for ($i = 0; $i -lt $n; $i++) { $buf.Add($tmp[$i]) }
        Start-Sleep -Milliseconds 20
    }
    return ,$buf.ToArray()
}
function To-Ascii([byte[]]$b) {
    if (-not $b -or $b.Count -eq 0) { return '' }
    (($b | ForEach-Object { if ($_ -ge 32 -and $_ -le 126) { [char]$_ } elseif ($_ -eq 13) { '<CR>' } elseif ($_ -eq 10) { '<LF>' } else { '.' } }) -join '')
}

# Find the LP-100A if no port given: the one that answers "P" with a ';' frame.
if (-not $Port) {
    foreach ($p in ([System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object -Unique)) {
        try {
            $t = New-Object System.IO.Ports.SerialPort $p,115200,'None',8,'One'
            $t.ReadTimeout = 200; $t.Open(); Start-Sleep -Milliseconds 80
            $t.DiscardInBuffer(); $t.Write('P'); $r = Read-Resp $t 250; $t.Close(); $t.Dispose()
            if ((To-Ascii $r) -match ';') { $Port = $p; break }
        } catch {}
    }
}
if (-not $Port) { Write-Host "No LP-100A found. Plug it in / power on, or pass -Port." -ForegroundColor Yellow; return }

$log = "$PSScriptRoot\command-probe-$(Get-Date -Format yyyyMMdd-HHmmss).log"
Write-Host "Probing $Port (115200 8N1). Watch the meter's screen for unexpected changes." -ForegroundColor Green
Write-Host "Log: $log`n" -ForegroundColor DarkGray

$cmds = [char[]](65..90)                                    # A-Z
if ($IncludeLower)  { $cmds += [char[]](97..122) }          # a-z
if ($IncludeDigits) { $cmds += [char[]](48..57) }           # 0-9

$sp = New-Object System.IO.Ports.SerialPort $Port,115200,'None',8,'One'
$sp.ReadTimeout = 250; $sp.WriteTimeout = 500; $sp.Open(); Start-Sleep -Milliseconds 100
try {
    foreach ($c in $cmds) {
        $sp.DiscardInBuffer()
        $sp.Write([string]$c)
        $r = Read-Resp $sp $ReadMs
        $ascii = To-Ascii $r
        $hex = if ($r.Count) { (($r | ForEach-Object { $_.ToString('X2') }) -join ' ') } else { '' }
        $mark = if ($r.Count -eq 0) { '   (no response)' }
                elseif ($c -eq 'P') { '   <-- known data frame' }
                elseif ($ascii -match ';') { '   *** framed response ***' }
                else { '   *** response ***' }
        $line = "'{0}' (0x{1:X2}) ->{2}  {3}" -f $c, [int]$c, $mark, $ascii
        Write-Host $line -ForegroundColor $(if ($r.Count -and $c -ne 'P') { 'Yellow' } else { 'Gray' })
        Add-Content -Path $log -Value ("{0}`t0x{1:X2}`tlen={2}`t{3}`t{4}" -f $c, [int]$c, $r.Count, $ascii, $hex)
        Start-Sleep -Milliseconds 120
    }
} finally {
    if ($sp.IsOpen) { $sp.Close() }
    $sp.Dispose()
}
Write-Host "`nDone. Any yellow line above is a command the meter answered (besides P)." -ForegroundColor Cyan
