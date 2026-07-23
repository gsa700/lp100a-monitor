# Porting the logging / CAT / plot subsystem from PowerShell w2-monitor → LP-100A (.NET)

**Source of truth:** `gsa700/w2-monitor` (archived) at tag **`v0.9.0-beta`**, file `W2App.ps1`.
Built there in v0.9.0-beta, **removed** in v0.10.0-beta with the note *"redeveloped in a dedicated
LP-100A project, where phase/impedance make them genuinely powerful."* This doc is that
redevelopment spec. The PowerShell is the **specification**, not code to translate line-by-line —
this becomes idiomatic C#/Avalonia MVVM.

## What the three features are

1. **Per-transmission (per-"over") CSV log** — one row per key-down, rolling cap ~2000 rows, with
   an in-app log reader (DataGrid) and an **Open in Excel** button.
2. **CAT live-frequency source** — three drivers (Kenwood TM-D710/V71A `BC`+`FO`; Kenwood
   TS-2000 / Elecraft / SmartSDR `FA;`; Hamlib `rigctld` network `f\n`). Stamps each over's frequency.
3. **SWR-vs-frequency plot** — built from logged overs, per-antenna filtered, reference lines at
   SWR 1.5 / 2.0. On the LP-100A this **also** becomes R-vs-f and X-vs-f (the actual upgrade).

## Key simplification vs the W2

The W2 port is multi-meter (N runspaces), dual-sampler (S1/S2), CAT-per-sampler, with focus
arbitration. **The LP-100A is single-meter** (`MeterService` wraps one connection). So drop:
per-meter loop, `Sensor`/`SensorType` columns, sampler-number → radio lookup, focus logic. One
meter → one over-tracker → one CAT source.

## Over-detection (from `Track-Tx`, W2App.ps1:707)

The core logic ports directly. TX is inferred from forward power (LP-100A CLAUDE.md confirms
`PowerRange` is NOT a TX flag — TX = fwd power > 0):

- `txOn = connected && power > ~0.1 W`.
- On rising edge: start over, reset accumulators (peakFwd, maxSwr, and — new for LP-100A — the
  R/X/phase **sampled at the moment of peak forward power**, plus min-SWR and its frequency).
- Each tick while on: accumulate `peakF = max`, `maxSwr = max`; capture impedance at new peak-power.
- **End the over only on a confirmed key-up** — a disconnect, OR a *valid* low reading persisting
  past a hang time (`txHang`, default 2.0 s). Read dropouts (null power) are ignored so a serial
  glitch can't reset the timer. (This is the hard-won bit — preserve it exactly.)
- Log only if `duration >= 1 s`. `timedOut = duration >= timeoutSec`.

## CSV schema

W2 header (`Write-TxLog`, W2App.ps1:558):
`Timestamp,Meter,Freq_MHz,Duration_s,PeakFwd_W,MaxSWR,MinReturnLoss_dB,Sensor,Range,SensorType,TimedOut`

**LP-100A header (proposed):**
`Timestamp,Freq_MHz,Duration_s,PeakFwd_W,MaxSWR,SWR_at_peak,MinReturnLoss_dB,R_ohm,X_ohm,Phase_deg,Range,TimedOut`

- Drop `Meter,Sensor,SensorType` (single meter, no samplers).
- Add `R_ohm,X_ohm,Phase_deg` (the LP-100A's reason for existing) captured at peak power; add
  `SWR_at_peak`. NOTE: this started life as `MinSWR` (resonance depth) and was **wrong** — the meter
  reports ~1.00 during the key-up ramp / key-down decay (too little power to measure reflection), so a
  running minimum pinned every over to 1.00. Sample SWR at peak power instead, with everything else.
- `MinReturnLoss_dB = -20*log10((swr-1)/(swr+1))` for swr>1 (unchanged from W2App.ps1:564).
- Writer behavior (W2App.ps1:556-573): header-mismatch → rename old file aside; append row; then
  trim to rolling `logMax` keeping the header. UTF-8.

## Layering & port order

**Phase 1 — logging, standalone (no CAT needed; Freq column just empty).**
- `Lp100a.Core`: `TxOverTracker` (pure state machine, the `Track-Tx` logic), `TxOverRecord`
  (the row), `TxLogWriter` (append + rolling-trim + header-migrate). **All pure/IO-thin → unit
  test these.** This is exactly the `tests/` project CLAUDE.md says to add when logic appears.
- `Lp100a.App`: a "Log each TX" toggle + log-reader window (DataGrid, newest-first) + Open in Excel
  (`Process.Start` the csv). Persist toggle + window bounds via existing `AppConfig`.

**Phase 2 — CAT frequency.**
- `Lp100a.Core`: `IFrequencySource` + three drivers (`KenwoodFoBc`, `Ts2000Cat`, `HamlibRigctld`).
  Poll loop ~350-400 ms, watchdog-bounded opens (mirror existing `SerialReader` discipline).
  Regexes/parse from W2App.ps1:173-219 port verbatim.
- `Lp100a.App`: a Radio/CAT setup section; feed `source.FreqMHz` into the tracker each tick.

**Phase 3 — plots.**
- `Lp100a.App/Controls`: reuse the `SmithChartControl` drawing approach for a custom plot control.
  `Get-LogPoints`/`Render-Plot` (W2App.ps1:596-654) is the reference: parse csv by header-index,
  auto-scale axes, grid + reference lines. Extend to R-vs-f and X-vs-f, not just SWR-vs-f.

## Notes / gotchas carried over

- Rolling-trim reads the whole file each write — fine at 2000 rows; keep it simple.
- Header migration on schema change avoids corrupting an old log — keep it.
- rigctld `Port` field is `host:port` (e.g. `127.0.0.1:4532`) — shares one rig across apps; this is
  the driver to prioritize for a remote/CM5 station.
