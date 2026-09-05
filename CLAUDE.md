# LP-100A Monitor (lp100a-monitor)

Cross-platform desktop monitor for the TelePost **LP-100A** Digital Vector RF Wattmeter.
Reads the meter over USB serial and shows forward power, SWR, reflected power, return loss,
dBm, and — the signature feature — the load impedance (**R + jX**) on a live **Smith chart**.
**.NET 10 + Avalonia 12.1.2**, MVVM. Windows / Linux / Raspberry Pi (arm64). GPLv3.
By David Erickson (AB0R). Status: **1.0.0-beta3**.

This app's .NET 10 + Avalonia layout is the reference template for the station tools (the W2
port follows it).

## Build / run

```sh
dotnet restore
dotnet run --project src/Lp100a.App            # needs the .NET 10 SDK + a desktop/DISPLAY
```

Solution: `LP100A.sln`. Output assembly is `Lp100aMonitor`. Run tests with `dotnet test`.
`Lp100a.Core` is deliberately UI-agnostic so serial/parse logic can be tested; nontrivial Core
logic gets covered in `tests/Lp100a.Core.Tests` (xUnit) — keep new logic testable and put it there.

Publish a self-contained build (per platform):

```sh
dotnet publish src/Lp100a.App -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o publish/win-x64
# swap -r for linux-x64 or linux-arm64 (Raspberry Pi)
```

## Layout

