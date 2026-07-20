# Antenna diagnostics — design notes (exploratory)

Status: **brainstorm, not committed.** Captures two related ideas for turning logged R+jX +
CAT frequency into antenna insight, and — importantly — *why the cheap one is the one to build.*
Depends on the logging work (`docs/PORTING-LOGGING.md`): Phase 1 gives per-over R+jX, Phase 2
gives frequency.

## The two ideas

### A. MoM radiation-pattern display (the ambitious one)
Run a Method-of-Moments model (NEC-2) of the antenna and draw its radiation pattern, updated to
the live CAT frequency, with the LP-100A's measured feedpoint impedance overlaid on the model's
predicted impedance as a "does reality match the model?" check.

**Feasibility of the compute: high.** NEC-2 is public-domain with reusable engines (necpp/PyNEC,
nec2c); LGPL/GPL is compatible with this GPLv3 app. Because antenna geometry is fixed and only
frequency changes, you don't need a live solver — solve once at model-import time into a pattern
cube (gain over az×el, sampled every ~25–50 kHz per band, plus modeled Z vs frequency), then the
runtime just interpolates/renders. That sidesteps real-time numerics and native NEC on the Pi.

**Why it's demoted (see "The tuner problem" below):**
- The pattern is always a *model*, never a measurement. The LP-100A sees one complex number; you
  cannot invert one impedance into a 3-D pattern. Measured Z can only *validate/tune* a model.
- Requires a real model per antenna: wire geometry, radius, segmentation, feedpoint, **height +
  ground** (which dominates HF elevation). Garbage-in/garbage-out.
- Native NEC libs would reopen the per-RID native-bundling problem that bit the single-file /
  updater flow (the Skia/HarfBuzz saga). Real cost.
- Avoid NEC-4 (buried radials) — licensed/export-restricted. NEC-2 only.

Verdict: a *someday luxury* for resonant-antenna / antenna-side-coupler users. Not the headline.

### B. Empirical impedance-signature overlay (the one to build)
Forget the model. Treat **tuner + feedline + antenna as one black-box one-port** as the meter sees
it, and build the reference from the operator's **own logged measurements** instead of a MoM model.

- As frequency sweeps a band, measured R+jX traces an **impedance-vs-frequency locus** on the Smith
  chart. Behind a well-set tuner it's a tight cluster near center — that residual wander *is* the
  fingerprint (a matched system is never *exactly* 1:1).
- Log it once as a **baseline**, draw it as an outline on the existing `SmithChartControl`, then in
  real time plot the live point against it. **CAT frequency is what makes it sharp** — it says where
  on the curve you *should* be right now, so the readout is an expected-vs-actual deviation vector.
- Store the baseline as a **cloud/envelope per frequency bin**, not a hairline, so normal day-to-day
  variation doesn't cry wolf; alarm only when the live point leaves the learned envelope.

**What it buys you:**
- Drift detection **even behind a tuner**: slow migration off the outline over hours/days = ice,
  rain, water in the coax, a corroding connector — the feedline-degradation early-warning, live.
- A re-tune / band change shows as a **discontinuous jump**; weather/physical drift shows as **slow
  migration** — so the two are distinguishable.

**What it is NOT:** a radiation pattern, or the antenna's *true* impedance. It's a
"same-as-before / different-than-before" comparator. That comparator is exactly the diagnostic
value, and no standalone box does it live against your own history. ~80% of the value for ~15% of
the effort.

## The tuner problem (why B beats A)

Most LP-100A installs — including this station — read on the **radio side of a tuner**. The ATU
synthesizes 50 + j0 into itself, so the meter sees the *tuner input* (~50 Ω, ~1:1), not the
antenna. The antenna's R+jX is transformed away. Consequences:
- Logged SWR/return-loss becomes "how well I tuned," not antenna health.
- A feedline can be de-embedded (linear, known length/VF); a **tuner cannot** (unknown per-band
  settings, lossy, varying topology). So the antenna doesn't "come back" mathematically.
- Idea A's model-vs-measured overlay has nothing real to compare against here.

Idea B survives this because it never claims to see the antenna feedpoint — it tracks whether the
*composite as-tuned* behaves like it did before.

**This station's tuner:** an **autotuner with memories it auto-recalls on sensing frequency.** This
is the best case for B — each memory yields a *reproducible* one-port, so the fingerprint is stable
per frequency region once the system settles. Design implication: **key baselines by frequency
region (matching how the tuner recalls memories)**, and **ignore the tune-in transient** — learn and
compare only on settled samples (skip the first moment of an over / any recall settling), since the
"it's pretty close once it settles" behavior means the pre-settle wander is noise, not signal.

## Where B lives (fits the existing split)
- **Core (new, pure, testable):** a profile builder — logged rows (Z + freq) → frequency-binned Z
  envelope; and a comparator — live (Z, freq) → in/out of envelope + deviation. No UI, no native
  deps. Unit-tested like the rest of Core.
- **App:** overlay the locus + live point + deviation vector on `SmithChartControl`; manage named
  baselines (per antenna / per condition); an "off-baseline" alert.
- **Data source:** the Phase-1 TX log + Phase-2 CAT frequency — no new capture path. A baseline can
  be computed straight from the CSV log.

## Recommended direction
Build **B** as the headline antenna feature once Phase 1/2 land. Keep **A** parked as an optional
later add-on for resonant / antenna-side-coupler setups, using the solve-once pattern-cube approach
so it never needs a live solver or native NEC on the Pi.

## Open questions
- Baseline keying granularity vs the tuner's memory boundaries — one baseline per memory region?
- How to define/settle the "learned envelope" (sample count, outlier rejection, settle window).
- Multiple named baselines (per antenna, seasonal) and how the user switches/labels them.
