namespace Lp100a.Core;

/// <summary>
/// Pure state machine that turns a stream of <see cref="Lp100Reading"/> samples into discrete
/// transmission "overs". Feed every polled sample to <see cref="Observe"/>; when a key-up is
/// confirmed it returns the completed <see cref="TxOverRecord"/>, otherwise null.
///
/// Key-up rule (ported from the PowerShell w2-monitor's Track-Tx): an over ends ONLY on a
/// disconnect, or on a *valid* sub-threshold reading that has persisted past the hang time. Read
/// dropouts — a tick with no frame — are ignored, so a serial glitch cannot reset or restart the
/// timer mid-over. SSB/CW power also dips between syllables/elements, which the hang time absorbs.
///
/// The tracker holds no clock: the caller passes the sample time to every <see cref="Observe"/>
/// call, which keeps the whole thing deterministic and unit-testable.
/// </summary>
public sealed class TxOverTracker
{
    private readonly double _onThresholdW;
    private readonly double _hangSeconds;
    private readonly int _timeoutSeconds;
    private readonly int _minDurationSeconds;

    private bool _active;
    private DateTime _start;
    private DateTime _lastTx;
    private double _peakF;
    private double _maxSwr;
    private double _minSwr;
    private double _rAtPeak;
    private double _xAtPeak;
    private double _phaseAtPeak;
    private int _rangeAtPeak;
    private double? _freqMhz;

    /// <param name="onThresholdW">Forward power above which the meter is considered keyed. Default 0.1 W.</param>
    /// <param name="hangSeconds">A valid sub-threshold reading must persist this long to end an over. Default 2.0 s.</param>
    /// <param name="timeoutSeconds">Overs at or above this duration are flagged <see cref="TxOverRecord.TimedOut"/>. Default 180 s.</param>
    /// <param name="minDurationSeconds">Overs shorter than this are dropped, not emitted. Default 1 s.</param>
    public TxOverTracker(double onThresholdW = 0.1, double hangSeconds = 2.0,
        int timeoutSeconds = 180, int minDurationSeconds = 1)
    {
        _onThresholdW = onThresholdW;
        _hangSeconds = hangSeconds;
        _timeoutSeconds = timeoutSeconds;
        _minDurationSeconds = minDurationSeconds;
    }

    /// <summary>True while an over is in progress.</summary>
    public bool InOver => _active;

    /// <summary>
    /// Feed one polled sample. <paramref name="reading"/> is null on a read dropout (no frame this
    /// tick); <paramref name="freqMhz"/> is the latest CAT frequency, or null when unbound;
    /// <paramref name="now"/> is the sample time; <paramref name="connected"/> is the serial link
    /// state. Returns a completed over on confirmed key-up, otherwise null.
    /// </summary>
    public TxOverRecord? Observe(Lp100Reading? reading, double? freqMhz, DateTime now, bool connected = true)
    {
        var valid = connected && reading is not null;
        var txOn = valid && reading!.ForwardPowerW > _onThresholdW;

        if (txOn)
        {
            if (!_active) StartOver(now);
            _lastTx = now;
            Accumulate(reading!);
            if (freqMhz is not null) _freqMhz = freqMhz;
            return null;
        }

        if (!_active) return null;

        // An over is open but this tick isn't TX. End it only on a confirmed key-up: a disconnect,
        // or a valid low reading that has outlived the hang time. A dropout keeps the over alive.
        var keyUp = !connected || (valid && (now - _lastTx).TotalSeconds > _hangSeconds);
        return keyUp ? EndOver() : null;
    }

    private void StartOver(DateTime now)
    {
        _active = true;
        _start = now;
        _lastTx = now;
        _peakF = 0.0;
        _maxSwr = 0.0;
        _minSwr = double.MaxValue;
        _rAtPeak = _xAtPeak = _phaseAtPeak = 0.0;
        _rangeAtPeak = 0;
        _freqMhz = null;
    }

    private void Accumulate(Lp100Reading r)
    {
        if (r.Swr > _maxSwr) _maxSwr = r.Swr;
        if (r.Swr >= 1.0) _minSwr = Math.Min(_minSwr, r.Swr);
        if (r.ForwardPowerW > _peakF)
        {
            _peakF = r.ForwardPowerW;
            _rAtPeak = r.ResistanceOhms;
            _xAtPeak = r.ReactanceOhms;
            _phaseAtPeak = r.PhaseDeg;
            _rangeAtPeak = r.PowerRange;
        }
    }

    private TxOverRecord? EndOver()
    {
        _active = false;
        var duration = (int)(_lastTx - _start).TotalSeconds;
        if (duration < _minDurationSeconds) return null;

        return new TxOverRecord
        {
            Start = _start,
            FreqMhz = _freqMhz,
            DurationSeconds = duration,
            PeakForwardW = _peakF,
            MaxSwr = _maxSwr,
            MinSwr = _minSwr == double.MaxValue ? _maxSwr : _minSwr,
            ResistanceOhms = _rAtPeak,
            ReactanceOhms = _xAtPeak,
            PhaseDeg = _phaseAtPeak,
            PowerRange = _rangeAtPeak,
            TimedOut = duration >= _timeoutSeconds,
        };
    }
}
