using Avalonia.Threading;
using Lp100a.Core;

namespace Lp100a.App.Services;

/// <summary>
/// Owns the current <see cref="IFrequencySource"/> and its lifecycle, and re-broadcasts its
/// background-thread updates on the UI thread. Phase 2 ships one source (Hamlib rigctld); the
/// interface is the seam where native Elecraft/Kenwood serial and FlexRadio sources land later.
/// </summary>
public sealed class FrequencyService : IDisposable
{
    private IFrequencySource? _source;
    private bool _enabled;
    private string _endpoint;

    public FrequencyService(bool enabled, string endpoint)
    {
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();
        _enabled = enabled;
        if (_enabled) StartSource();
    }

    public static string DefaultEndpoint => $"127.0.0.1:{RigctldProtocol.DefaultPort}";

    /// <summary>Fires on the UI thread when frequency, PTT, or connection status changes.</summary>
    public event Action? Changed;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (_enabled) StartSource(); else StopSource();
            Changed?.Invoke();
        }
    }

    /// <summary>rigctld "host" or "host:port". Applied on the next <see cref="Restart"/>.</summary>
    public string Endpoint
    {
        get => _endpoint;
        set => _endpoint = string.IsNullOrWhiteSpace(value) ? DefaultEndpoint : value.Trim();
    }

    /// <summary>Re-apply the current endpoint (the Setup "Apply" button).</summary>
    public void Restart()
    {
        StopSource();
        if (_enabled) StartSource();
        Changed?.Invoke();
    }

    /// <summary>Live frequency in MHz, or null when disabled/disconnected/unknown.</summary>
    public double? FreqMhz => _enabled ? _source?.FreqMhz : null;

    /// <summary>Live TX state, or null when unknown or the rig can't report it.</summary>
    public bool? IsTransmitting => _enabled ? _source?.IsTransmitting : null;

    public bool IsConnected => _enabled && (_source?.IsConnected ?? false);

    public string Status =>
        !_enabled ? "Off — the log's frequency column stays empty."
        : _source is null ? $"Bad address \"{_endpoint}\" — expected host or host:port."
        : _source.Status;

    public bool StatusIsError => _enabled && (_source is null || _source.StatusIsError);

    /// <summary>Frequency for display, e.g. "14.0740 MHz" or "—".</summary>
    public string FreqText => FreqMhz is { } f ? $"{f:0.0000} MHz" : "—";

    private void StartSource()
    {
        // A malformed endpoint leaves _source null, which Status surfaces as a clear message
        // rather than silently doing nothing.
        _source = RigctldFrequencySource.FromEndpoint(_endpoint);
        if (_source is null) return;
        _source.Changed += OnSourceChanged;
        _source.Start();
    }

    private void StopSource()
    {
        if (_source is null) return;
        _source.Changed -= OnSourceChanged;
        _source.Dispose();
        _source = null;
    }

    // Source events arrive on its poll thread; hop to the UI thread before telling any view.
    private void OnSourceChanged() => Dispatcher.UIThread.Post(() => Changed?.Invoke());

    public void Dispose() => StopSource();
}
