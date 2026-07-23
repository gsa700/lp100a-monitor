namespace Lp100a.Core;

/// <summary>
/// A source of live operating frequency (and, where the rig can report it, TX state) for stamping
/// onto logged transmissions.
///
/// <para><b>Why TX state matters:</b> the W2's CAT drivers were frequency-only, because that meter
/// reported which sampler was active and so did its own attribution. The LP-100A does not report
/// its active sampler over serial (confirmed with N8LP), so on a dual-coupler installation the
/// *radio* has to say who is keyed. <see cref="IsTransmitting"/> is nullable precisely because not
/// every rig/backend can answer that — and a source that can't must not be guessed at.</para>
///
/// Implementations poll on their own background thread; <see cref="Changed"/> therefore fires off
/// the UI thread and subscribers must marshal. Property reads are safe from any thread.
/// </summary>
public interface IFrequencySource : IDisposable
{
    /// <summary>Short human-readable name for the Setup UI, e.g. "rigctld 127.0.0.1:4532".</summary>
    string Name { get; }

    /// <summary>True while the source has a live connection to its rig/daemon.</summary>
    bool IsConnected { get; }

    /// <summary>Last status or error message, for display.</summary>
    string Status { get; }

    /// <summary>True when <see cref="Status"/> reports a fault rather than normal progress.</summary>
    bool StatusIsError { get; }

    /// <summary>Current operating frequency in MHz, or null when unknown/disconnected.</summary>
    double? FreqMhz { get; }

    /// <summary>
    /// Current TX state, or null when this source cannot report it. Null means "don't know" and
    /// must never be treated as "not transmitting".
    /// </summary>
    bool? IsTransmitting { get; }

    /// <summary>Fires on a background thread when frequency, PTT, or status changes.</summary>
    event Action? Changed;

    void Start();
    void Stop();
}
