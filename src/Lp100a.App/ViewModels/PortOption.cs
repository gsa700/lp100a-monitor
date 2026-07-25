namespace Lp100a.App.ViewModels;

/// <summary>
/// A selectable serial port plus the USB adapter's chip serial, when one is readable.
///
/// Showing the serial is the difference between "which of these four COM ports is the meter?" and
/// just knowing — the LP-100A, a W2, and a transmitter can all be FTDI adapters on one machine, and
/// Windows renumbers them freely. It's the same serial the app pins the connection to, so what you
/// pick here is what gets followed across a renumber.
///
/// <see cref="Serial"/> is null wherever no stable serial is exposed (non-Windows, or an adapter
/// with no serial burned in), and the port then simply shows on its own.
/// </summary>
public sealed record PortOption(string Port, string? Serial)
{
    public string Display => Serial is null ? Port : $"{Port}  ({Serial})";
}
