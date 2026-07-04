<#
.SYNOPSIS
    Auto-scan COM ports for a TelePost LP-100A and dump its raw serial response.

.DESCRIPTION
    For each available COM port, opens it at 115200 8N1 (no flow control),
    sends the ASCII poll command "P", and reads whatever comes back. Prints
    the raw response as HEX + ASCII so we can see the leading prefix byte and
    the true field layout, then parses the comma-separated fields against the
    community-reverse-engineered map (WT2P).

    This is an EXPLORATION probe: it does not assume the field map is correct,
    it shows you the raw bytes so we can confirm it on your actual unit.

    Protocol (community-derived, unverified):
      115200 baud, 8 data bits, no parity, 1 stop bit, no flow control.
      Host sends "P"; device replies with one comma-separated line.
      Fields: 0=FwdPwr(W) 1=Z 2=Phase 3=AlarmSet 4=Callsign
              5=? 6=PeakHold 7=dBm 8=SWR   (first char is a prefix byte)

.PARAMETER Port
    Probe only this port (e.g. COM5). Omit to scan all ports.

.PARAMETER Command
    Poll string to send. Default "P". Try "P`r" if plain "P" gets no reply.

.PARAMETER Polls
    Number of poll/read cycles per port on a match. Default 5.

.EXAMPLE
    .\Probe-LP100A.ps1
    .\Probe-LP100A.ps1 -Port COM5 -Polls 20
    .\Probe-LP100A.ps1 -Command "P`r"
#>
[CmdletBinding()]
param(
    [string]$Port,
    [string]$Command = "P",
    [int]$Polls = 5,
    [int]$ReadTimeoutMs = 500
)

$fieldNames = @('FwdPwr(W)','Z','Phase','AlarmSet','Callsign','field5(?)','PeakHold','dBm','SWR')

function Format-Hex([byte[]]$bytes) {
    if (-not $bytes -or $bytes.Count -eq 0) { return '(no bytes)' }
    ($bytes | ForEach-Object { $_.ToString('X2') }) -join ' '
}

function Format-Ascii([byte[]]$bytes) {
    if (-not $bytes -or $bytes.Count -eq 0) { return '' }
    ($bytes | ForEach-Object {
        if ($_ -ge 32 -and $_ -le 126) { [char]$_ }
        elseif ($_ -eq 13) { '<CR>' }
        elseif ($_ -eq 10) { '<LF>' }
        else { "<$($_.ToString('X2'))>" }
    }) -join ''
}

function Read-Available {
    param($sp, [int]$timeoutMs)
    $buf = New-Object System.Collections.Generic.List[byte]
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    # wait for first byte, then drain until a short gap
    while ([DateTime]::UtcNow -lt $deadline -and $sp.BytesToRead -eq 0) {
        Start-Sleep -Milliseconds 5
    }
    while ($sp.BytesToRead -gt 0) {
        $tmp = New-Object byte[] $sp.BytesToRead
        $n = $sp.Read($tmp, 0, $tmp.Length)
        for ($i = 0; $i -lt $n; $i++) { $buf.Add($tmp[$i]) }
        Start-Sleep -Milliseconds 20   # allow trailing bytes of the frame to arrive
    }
    return ,$buf.ToArray()
}

