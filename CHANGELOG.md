# Changelog

All notable changes to **LP-100A Monitor** are documented here.

This project follows [Semantic Versioning](https://semver.org). Versions below
`1.0.0` are pre-release: real and in active use, but not yet broadly field-tested.

## [0.9.18-beta] - 2026-07-28

### Fixed
- **Installing made the folder you installed from undeletable.** The install finished, the app
  started, and the download folder then refused to be deleted for as long as the app was running,
  saying it was in use — with nothing on screen connecting the two. The installed copy was being
  started without a working directory of its own, so it inherited the one it was launched from, and
  Windows will not delete a folder that a running program is sitting in. It now starts in its own
  install folder, and the copy you installed from can be deleted straight away.
- **An install could quietly fail to appear in Installed apps.** The program installed and ran
  normally, but never showed up under Settings → Apps → Installed apps — removing the ordinary way
  to uninstall it — and reported success regardless. The registration is now checked after it is
  written and retried once, and an install that still cannot register says so plainly instead of
  claiming to have worked. Note the underlying cause was not identified; starting the app again has
  always re-registered it, and still does.

## [0.9.17-beta] - 2026-07-28

### Added
- **The app installs itself on Windows.** Run a freshly unzipped copy and it offers to install:
  it copies itself to `%LOCALAPPDATA%\Programs\LP-100A Monitor`, adds a Start Menu shortcut, and
  appears in **Settings → Apps → Installed apps** so it can be removed the normal way. Per-user by
  necessity rather than preference — the in-app updater replaces the running executable, which
  needs no elevation there and would need it on every update under `Program Files`.
- **The same on Linux and Raspberry Pi.** Installs to `~/.local/share/lp100a-monitor`, adds a
  proper application-menu entry with an icon, and symlinks `~/.local/bin/lp100a-monitor` so it runs
  from a terminal by name. No root, no `.deb`, no AppImage — and no more `chmod +x` and launching
  from a terminal. *Published but not yet field-tested on Linux.*
- **A way to decline installing for good.** Put a file named `portable.txt` beside the executable
  and the app runs where it stands, registers nothing, and stops offering to install. Settings and
  the transmission log still live in the user profile, so this skips installing rather than leaving
  no trace.
- **Silent `--install` and `--uninstall`** (with `--quiet`) for unattended use. The installed-apps
  entry uses them.
- Copies installed by hand before there was an installer are adopted where they stand, so they
  appear in Installed apps without being copied to a second location.

### Changed
- Uninstalling asks about settings and the transmission log **separately**, and both default to
  being kept. They share a directory but not their stakes: settings are recreated by reconfiguring,
  while the log is operating history that nothing can bring back. The prompt says how many
  transmissions are at risk rather than naming a file. No command-line switch can delete the log —
  only a person answering that prompt.

## [0.9.16-beta] - 2026-07-28

### Changed
- **Moved to .NET 10.** All four projects retarget `net8.0` → `net10.0`, ahead of .NET 8's LTS
  support window closing in November 2026. `System.IO.Ports` and `System.Management` bumped
  8.0.0 → 10.0.10 to match the runtime. Avalonia deliberately stays at 11.2.1 — its `net8.0`
  assets work unchanged under `net10.0`, and holding the Avalonia 12 bump back keeps the two
  migrations separately diagnosable. Building from source now needs the .NET 10 SDK; the shipped
  builds are self-contained, so nothing changes for anyone running a release.
- Self-contained builds grew roughly 7% (win-x64 90 MB → 96 MB).
- **Upgraded to Avalonia 12.1.0** from 11.2.1. This clears a high-severity advisory
  ([GHSA-xrw6-gwf8-vvr9](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9)) in
  `Tmds.DBus.Protocol`, which reaches the app transitively through `Avalonia.FreeDesktop` and so
  could only be fixed by moving Avalonia. Affected Linux and Raspberry Pi builds; Windows never
  used the DBus layer. The upgrade needed one source change — `TextBox.Watermark` is now
  `PlaceholderText` — and no behavioural changes.
- `Avalonia.Diagnostics` dropped: it has no 12.x release, and nothing in the app ever attached
  DevTools, so the Debug-only reference was unused.

### Fixed
- Release archives no longer carry ~101 MB of native SkiaSharp/HarfBuzzSharp debug symbols.
  Avalonia 12 ships them, and unlike the native libraries themselves they are not bundled into
  the single-file executable, so they landed loose next to it.

## [0.9.15-beta] - 2026-07-26

### Fixed
- **The Alarm tab scrolled again.** The Setup window had a hand-measured height, which goes stale
  the moment anyone adds a line of text to a tab — as the wall-clock ID wording promptly did. The
  window now measures its tabs at open and takes the height of the tallest, so every page fits and
  the window no longer changes size as you click between tabs. Adding options can't quietly
  reintroduce a scrollbar.

## [0.9.14-beta] - 2026-07-26

### Added
- **Transmit timeout** (Setup → Alarm) — warn after a set time of continuous key-down. The TX TIMER
  goes amber 30 s before the limit and red at it, and keeps counting. This is the app's own timer:
  it only warns, and never keys or unkeys the radio. It's also what the log's **Timed out** column
  has always meant — until now that column ran off a fixed 180 s that nothing in the app could see
  or change.
- **Station ID reminder** (Setup → Alarm) — identify *on the tens*. The reminder falls on the clock
  itself (:00, :10, :20 past the hour), not ten minutes after you last identified, so it lands on a
  number you can read off any clock in the shack. It counts down in its own row, keeps running while
  you listen, and doesn't restart when you unkey — only identifying clears it, by clicking the row.
  A mark already past when the QSO began isn't held against you, identifying in the final minute
  satisfies the mark just ahead, and a missed mark stays flagged instead of quietly clearing at the
  next one. Off by default, and never written to the log.

### Changed
- The Setup window is shorter (640 → 520 px): measured against the tallest tab, it had been carrying
  about 150 px of empty space under every tab. No tab scrolls at the new height.

## [0.9.13-beta] - 2026-07-25

### Added
- **Clear log** in the log window — starts a fresh log for a clean run of data. The current log is
  **renamed aside** to `TXlog_<timestamp>.csv`, never deleted, and the status line names the file it
  went to. Two clears in the same second no longer overwrite the first archive.
- **The port list now shows each USB adapter's serial** — `COM7  (A10KMB4VA)`. With a meter, a
  second meter and a transmitter all on similar adapters, the COM number alone doesn't say which is
  which; the serial shown is the one the connection is pinned to when Windows renumbers the port.

### Fixed
- **An adapter with no serial burned in was pinned to the USB socket instead of the cable.** Windows
  synthesises a location-based id (`6&122B2E46&0&1&1`) for such adapters, and it was being accepted
  as if it were a serial — so moving that cable to another socket broke the very follow-the-cable
  behaviour the pinning exists for. Those ids are now rejected and the port simply lists without a
  serial. Adapters that report a real serial are unaffected.

### Changed
- **"Open in Excel" is now "Open CSV"** — it never needed Excel; it hands the file to whatever your
  system opens `.csv` with.
- The log window's action buttons centre their labels.

## [0.9.12-beta] - 2026-07-25

### Fixed
- **App crash when refreshing the transmission log.** With the log window open, clicking
  **Refresh** — or simply logging a new over, which refreshes it automatically — took the whole
  app down. The window's reference to its own grid was never wired up, so the auto-scroll-to-newest
  hit a null reference. Anyone who left the log window open while operating would have hit this on
  their first transmission. **Everyone on 0.9.11-beta should update.**
- The log's column headers were clipped to their own captions ("Freq MH", "SWR@pl", "Ran") and
  timestamps were cut off; columns are now sized to fit their headers, and the window opens wider.
- The log now scrolls to the newest row when it first opens, not only on later refreshes.
- Setup's tabs reported themselves to screen readers as "Avalonia.Controls.ScrollViewer" instead
  of their names; they now announce as Connection / Display / Alarm / Logging / Updates.

## [0.9.11-beta] - 2026-07-25

### Added
- **In-app transmission log viewer** — **Setup → Logging → View log** opens the logged overs in a
  sortable table (time, frequency, duration, peak power, worst SWR, SWR and R+jX at peak, range),
  so you no longer need a spreadsheet to read the log. Click any column to sort; **Open in Excel**
  is still there for deeper work. Rows are chronological with the newest at the bottom, and the
  view scrolls to the newest automatically — including live, as each new over is logged.

### Changed
- **Setup is now tabbed** — Connection, Display, Alarm, Logging, Updates. The single scrolling
  page had grown to seven sections and got taller with every option. Connection holds both data
  sources (the meter's serial port and the rigctld CAT link), so it's one place to look when
  something isn't talking. The window reopens on whichever tab you used last.
- Tab headers are drawn as actual tabs rather than the theme's default underlined text labels.

## [0.9.10-beta] - 2026-07-25

### Fixed
- **App crash when the CAT (rigctld) source was disabled after the daemon went away.** Closing the
  upstream rigctld provider (e.g. MultiCAT) left the frequency poller retrying, as intended — but
  then unticking "Read frequency from rigctld" in Setup could crash the whole app. The poll loop's
  error handler used an exception filter that let a socket error escape as an unhandled
  background-thread exception when it raced shutdown. It now catches unconditionally and exits
  cleanly. Toggling the source off — or losing rigctld and getting it back — is safe either way.

## [0.9.9-beta] - 2026-07-23

### Added
- **Live frequency from a radio (CAT), via Hamlib `rigctld`** — **Setup → Frequency (CAT)** takes a
  `host:port` (default `127.0.0.1:4532`), shows the live dial reading and connection state, and
  stamps the operating frequency onto every logged transmission, filling the log's `Freq_MHz`
  column. The frequency in force at any point during an over is kept, so a brief CAT dropout
  mid-transmission doesn't blank it.

  rigctld is the portable path on purpose: Hamlib supports virtually every rig, and rigctld is a
  *sharing* daemon that owns the CAT port and serves many clients — so the monitor never fights
  your logger or SmartSDR for a serial port. To try it without a radio, run `rigctld -m 1` (dummy).

  Under the hood this lands as an `IFrequencySource` seam in the UI-free core, so native Elecraft /
  Kenwood serial and FlexRadio network sources can slot in later without disturbing the log path.
  PTT/TX state is read too where the rig reports it (and stays *unknown* rather than "receiving"
  when it can't) — that's the groundwork for attributing overs to the right radio on a dual-coupler,
  two-radio station, where the meter itself can't say which sampler is active.

## [0.9.8-beta] - 2026-07-23

### Changed
- **TX log: `MinSWR` replaced by `SWR_at_peak`.** The minimum was meaningless — during the key-up
  ramp and key-down decay there's too little power for the meter to measure reflection, so it
  reports ~1.00, and a running minimum latched onto that on *every* over (125 of 125 logged rows
  read exactly 1.00). SWR is now sampled at peak forward power, alongside R/X/phase, so each row is
  one coherent snapshot of the same instant and reflects the real operating SWR. `MaxSWR` (worst
  seen anywhere in the over) is unchanged and was never affected.

  This is a schema change: an existing `TXlog.csv` is automatically archived aside as
  `TXlog_<timestamp>.csv` and a fresh one started, so earlier data is kept but not mixed.

## [0.9.7-beta] - 2026-07-20

### Fixed
- **TX log CSV corrupted at ≥ 1000 W.** Power (and any other numeric column) was written with a
  thousands separator, so a kilowatt forward-power reading rendered as `1,097.3` and split into two
  columns, shifting every field after it. All numeric columns now use a non-grouping format. Delete
  an existing `TXlog.csv` written by 0.9.6-beta — its rows have the extra column.

## [0.9.6-beta] - 2026-07-20

### Added
- **Per-transmission logging (CSV)** — an opt-in **Setup → Logging** toggle records one row per
  over: timestamp, duration, peak forward power, min/max SWR, min return loss, and the load
  **R + jX** (with phase) sampled at peak power. An **Open log** button opens it in your
  spreadsheet app (or reveals the folder before the first row). The log lives at
  `%AppData%/Lp100aMonitor/TXlog.csv` (Windows) / `~/.config/Lp100aMonitor/TXlog.csv` (Linux),
  capped to a rolling 2000 rows. An over is counted only on a confirmed key-up, so a serial
  dropout mid-over won't split or cut it short. The frequency column stays empty until CAT radio
  support lands. First port of the logging subsystem from the retired PowerShell w2-monitor, where
  the LP-100A's phase/impedance make the data far more useful.

## [0.9.5-beta] - 2026-07-17

### Changed
- **Setup → Updates restyled** to match W2 Monitor: the status line sits above a single button
  that reads "Check for updates" and switches to "Update now" once one is found, with a
  "Release page" button beside it and the "Check for updates at startup" toggle below. Same
  behaviour as before, laid out more cleanly (kept in the app's blue/green palette).

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
