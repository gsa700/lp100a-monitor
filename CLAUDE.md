# LP-100A Monitor (lp100a-monitor)

Cross-platform desktop monitor for the TelePost **LP-100A** Digital Vector RF Wattmeter.
Reads the meter over USB serial and shows forward power, SWR, reflected power, return loss,
dBm, and — the signature feature — the load impedance (**R + jX**) on a live **Smith chart**.
**.NET 8 + Avalonia 11.2.1**, MVVM. Windows / Linux / Raspberry Pi (arm64). GPLv3.
By David Erickson (AB0R). Status: **0.9.3-beta**.

This app's .NET 8 + Avalonia layout is the reference template for the station tools (the W2
port follows it).

## Build / run

```sh
dotnet restore
dotnet run --project src/Lp100a.App            # needs the .NET 8 SDK + a desktop/DISPLAY
```

Solution: `LP100A.sln`. Output assembly is `Lp100aMonitor`. There is currently **no test
project** — `Lp100a.Core` is deliberately UI-agnostic so serial/parse logic *can* be tested;
add a `tests/` project if you introduce nontrivial logic.

Publish a self-contained build (per platform):

```sh
dotnet publish src/Lp100a.App -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o publish/win-x64
# swap -r for linux-x64 or linux-arm64 (Raspberry Pi)
```

## Layout

```
src/
  Lp100a.Core/   # NO UI: SerialReader, StreamFramer, FrameParser, Lp100Reading.
                 #   Reusable by a future headless logger — keep it UI-free.
  Lp100a.App/    # Avalonia MVVM
                 #   Services/  MeterService (single connection), PortIdentity, UpdateService, AppConfig
                 #   ViewModels/ MainWindow, Setup, Vector, ViewModelBase
                 #   Views/     MainWindow, SetupWindow, VectorWindow
                 #   Controls/  SmithChartControl, PeakBar, SwrBar
tools/IconGen/   # small icon generator
```

Root-level PowerShell probe/capture scripts (`Capture-LP100A-*.ps1`, `Probe-LP100A*.ps1`) are
for exploring the serial stream against real hardware.

**Single-meter by design** — `MeterService` wraps one connection (contrast the W2 port, which
manages N meters via a MeterManager).

## LP-100A serial protocol (per official manual, p.20 — confirmed)

- **115200 8N1, no flow control.** Send ASCII `P`; the meter replies with ONE frame delimited
  by a leading `;` (no CR/LF). This is a single-`P`-poll stream (the W2, by contrast, is
  multi-command query/response).
- Fields: `Power(W), Z(Ω), Phase(°), AlarmIdx, Callsign, PowerRange, MeterMode, dBm, SWR`
  - `AlarmIdx`: 0=off, 1=1.5, 2=2.0, 3=2.5, 4=3.0, 5=User
  - `PowerRange`: autorange scale — 0=High, 1=Mid, 2=Low (**NOT** a transmit flag; TX is
    inferred from forward power > 0)
  - `MeterMode`: 0=Average, 1=Peak, 2=Tune
- Control commands used: `A` cycles the alarm setpoint, `F` cycles Avg/Peak/Tune. **Never send
  `M`** — it changes the meter's display screen and would move it off the Watts screen, killing
  live data. Keep the meter on its **Watts/Power screen**.
- The **User** alarm setpoint value isn't reported over serial, so on-screen alert scaling falls
  back to defaults for User/Off; the meter's own hardware alarm/relay still works.

## Config & updater

- App auto-connects the saved port, pinned by its adapter chip serial.
- In-app updater (`UpdateService`) targets GitHub `gsa700/lp100a-monitor`, `Setup → Updates`.
  Confirmed working on Windows and Linux/CM5.

## Release workflow

`gh` is installed and authed as `gsa700`. Release = git tag + self-contained zips for
win-x64 / linux-x64 / linux-arm64 attached to a GitHub release (asset names must match the
updater's expectations). Update `CHANGELOG.md` each release. Open/parked backlog: data logging
(the UI-free `Core` enables it) and multi-unit support.