function Probe-Port {
    param([string]$name)

    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan
    $sp = New-Object System.IO.Ports.SerialPort $name, 115200, ([System.IO.Ports.Parity]::None), 8, ([System.IO.Ports.StopBits]::One)
    $sp.Handshake    = [System.IO.Ports.Handshake]::None
    $sp.ReadTimeout  = $ReadTimeoutMs
    $sp.WriteTimeout = 500
    $sp.NewLine      = "`r"

    try {
        $sp.Open()
    } catch {
        Write-Host "  open failed: $($_.Exception.Message)" -ForegroundColor DarkGray
        return $false
    }

    try {
        $sp.DiscardInBuffer(); $sp.DiscardOutBuffer()
        $cmdBytes = [System.Text.Encoding]::ASCII.GetBytes($Command)
        $sp.Write($cmdBytes, 0, $cmdBytes.Length)

        $resp = Read-Available -sp $sp -timeoutMs $ReadTimeoutMs
        if (-not $resp -or $resp.Count -eq 0) {
            Write-Host "  sent '$Command' -> no response" -ForegroundColor DarkGray
            return $false
        }

        Write-Host "  RAW HEX  : $(Format-Hex $resp)" -ForegroundColor Yellow
        Write-Host "  RAW ASCII: $(Format-Ascii $resp)" -ForegroundColor Yellow

        $text = ([System.Text.Encoding]::ASCII.GetString($resp)).Trim()
        if ($text -notmatch ',') {
            Write-Host "  (no commas - probably not an LP-100A)" -ForegroundColor DarkGray
            return $false
        }

        # First char is the presumed prefix byte; strip it before splitting.
        $prefix = $text.Substring(0,1)
        $fields = $text.Substring(1).Split(',')
        Write-Host "  prefix byte: '$prefix'  (0x$([int][char]$prefix | ForEach-Object { $_.ToString('X2') }))"
        Write-Host "  field count: $($fields.Count)" -ForegroundColor Green
        for ($i = 0; $i -lt $fields.Count; $i++) {
            $label = if ($i -lt $fieldNames.Count) { $fieldNames[$i] } else { "field$i(?)" }
            "    [{0,2}] {1,-12} = {2}" -f $i, $label, $fields[$i] | Write-Host
        }

        Write-Host "  *** LP-100A detected on $name ***" -ForegroundColor Green

        if ($Polls -gt 1) {
            Write-Host "  streaming $Polls polls (Fwd / SWR / dBm):" -ForegroundColor Cyan
            for ($p = 1; $p -le $Polls; $p++) {
                $sp.DiscardInBuffer()
                $sp.Write($cmdBytes, 0, $cmdBytes.Length)
                $r = Read-Available -sp $sp -timeoutMs $ReadTimeoutMs
                $t = ([System.Text.Encoding]::ASCII.GetString($r)).Trim()
                if ($t -match ',') {
                    $f = $t.Substring(1).Split(',')
                    $fwd = if ($f.Count -gt 0) { $f[0] } else { '?' }
                    $swr = if ($f.Count -gt 8) { $f[8] } else { '?' }
                    $dbm = if ($f.Count -gt 7) { $f[7] } else { '?' }
                    "    #{0,-2} Fwd={1,-8} SWR={2,-6} dBm={3}" -f $p, $fwd, $swr, $dbm | Write-Host
                } else {
                    "    #{0,-2} (no data)" -f $p | Write-Host
                }
                Start-Sleep -Milliseconds 80
            }
        }
        return $true
    } catch {
        Write-Host "  error: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    } finally {
        if ($sp.IsOpen) { $sp.Close() }
        $sp.Dispose()
    }
}

# --- main ---
Write-Host "LP-100A serial probe  (115200 8N1, poll='$Command')" -ForegroundColor White

$ports = if ($Port) { @($Port) } else { [System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object -Unique }

if (-not $ports -or $ports.Count -eq 0) {
    Write-Host ""
    Write-Host "No COM ports found. Plug in / power on the LP-100A (and its USB-serial" -ForegroundColor Yellow
    Write-Host "adapter), confirm the driver loaded, then re-run this script." -ForegroundColor Yellow
    return
}

Write-Host "Ports to scan: $($ports -join ', ')"
$found = @()
foreach ($p in $ports) {
    if (Probe-Port -name $p) { $found += $p }
}

Write-Host ""
if ($found.Count -gt 0) {
    Write-Host "LP-100A found on: $($found -join ', ')" -ForegroundColor Green
} else {
    Write-Host "No LP-100A detected. If a port opened but gave no reply, try:  -Command `"P``r`"" -ForegroundColor Yellow
}
