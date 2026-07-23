using Lp100a.Core;

namespace Lp100a.App.Services;

/// <summary>
/// Bridges the live meter feed to the Core per-over logger. Runs a <see cref="TxOverTracker"/> over
/// every frame; when an over completes and logging is enabled, appends it via <see cref="TxLogWriter"/>.
///
/// All <see cref="MeterService"/> events arrive on the UI thread, so this needs no locking, and the
/// once-per-over file write is cheap enough to stay on that thread. Frequency comes from the
/// optional <see cref="FrequencyService"/>; with no source configured the log's Freq column stays
/// empty rather than carrying a guess.
/// </summary>
public sealed class TxLoggingService : IDisposable
{
    private readonly MeterService _meter;
    private readonly FrequencyService? _frequency;
    private readonly TxOverTracker _tracker = new();
    private readonly TxLogWriter _writer;
    private bool _wasLive;

    public TxLoggingService(MeterService meter, string logPath, bool enabled,
        FrequencyService? frequency = null)
    {
        _meter = meter;
        _frequency = frequency;
        _writer = new TxLogWriter(logPath);
        Enabled = enabled;
        _meter.ReadingReceived += OnReading;
        _meter.StateChanged += OnStateChanged;
    }

    /// <summary>When true, completed overs are written to the CSV.</summary>
    public bool Enabled { get; set; }

    /// <summary>Overs written this session.</summary>
    public int LoggedCount { get; private set; }

    /// <summary>Last write error, or null. Cleared on the next successful write.</summary>
    public string? LastError { get; private set; }

    public string LogPath => _writer.Path;

    /// <summary>Fires (UI thread) after an over is logged or a write fails, so a view can refresh.</summary>
    public event Action? Changed;

    private void OnReading(Lp100Reading r) => Feed(r, connected: true);

    private void OnStateChanged()
    {
        // A frozen (stale) or dropped link can't confirm a key-up via a low frame, so close any open
        // over when the feed stops being live. Only act on the live -> not-live edge.
        var live = _meter is { IsConnected: true, IsStale: false };
        if (_wasLive && !live) Feed(null, connected: false);
        _wasLive = live;
    }

    private void Feed(Lp100Reading? reading, bool connected)
    {
        // The tracker keeps the latest non-null frequency seen during the over, so a brief CAT
        // dropout mid-transmission doesn't blank the column.
        var over = _tracker.Observe(reading, _frequency?.FreqMhz, DateTime.Now, connected);
        if (over is null || !Enabled) return;

        try
        {
            _writer.Append(over);
            LoggedCount++;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _meter.ReadingReceived -= OnReading;
        _meter.StateChanged -= OnStateChanged;
    }
}
