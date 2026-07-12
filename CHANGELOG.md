# Changelog

All notable changes to **LP-100A Monitor** are documented here.

This project follows [Semantic Versioning](https://semver.org). Versions below
`1.0.0` are pre-release: real and in active use, but not yet broadly field-tested.

## [0.6.1-beta] - 2026-07-07

### Changed
- Reworked the forward-power bar's auto-range: finer scale steps and ~40% headroom so
  power reads ~70% up the bar instead of pegged at the top, and the full-scale now holds
  while the peak-hold marker is elevated (the marker slides down a fixed scale).
- Compact Updates layout in Setup — "Check for updates" and "Update now" share one row,
  so the window no longer grows a row taller when an update is pending.

## [0.6.0-beta] - 2026-07-07

### Added
- A **(reset)** link next to Peak forward in Setup — clears the PEAK FORWARD readout
  and drops the power-bar scale back down.

### Changed
- The forward-power bar now **auto-ranges with decay**: it rises instantly to fit, then
  eases the full-scale back down when power drops, instead of staying at the session's
  highest range until reset.

## [0.5.0-beta] - 2026-07-06

### Added
- **SWR alarm** — enable it in Setup and set a threshold; a red HIGH SWR banner
  appears on the main window when the live SWR crosses it while transmitting.
- **Peak hold on/off** — toggle the power-bar peak-hold marker from Setup.

### Changed
- Lengthened the Smith-chart fade trail from ~1.2 s to ~3 s.

## [0.4.0-beta] - 2026-07-06

### Added
- **Peak-hold marker** on the forward-power bar — jumps to each new power peak, holds
  briefly, then eases back toward the live reading.
- **Fade trail** on the Smith chart — recent operating points leave a short trail that
  fades out, so you can watch the impedance move while tuning (shown during transmit).
- An always-visible **Open releases page** link in Setup → Updates.

### Changed
- Recolored the main-window title blue and dropped its glow, to calm the green.

## [0.3.0-beta] - 2026-07-04

### Added
- **App icon** — a Smith-chart vector emblem, shown in the window title bar, the
  taskbar, and on the executable.

## [0.2.0-beta] - 2026-07-04

### Added
- **In-app updater** (Setup → Updates): check GitHub for a newer release, download the
  build for your platform, and restart to apply. Optional check-at-startup.
- Project licensed under **GPL-3.0**.

### Note
- To pick up this and future updates automatically, install this build once manually;
  the previous 0.1.0-beta build predates the updater.

## [0.1.0-beta] - 2026-07-04

First public beta. A cross-platform (.NET 8 + Avalonia) desktop monitor for the
TelePost **LP-100A Digital Vector RF Wattmeter** over USB serial.

### Added
- Live meter over serial (115200 8N1, `P` poll, frames delimited by `;`): forward
  power, SWR, reflected power, return loss, dBm, |Z|, phase, and **R + jX**.
- Main window in the W2-Monitor family style: green power/SWR hero readouts, blue
  forward-power bar, gold SWR bar, and toggleable secondary rows.
- **Vector window** with a Smith chart — constant-R/X grid with ohm labels, a live
  operating-point marker, and a constant-SWR circle — for antenna/tuner tuning.
- **Setup window**: port selection and per-row display toggles.
- FTDI/USB **serial-ID pinning**: the meter is followed by its adapter's chip serial
  across COM-port renumbering.
- **Auto-connect** on startup to the last-used adapter.
- Persistence: window positions/sizes and display choices are remembered between
  runs. The Setup and Vector windows are children of the main window and close with it.

### Notes
- Keep the LP-100A on its **Watts screen** for live power/vector data over serial.
- Builds provided for Windows x64, Linux x64, and Linux arm64 (Raspberry Pi).
- The LP-100A serial interface is read-only (poll `P`); the app does not send
  control commands to the meter.