```
src/
  Lp100a.Core/   # NO UI: SerialReader, StreamFramer, FrameParser, Lp100Reading.
                 #   Data logging (Phase 1): TxOverTracker, TxOverRecord, TxLogWriter,
                 #     plus the read side: TxLogReader, TxLogEntry.
                 #   CAT frequency (Phase 2): IFrequencySource, RigctldProtocol,
                 #     RigctldFrequencySource.
                 #   Reusable by a future headless logger — keep it UI-free.
  Lp100a.App/    # Avalonia MVVM
                 #   Services/  MeterService (single connection), FrequencyService,
                 #     TxLoggingService, PortIdentity, UpdateService, AppConfig
                 #   ViewModels/ MainWindow, Setup, Vector, Log, ViewModelBase
                 #   Views/     MainWindow, SetupWindow, VectorWindow, LogWindow
                 #   Controls/  SmithChartControl, PeakBar, SwrBar
tests/
  Lp100a.Core.Tests/   # xUnit — Core logic only (no UI). Put new nontrivial logic here.
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

## The serial link supervises itself

`SerialReader` runs a supervisor loop: resolve port → run one session → back off 1 s → repeat, until
`Stop()`. Ported from the W2 monitor (2026-07-28) after the LP-100A was observed losing its meter
across a PC sleep while the W2 sitting beside it recovered every time.

The bug it fixes was structural, and worth remembering as a shape: the poll loop used to sit *inside*
the `try`, so the first exception ended the thread and the app stayed dead until someone reconnected
by hand. A sleep/resume throws exactly that exception whenever the USB device re-enumerates — which
is why it was intermittent, since sometimes the handle survives. **The loop must wrap the `try`, not
the other way round.**

- **Two loss signals, weighted differently.** A hard port error is acted on at once. Silence is not:
  an LP-100A parked on a screen other than Watts is *legitimately* quiet, and reconnecting cannot fix
  that, so `LinkHealth`'s silence threshold is 6 s — comfortably above the 2 s stale indicator — and
  is only a backstop for a handle that survives a resume but never delivers again. Shortening it
  would put a wrongly-parked meter into a permanent reconnect loop.
- **Reconnect re-resolves the port** through the `resolvePort` delegate, which `MeterService` wires to
  `PortIdentity.ResolvePort` and the saved chip serial. A meter that comes back on a different COM
  number is followed; `CurrentPort` is republished on the UI thread when it moves.
- **`IsConnected` tracks the reader thread, not the last message.** An error now usually means
  "reconnecting", so clearing the flag on any error would make `MeterService`'s own
  `ReadingReceived` guard discard the frames a successful reconnect delivers — a link that recovered
  in fact but stayed frozen on screen. During a reconnect the meter reads stale, which is accurate.
- `Open()`/`Close()` run under a watchdog with an atomic ownership handoff, because a surprise-removed
  device can wedge the native call; without the handoff a late-completing open orphans a handle and
  the next attempt hits a self-inflicted "port in use".

## CAT frequency: rigctld only — deliberately

The log's `Freq_MHz` comes from **Hamlib rigctld over TCP, and nothing else**. No native
Elecraft/Kenwood/FlexRadio drivers live here, by decision (2026-07-25): David's **MultiCAT**
(`~/Documents/Programming/MultiCat`) is the station's CAT hub — it owns each radio and supervises a
real `rigctld.exe` per radio, re-exposing them as standard rigctld endpoints many clients can share.
So the monitor stays a dumb rigctld client and every radio-specific concern (K4 network CAT, CI-V,
Flex, arbitration) is solved once, upstream. `IFrequencySource` remains the seam, but adding sources
here is the wrong layer — add them to MultiCAT instead.

Attribution rule: log a frequency only when it's known, never a guess — a wrong frequency is worse
than a blank, because the planned impedance-signature analysis keys its baseline by frequency.
For dual-coupler/SO2R later: one rigctld endpoint per radio + `\get_ptt` to tell which rig is keyed
(the LP-100A cannot report which sampler is active — confirmed by N8LP).

## Config & updater

- App auto-connects the saved port, pinned by its adapter chip serial.
- In-app updater (`UpdateService`) targets GitHub `gsa700/lp100a-monitor`, `Setup → Updates`.
  Confirmed working on Windows and Linux/CM5.
- **Version ordering is `VersionOrder` (Core, tested), not `System.Version`.** Tags and assembly
  versions are compared as semantic versions including the pre-release suffix, so `1.0.0-beta` →
  `1.0.0-beta2` → `1.0.0` is an ascending series. The old dash-truncating compare made those three
  equal, which would have stranded everyone on the first 1.0 beta. Don't "simplify" it back.
- Since v1.0.0-beta there is **no Windows installed-apps entry**; uninstall is Setup → Updates →
  Remove, or `--uninstall`. See *Self-install* for the reason (PCA registry virtualisation).

## Release workflow

`gh` is installed and authed as `gsa700`. Release = git tag + self-contained zips for
win-x64 / linux-x64 / linux-arm64 attached to a GitHub release (asset names must match the
updater's expectations). Update `CHANGELOG.md` each release. Open/parked backlog: data logging
(the UI-free `Core` enables it) and multi-unit support.

## Self-install (Windows and Linux)

Modelled on NTP Time Sync rather than on an installer framework: no Inno/WiX/MSI, no new
toolchain. The app installs *itself*.

**Per-user is a constraint, not a preference.** `UpdateService.ApplyAndRestart` replaces the
running executable in place, which needs no elevation under `%LOCALAPPDATA%\Programs` and would
need it on every update under `Program Files`. A machine-wide installer would quietly break the
updater. Don't "fix" the install location without re-reading that method.

**Location is the mode.** `InstallLayout.Detect` (Core, pure, tested) returns Installed / Portable /
Loose from the executable's directory plus the presence of a `portable.txt` marker. Nothing is
written anywhere that could disagree with where the file actually is. The marker wins over
everything, including the install directory — one unambiguous way to say "don't install this".

**Portable is not data-portable, and finishing that job has an ordering trap.** `Portable` only
suppresses installing and registering; `ConfigStore.DataDir` ignores the mode entirely, so config
and `TXlog.csv` stay in the user profile. Decided 2026-07-28: the LP-100A is a bench instrument with
an inline coupler, so nobody is running this off a stick — the marker is worth keeping as a way to
decline the prompt, not as a portable edition, and the docs say so. If a real request for
leave-no-trace ever arrives and `DataDir` becomes mode-aware, note what that does to any
"don't ask again" button that writes the marker for the user: answering *no* to installing would
then silently move where the app looks for the transmission log, and the log would read empty.
Make `DataDir` portable-aware first, or never write the marker on the user's behalf.

**Pre-installer copies are adopted, not re-installed.** Unzipping in Explorer produced folders like
`Lp100aMonitor-win-x64`; `InstallLayout.LegacyFolders` treats those as installed where they stand
so upgrading doesn't leave an orphaned second copy.

**No registry entry on Windows — do not reintroduce one without a signature and an Explorer-launch
test.** Windows integration is the Start Menu shortcut, full stop. The app used to write an
installed-apps entry to `HKCU\…\Uninstall`, and from 0.9.18 through 0.9.22 that entry kept going
missing or stale despite one-shot `reg import`, read-back verification, re-assertion every launch and
a diagnostics log. The cause (proven in the W2 port, 2026-09-04): Windows' Program Compatibility
Assistant attaches its `DetectorsAppHealth` layer to an unsigned exe launched from Explorer or by the
updater's helper, and that layer virtualises **every** registry write — `reg.exe`, in-process
`RegSetValueEx`, children included — into an overlay the process reads back consistently and loses on
exit. So every "verified" write from a shell launch was real to the process and invisible to Windows.
The manifest `supportedOS` opt-out, in-process writes and a scrubbed relaunch were each tested in W2
and none escaped it; the one untested lever is an Authenticode signature. The registration code is
one commit back in history (v0.9.22-beta) if that ever happens. Removal lives on Setup → Updates
instead, running the same flow as `--uninstall`. The full ruled-out list is in W2's `BACKLOG.md`.

**The transmission log is not app data, and since v1.0.0-beta2 it doesn't live with it.**
`ConfigStore.DataDir` (`%APPDATA%\Lp100aMonitor`, `~/.config/Lp100aMonitor`) holds `config.json`
only. The log and its `TXlog_<stamp>.csv` archives live in `ConfigStore.LogDirectory` —
`Documents\LP-100A Monitor` — because they are operating history: the thing you open in a
spreadsheet, keep for years and expect to be backed up, none of which describes app data. On Linux
that is `XDG_DOCUMENTS_DIR` read from `user-dirs.dirs` (`XdgUserDirs`, Core, tested), falling back to
`~/Documents`; .NET's `MyDocuments` there is just `$HOME`, which would have dropped a CSV into the top
of the home directory. **Uninstall cannot reach the log at all** — it deletes nothing outside the app's
own folders — which is a better guarantee than the keep/delete prompt it replaced. An existing log is
brought across on first start by `TxLogRelocator`: copy, SHA-256 verify, then delete the original,
per file; a name clash leaves both copies and says so; the file-selection rules are
`TxLogRelocationPlan` (Core, tested) so the part that could lose data isn't trusted to eyeballing.
`TxLogWriter`'s own rule is unchanged: archive aside, never delete.

**Linux/Pi uses the same detection**, differing only in paths and in what "register" means:
`~/.local/share/lp100a-monitor/` for the binary (lower-case and hyphenated per XDG — and free of the
space that would otherwise have to survive `.desktop` quoting), a `.desktop` entry in
`~/.local/share/applications`, the 256px icon written to the hicolor theme, and a
`~/.local/bin/lp100a-monitor` symlink for terminal use. No root, no `.deb`, no AppImage. A copied
binary also gets its executable bit set — without it the menu entry silently does nothing.

`DesktopEntry` (Core, pure, 12 tests) builds the entry, including the spec's `Exec` quoting rules.
It is tested rather than eyeballed because the failure mode is a menu item that quietly does
nothing rather than an error anybody sees.

**Uninstall removes shared-directory items file by file.** The install directory is private and can
go recursively, but `~/.local/bin`, the icon theme and `~/.local/share/applications` all belong to
the system — nothing may delete a directory it does not own. Getting this wrong on Linux means
deleting every user binary on the machine, so keep new removals in `Unregister`'s file-at-a-time
form.

**Three rules the uninstall path learned on 2026-09-04, each from a real failure.** (1) *Never close
the app from inside a dialog's click.* Every `ConfirmDialog` answer is a continuation running inside
the mouse-release that pressed the button; closing the main window there — which tears down the
dialog, its owner and the owned Setup window mid-delivery — left a windowless process that never
exited. `CloseMainWhenIdle` posts the close at Background priority instead. Reproduced only with real
pointer input; UIA `Invoke` (no pointer event) never hung, which is the discriminator that found it.
The harness is `repro-remove-click.ps1` in the session scratchpad — UIA-driven, DPI-aware, real
clicks. (2) *The uninstall helper gets its own working directory.* It inherited the app's, which since
the 0.9.20 launch fix is the install folder, and a process cannot remove the directory it stands in —
files gone, empty folder left. `Path.GetTempPath()`. (3) *The helper retries the delete for ten
seconds.* A one-shot delete can land between the PID vanishing and the exe mapping being released.
An uninstalling process also arms `Environment.Exit(0)` three seconds after cleanup as a backstop:
the helper is waiting on that PID and nothing in the process is worth preserving by then. **W2 shares
all three** and should get the same treatment.

**Uninstall leaves the single-file extraction directory behind** *(found via the W2 port on the CM5,
2026-08-02; open here)*. A self-contained single-file build unpacks its native libraries to
`$HOME/.net/<AppName>/<hash>/` on Linux and `%TEMP%\.net\…` on Windows, and `Uninstall` knows nothing
about either. The hash changes with every build, so they accumulate one per distinct binary ever
launched. Measured for this app: **263 MB across 15 directories** on the Windows box and 10 more on
the CM5 — currently the larger offender of the two station tools.

Not a correctness problem, but "uninstall the program" leaving that behind isn't what it says on the
tin. Two things to get right: the path is chosen by the .NET host rather than by us, so removing
`$HOME/.net/<AppName>` wholesale is the honest scope; and a *running* copy holds one of those
directories open, which is why this belongs in the uninstall trampoline beside the install directory
rather than in `Unregister`. Note the platforms differ in kind — on Windows they sit in `%TEMP%`,
which Storage Sense can reclaim, while on Linux `$HOME/.net/` is swept by nothing, so **fix the Linux
side first** if the two are ever separated.

> **Linux is unverified on hardware *here*.** It compiles, publishes for linux-x64/arm64, and its pure
> logic is unit-tested, but no part of this app's filesystem work — icon extraction, `.desktop` write,
> symlink, `chmod`, the `sh` uninstall trampoline — has run on a real Linux box. The CM5 is the place
> to find out. The Windows path is verified end to end.
>
> *What the W2 port has since established, 2026-08-02:* it took this pattern and ran the whole thing on
> the CM5 — install, `.desktop` write, icon, symlink, `chmod` (both artifacts land `rwxr-xr-x`), and an
> uninstall driven from an installed **published** build, with the generated script captured before it
> deleted itself. The trampoline's critical property held: exactly one `rm -rf`, aimed at the install
> directory alone, with `~/.local/bin`, the icon theme and `~/.local/share/applications` untouched. So
> the *shape* is proven on hardware. What remains unproven for this app is its own paths and names, and
> the concerns the W2 port does not share — chiefly that uninstall here must protect `TXlog.csv`.

## Notes travel through this repo, not through memory

**Claude's saved memory does not cross machines — this repo is the only channel between sessions.**
Memory files live under the per-user Claude directory on whichever box a session ran on, so a session
on the CM5 cannot read anything a session on the Windows box saved, and vice versa. The same applies
between this project and `w2-monitor-x`: they are separate repos, and a finding in one reaches the
other only if somebody writes it down in both. That is exactly how the extraction-directory leak above
arrived here — found by the W2 port on the CM5, carried across by hand.

So nothing about the project may live only in memory. It goes in `CLAUDE.md`, `CHANGELOG.md` or a
commit message. Memory is for how to work with David; the repo is for what is true about the project.

Two consequences, both of which have already bitten on the W2 side:

- **Write the reasoning, not just the conclusion.** The next session is a stranger with no context — it
  did not run the experiment, so "ruled out" saves it nothing unless the file says how it was ruled
  out. By the same token a wrong claim here misinforms someone with no way to check it, which is why a
  stale line in these files costs more than a bug in the code: the code has tests behind it.
- **Pull before editing the shared docs.** Sessions are asynchronous with no liveness — neither knows
  what the other is doing right now. On 2026-08-02 two sessions edited the W2 backlog within minutes of
  each other and it merged cleanly only because they happened to touch different sections.

## Queued from the W2 port: one fix left (noted 2026-07-30, updated 2026-09-04)

The W2 port took this app's installer and tabbed Setup and hit several things worth bringing back.
**These have shipped here** — don't redo them:

- *Uninstall deleting a directory it didn't create* — fixed in `340cf85`, released in v0.9.19-beta.
- *Registration written per-value and never re-asserted* — fixed in v0.9.19-beta, then made moot:
  the registry entry was removed altogether in v1.0.0-beta (see *Self-install* above for why).
- *Updater blind to pre-release suffixes* — `VersionOrder` (Core, tested) ported in v1.0.0-beta. The
  old comparison truncated at the dash, so 1.0.0-beta, 1.0.0-beta2 and 1.0.0 were indistinguishable
  and nobody on the first beta would ever have been offered the next.
- *Uninstall from Setup* — the **Remove…** button on Setup → Updates, v1.0.0-beta.

One is still open. Reference implementation is in `~/Documents/Programming/w2-monitor-x`
(`src/W2.App/App.axaml.cs`, `src/W2.App/ViewModels/SetupViewModel.cs`).

**Opening Setup because of an update should select the Updates tab.** `SelectedTabIndex` is restored
from `_config.SetupTab`, so when startup finds an update and calls `ShowSetup()` (`App.axaml.cs:98`),
the window appears on whatever tab was last used — with nothing on screen saying why it opened. W2
gives `ShowSetup` an optional tab argument and passes the Updates index on that path, while still
restoring the remembered tab everywhere else. Cosmetic, but it's the difference between a window that
explains itself and one that doesn't.

Also worth knowing, though it needs no change here: **`APPDATA` does not isolate config on Windows.**
.NET resolves `SpecialFolder.ApplicationData` through the known-folder API and ignores the environment
variable, so redirecting it before a smoke test protects nothing. Force-kill the test instance instead,
so it never reaches save-on-exit. `HOME`/`XDG_CONFIG_HOME` on Linux do work.

## The .NET 10 + Avalonia 12 migration (2026-07-28)

Both done, deliberately as two commits so a regression points at one culprit rather than two.

**.NET 10.** All four projects target `net10.0` — Core, App, Tests, and `tools/IconGen`. Driven by
.NET 8's LTS support window closing in November 2026. `System.IO.Ports` and `System.Management`
moved 8.0.0 → 10.0.10 to match the runtime. Avalonia stayed at 11.2.1 for that commit: its
`lib/net8.0` assets run unchanged under `net10.0`, so the framework move never required the bump.

**Avalonia 12.1.0.** Went far more smoothly than the "major version" label suggests — one
deprecation total (`TextBox.Watermark` → `PlaceholderText`, in `SetupWindow.axaml`) and no code
changes. Two things to know:
- **`Avalonia.Diagnostics` was dropped, not bumped.** It has no 12.x release. Nothing ever called
  `AttachDevTools()`, so the Debug-only reference was dead weight. If you ever want DevTools back,
  check where 12.x moved them rather than re-adding the 11.x package.
- **`Tmds.DBus.Protocol` 0.20.0 → 0.94.1 clears GHSA-xrw6-gwf8-vvr9** (high severity, Linux/Pi
  only). It is transitive via `Avalonia.FreeDesktop`, so the Avalonia version was the only lever —
  this is why the bump happened when it did. `dotnet list package --vulnerable --include-transitive`
  is now clean across all three projects; that's the command to re-check with.

**Watch the publish output.** Avalonia 12's SkiaSharp/HarfBuzzSharp build ships native `.pdb`
symbols (~101 MB combined) that do *not* get bundled into the single file — they land loose beside
the exe. The `TrimNativeSymbols` target in `Lp100a.App.csproj` drops them at publish. If release
zips ever balloon again, look there first.

Verified on Windows against a real LP-100A: 90/90 tests, all three RIDs publish as a true single
file, and the app connects, renders the Smith chart and the DataGrid log, and scales correctly at
150% desktop scaling. Sizes: win-x64 100 MB, linux-x64 96 MB, linux-arm64 102 MB (net8/Avalonia 11
was 90/85/91).

Still open:
- **linux-x64 / linux-arm64 have only been cross-published, never run.** The CM5 needs a real launch
  before any release ships on this — it's the one platform where the DBus layer is actually used.
- **The in-app `UpdateService` round trip is unverified on both changes.** It replaces only the exe,
  so confirm the self-extracting single file still carries its native libs after an in-place update.

Publish size grew about 7% (win-x64 90 MB → 96 MB, linux-x64 85 → 92, linux-arm64 91 → 97).
