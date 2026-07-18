# Changelog

All notable changes to **LP-100A Monitor** are documented here.

This project follows [Semantic Versioning](https://semver.org). Versions below
`1.0.0` are pre-release: real and in active use, but not yet broadly field-tested.

## [0.9.4-beta] - 2026-07-17

### Added
- **Stale-data watchdog** — a connected meter that stops sending frames *without* a serial
  error (powered off with the USB adapter still plugged in, cable half-out, or knocked off its
  Watts screen) now reads as frozen instead of live: after ~2 s of silence the connection dot
  turns amber, the status line shows "no data (check the meter)", and the readouts dim. They
  return to normal the instant frames resume.

### Fixed
- The framer no longer accumulates unboundedly when pointed at a non-LP-100A stream (a wrong
  COM port that never sends the `;` frame delimiter); the partial-frame buffer is now capped.
- A serial frame that arrived in the instant a disconnect was processed could briefly revive
  the readouts and flash the connection dot green; the reading is now dropped once disconnected.

## [0.9.3-beta] - 2026-07-12

### Changed
- The clickable METER MODE / METER ALARM values now right-justify flush with the other readout
  rows (dropped the padding that was offsetting them; the hit area is preserved via a min width).
- Default peak-hold decay time is now 1.0 s (was 1.5 s). Affects new installs only — an existing
  saved setting is left as-is.

## [0.9.2-beta] - 2026-07-12

### Changed
- Trimmed the padding on the clickable METER MODE / METER ALARM controls so their rows line up
  with the other readout rows again (0.9.1 made them taller). Kept the wider hit area and the
  instant-readback feedback.

## [0.9.1-beta] - 2026-07-12

### Fixed
- The clickable METER MODE / METER ALARM controls (and the Setup alarm setpoint) sometimes
  needed a second click. Enlarged their hit areas, and after a control command the reader now
  settles briefly and polls immediately, so the value updates right away instead of on the next
  scheduled poll.

## [0.9.0-beta] - 2026-07-12

### Added
- **Live SWR bar** — the SWR bar now fills with a green → orange → red gradient, and the colour
  breakpoints **scale to the meter's alarm setpoint**: red anchors where your alarm trips, with
  orange approaching and green safely below. (Falls back to a fixed green→orange→red for the
  Off/User settings, which send no numeric over serial.)
- **Alarm built into the bar** — when the alarm trips, the SWR bar itself flashes red with the
  live "HIGH SWR n.n" text embedded inside it, replacing the old separate banner. It stays
  visible while tripped even if the SWR bar is toggled off.
- **Alarm setpoint control in Setup** — the SWR ALARM section now shows the meter setpoint and
  lets you cycle it, so the alarm is settable from the app even when the main-window METER ALARM
  row is hidden.

### Changed
- The SWR bar is a fixed height matched to the power bar (no longer changes size when the alarm
  trips). SWR bar range is 1.0–3.0 (values above 3:1 peg the bar; the numeric readout is
  unlimited).

## [0.8.0-beta] - 2026-07-12

### Added
- **Meter mode indicator & control** — a METER MODE row shows the meter's Avg/Peak/Tune power
  mode, and clicking it cycles the meter Avg → Peak → Tune (sends the `F` command). No more
  reaching for the front panel.
- **Meter SWR alarm control** — a METER ALARM row shows the meter's SWR alarm setpoint
  (OFF / 1.5 / 2.0 / 2.5 / 3.0 / User) and clicking it cycles the setpoint (sends `A`), driving
  the LP-100A's own hardware alarm and protective PTT relay.

### Changed
- **Serial field map corrected against the official manual (p.20).** Field [5] is the autorange
  scale (High/Mid/Low), not a transmit flag; field [6] is the power mode; field [3] is the
  alarm-setpoint index. Transmit detection now keys purely off forward power.
- **SWR alarm integrated.** The on-screen HIGH SWR banner now echoes the meter's own alarm
  setpoint — one trip point, set on the METER ALARM row — instead of a separate app threshold.
  A new "Show on-screen SWR alarm" toggle enables/disables just the visual banner; the meter's
  hardware alarm is unaffected either way.
- **Setup window compacted** — display toggles are laid out in two columns so the window fits
  comfortably on smaller screens (e.g. a 1080p laptop).

### Notes & limitations
- The meter does not send the numeric value of its **User** alarm setpoint over serial, so the
  on-screen banner cannot show for the **User** or **Off** settings — the meter's own hardware
  alarm/relay still works normally. The presets 1.5–3.0 drive the banner.
- The app sends only `F` (Peak/Avg/Tune) and `A` (alarm) to the meter. It never sends `M`
  (mode/screen change), which would move the meter off its Watts screen and interrupt live data.

## [0.7.0-beta] - 2026-07-12

### Added
- **Peak-hold decay time** — set how long the peak-hold marker sits at the peak before it
  eases down (Setup → Peak hold → decay, 0.25–5 s). Persists between runs.

### Changed
- SWR bar is taller (~80% of the power bar) with square corners, so the two bars read as a
  consistent pair.

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
