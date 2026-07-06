using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lp100a.App.Controls;

/// <summary>
/// A horizontal bar with a filled level plus a thin peak-hold marker. <see cref="Value"/>,
/// <see cref="Maximum"/>, and <see cref="Peak"/> are in the same units (e.g. watts).
/// </summary>
public sealed class PeakBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PeakBar, double>(nameof(Value));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<PeakBar, double>(nameof(Maximum), 1.0);

    public static readonly StyledProperty<double> PeakProperty =
        AvaloniaProperty.Register<PeakBar, double>(nameof(Peak));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PeakBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<PeakBar, IBrush?>(nameof(Track));

    public static readonly StyledProperty<IBrush?> PeakBrushProperty =
        AvaloniaProperty.Register<PeakBar, IBrush?>(nameof(PeakBrush));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Peak { get => GetValue(PeakProperty); set => SetValue(PeakProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public IBrush? PeakBrush { get => GetValue(PeakBrushProperty); set => SetValue(PeakBrushProperty, value); }

    static PeakBar()
    {
        AffectsRender<PeakBar>(ValueProperty, MaximumProperty, PeakProperty,
            FillProperty, TrackProperty, PeakBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var max = Maximum > 0 ? Maximum : 1.0;

        if (Track is { } tb) context.DrawRectangle(tb, null, new Rect(0, 0, w, h));

        var fillW = Math.Clamp(Value / max, 0, 1) * w;
        if (fillW > 0 && Fill is { } fb) context.DrawRectangle(fb, null, new Rect(0, 0, fillW, h));

        if (Peak > 0 && PeakBrush is { } pb)
        {
            const double mw = 3.0;
            var x = Math.Clamp(Math.Clamp(Peak / max, 0, 1) * w - mw / 2, 0, Math.Max(0, w - mw));
            context.DrawRectangle(pb, null, new Rect(x, 0, mw, h));
        }
    }
}
