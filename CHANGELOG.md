# Changelog

All notable changes to **LP-100A Monitor** are documented here.

This project follows [Semantic Versioning](https://semver.org). Versions below
`1.0.0` are pre-release: real and in active use, but not yet broadly field-tested.

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
