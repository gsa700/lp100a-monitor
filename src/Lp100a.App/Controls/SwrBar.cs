using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lp100a.App.Controls;

/// <summary>
/// The SWR bar. In normal use it draws a green→orange→red gradient fill mapped across the
/// <see cref="Minimum"/>–<see cref="Maximum"/> SWR scale, so the fill's leading edge colour
/// tells you the severity at a glance. When <see cref="Alarm"/> is set, the whole bar becomes
/// the alarm: it flashes red and shows <see cref="AlarmText"/> centred inside it (replacing the
/// old separate HIGH SWR banner).
/// </summary>
public sealed class SwrBar : Control
{
    // green (low) → orange (mid) → red (high), mapped across the SWR scale.
    private static readonly Color LowColor = Color.Parse("#3CC850");
    private static readonly Color MidColor = Color.Parse("#FF8C00");
    private static readonly Color HighColor = Color.Parse("#E64C4C");

    // Alarm flash: alternate a bright red with the deep-red banner tone used elsewhere.
    private static readonly IBrush AlarmBright = new SolidColorBrush(Color.Parse("#E64C4C"));
    private static readonly IBrush AlarmDim = new SolidColorBrush(Color.Parse("#3A1414"));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SwrBar, double>(nameof(Value), 1.0);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<SwrBar, double>(nameof(Minimum), 1.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SwrBar, double>(nameof(Maximum), 3.0);

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<SwrBar, IBrush?>(nameof(Track));

    public static readonly StyledProperty<double> AlarmSetpointProperty =
        AvaloniaProperty.Register<SwrBar, double>(nameof(AlarmSetpoint));

    public static readonly StyledProperty<bool> AlarmProperty =
        AvaloniaProperty.Register<SwrBar, bool>(nameof(Alarm));

    public static readonly StyledProperty<string?> AlarmTextProperty =
        AvaloniaProperty.Register<SwrBar, string?>(nameof(AlarmText));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>Meter SWR alarm setpoint (0 = none/Off/User → fixed fallback gradient).</summary>
    public double AlarmSetpoint { get => GetValue(AlarmSetpointProperty); set => SetValue(AlarmSetpointProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public bool Alarm { get => GetValue(AlarmProperty); set => SetValue(AlarmProperty, value); }
    public string? AlarmText { get => GetValue(AlarmTextProperty); set => SetValue(AlarmTextProperty, value); }

    private readonly DispatcherTimer _flashTimer;
    private bool _flashOn = true;

    static SwrBar()
    {
        AffectsRender<SwrBar>(ValueProperty, MinimumProperty, MaximumProperty,
            TrackProperty, AlarmProperty, AlarmTextProperty, AlarmSetpointProperty);
    }

    public SwrBar()
    {
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _flashTimer.Tick += (_, _) => { _flashOn = !_flashOn; InvalidateVisual(); };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AlarmProperty)
        {
            if (Alarm) { _flashOn = true; _flashTimer.Start(); }
            else _flashTimer.Stop();
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _flashTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var full = new Rect(0, 0, w, h);

        if (Alarm)
        {
            context.DrawRectangle(_flashOn ? AlarmBright : AlarmDim, null, full);
            DrawAlarmText(context, w, h);
            return;
        }

        if (Track is { } tb) context.DrawRectangle(tb, null, full);

        var span = Maximum - Minimum;
        if (span <= 0) return;
        var frac = Math.Clamp((Value - Minimum) / span, 0, 1);
        var fillW = frac * w;
        if (fillW <= 0) return;

        // Gradient axis anchored to the FULL width (absolute), so the fill (0..fillW) samples the
        // left portion — the leading edge sits at the colour for the current SWR.
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(w, 0, RelativeUnit.Absolute),
        };
        foreach (var stop in BuildStops(span)) brush.GradientStops.Add(stop);
        context.DrawRectangle(brush, null, new Rect(0, 0, fillW, h));
    }

    /// <summary>
    /// Colour breakpoints for the fill. When a numeric alarm setpoint is known, red is anchored
    /// at the setpoint (where the meter's alarm trips) with orange approaching and green safely
    /// below — so the bar "goes red where your alarm goes off". Otherwise a fixed green→orange→red.
    /// </summary>
    private IEnumerable<GradientStop> BuildStops(double span)
    {
        var s = AlarmSetpoint;
        if (s > Minimum && s <= Maximum)
        {
            double Frac(double swr) => Math.Clamp((swr - Minimum) / span, 0, 1);
            var greenHold = Minimum + 0.50 * (s - Minimum);  // solid green to halfway
            var orange = Minimum + 0.82 * (s - Minimum);     // orange approaching the setpoint
            return new[]
            {
                new GradientStop(LowColor, 0.0),
                new GradientStop(LowColor, Frac(greenHold)),
                new GradientStop(MidColor, Frac(orange)),
                new GradientStop(HighColor, Frac(s)),        // red at the setpoint…
                new GradientStop(HighColor, 1.0),            // …and beyond
            };
        }

        return new[]
        {
            new GradientStop(LowColor, 0.0),
            new GradientStop(LowColor, 0.28),   // hold green through ~SWR 1.5
            new GradientStop(MidColor, 0.55),   // orange around SWR 2.0
            new GradientStop(HighColor, 1.0),   // red toward SWR 3.0
        };
    }

    private void DrawAlarmText(DrawingContext context, double w, double h)
    {
        if (string.IsNullOrEmpty(AlarmText)) return;
        var size = Math.Clamp(h * 0.62, 10, 15);
        var ft = new FormattedText(
            AlarmText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            size,
            Brushes.White);
        context.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }
}
