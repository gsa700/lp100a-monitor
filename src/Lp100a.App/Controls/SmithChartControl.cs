using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lp100a.App.Controls;

/// <summary>
/// A minimal Smith chart. Draws the constant-R circles and constant-X arcs, labels
/// them in ohms, then plots the normalized load impedance (R + jX) as a marker in the
/// reflection-coefficient (Γ) plane and draws the constant-SWR circle through it.
/// Set <see cref="Resistance"/> / <see cref="Reactance"/> in ohms (referenced to 50 Ω).
/// </summary>
public sealed class SmithChartControl : Control
{
    private const double Z0 = 50.0;

    public static readonly StyledProperty<double> ResistanceProperty =
        AvaloniaProperty.Register<SmithChartControl, double>(nameof(Resistance), 50.0);

    public static readonly StyledProperty<double> ReactanceProperty =
        AvaloniaProperty.Register<SmithChartControl, double>(nameof(Reactance), 0.0);

    public double Resistance
    {
        get => GetValue(ResistanceProperty);
        set => SetValue(ResistanceProperty, value);
    }

    public double Reactance
    {
        get => GetValue(ReactanceProperty);
        set => SetValue(ReactanceProperty, value);
    }

    /// <summary>When true, marker movement is recorded into the fading trail (set from TX state).</summary>
    public static readonly StyledProperty<bool> ActiveProperty =
        AvaloniaProperty.Register<SmithChartControl, bool>(nameof(Active));

    public bool Active
    {
        get => GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    static SmithChartControl()
    {
        AffectsRender<SmithChartControl>(ResistanceProperty, ReactanceProperty);
    }

    // Normalized grid lines to draw (× 50 Ω for the labels).
    private static readonly double[] RCircles = { 0.2, 0.5, 1.0, 2.0, 5.0 };
    private static readonly double[] XArcs = { 0.2, 0.5, 1.0, 2.0, 5.0 };
    private static readonly double[] XLabels = { 0.5, 1.0, 2.0 };  // labelled subset, keeps the rim readable

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly IBrush OuterBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly IBrush RLabelBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly IBrush XLabelBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly IBrush SwrBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0xC8, 0x50));
    private static readonly IBrush MarkerBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x2A));
    private static readonly IBrush BackBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));

    // Fading trail of recent operating points (Γ-plane) — see the impedance move while tuning.
    private static readonly TimeSpan TrailLife = TimeSpan.FromSeconds(3.0);
    private const double TrailEps = 0.006;
    private readonly List<(double x, double y, DateTime t)> _trail = new();
    private DispatcherTimer? _trailTimer;

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 2 || h <= 2) return;

        var cx = w / 2.0;
        var cy = h / 2.0;
        var radius = Math.Min(w, h) / 2.0 - 20.0;
        if (radius <= 4) return;

        var gridPen = new Pen(GridBrush, 1);
        var axisPen = new Pen(AxisBrush, 1);
        var outerPen = new Pen(OuterBrush, 1.5);

        Point Map(double gx, double gy) => new(cx + gx * radius, cy - gy * radius);

        context.DrawRectangle(BackBrush, null, new Rect(0, 0, w, h));

        // Grid detail is clipped to the unit circle.
        var unitDisk = new EllipseGeometry(new Rect(cx - radius, cy - radius, radius * 2, radius * 2));
        using (context.PushGeometryClip(unitDisk))
        {
            // Constant-resistance circles: center (r/(1+r), 0), radius 1/(1+r).
            foreach (var r in RCircles)
            {
                var gcx = r / (1.0 + r);
                var grad = 1.0 / (1.0 + r);
                context.DrawEllipse(null, gridPen, Map(gcx, 0), grad * radius, grad * radius);
            }

            // Constant-reactance arcs: center (1, 1/x), radius 1/|x|, both signs.
            foreach (var x in XArcs)
            {
                foreach (var sx in new[] { x, -x })
                {
                    var grad = 1.0 / Math.Abs(sx);
                    context.DrawEllipse(null, gridPen, Map(1.0, 1.0 / sx), grad * radius, grad * radius);
                }
            }

            context.DrawLine(axisPen, Map(-1, 0), Map(1, 0));
        }

        context.DrawEllipse(null, outerPen, new Point(cx, cy), radius, radius);

        // Current operating point in the Γ plane.
        var (hasPoint, gRe, gIm) = Gamma(Resistance, Reactance);
        var gMag = Math.Sqrt(gRe * gRe + gIm * gIm);

        // Constant-SWR circle through the operating point (centered at the match point).
        if (hasPoint && gMag > 0.01)
        {
            var swr = gMag >= 1.0 ? double.PositiveInfinity : (1.0 + gMag) / (1.0 - gMag);
            var swrPen = new Pen(SwrBrush, 1.5) { DashStyle = DashStyle.Dash };
            context.DrawEllipse(null, swrPen, new Point(cx, cy), gMag * radius, gMag * radius);

            var swrText = double.IsInfinity(swr) ? "SWR ∞" : $"SWR {swr:0.00}";
            DrawText(context, swrText, new Point(cx, cy - gMag * radius - 9), SwrBrush, 12, centered: true);
        }

        // 50 Ω match point.
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), null, new Point(cx, cy), 3, 3);

        // Resistance labels on the real axis (left crossing of each R circle), in ohms.
        foreach (var r in RCircles)
        {
            var gx = (r - 1.0) / (r + 1.0);
            var p = Map(gx, 0);
            DrawText(context, $"{r * Z0:0}", new Point(p.X, p.Y + 11), RLabelBrush, 10, centered: true);
        }

        // Reactance labels just outside the rim (both signs), in ohms.
        foreach (var x in XLabels)
        {
            foreach (var sx in new[] { x, -x })
            {
                var d = 1.0 + sx * sx;
                var bx = (sx * sx - 1.0) / d;     // Γ at r = 0 (on the unit circle)
                var by = 2.0 * sx / d;
                var sign = sx >= 0 ? "+j" : "−j";
                DrawText(context,
                    $"{sign}{Math.Abs(sx) * Z0:0}",
                    new Point(cx + bx * (radius + 12), cy - by * (radius + 12)),
                    XLabelBrush, 10, centered: true);
            }
        }

        // Fading trail of recent operating points, drawn under the marker.
        if (_trail.Count > 0)
        {
            var now = DateTime.UtcNow;
            var markerColor = ((SolidColorBrush)MarkerBrush).Color;
            foreach (var p in _trail)
            {
                var age = (now - p.t).TotalSeconds;
                if (age >= TrailLife.TotalSeconds) continue;
                var op = (1.0 - age / TrailLife.TotalSeconds) * 0.45;
                context.DrawEllipse(new SolidColorBrush(markerColor, op), null, Map(p.x, p.y), 4, 4);
            }
        }

        // Operating-point marker on top.
        if (hasPoint)
            context.DrawEllipse(MarkerBrush, new Pen(Brushes.White, 1.5), Map(gRe, gIm), 6, 6);
    }

    private static (bool ok, double gx, double gy) Gamma(double r, double x)
    {
        var rn = r / Z0;
        var xn = x / Z0;
        var den = (rn + 1.0) * (rn + 1.0) + xn * xn;
        if (den <= 1e-9) return (false, 0.0, 0.0);
        return (true, (rn * rn - 1.0 + xn * xn) / den, 2.0 * xn / den);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ReactanceProperty && Active) PushTrailPoint();
    }

    private void PushTrailPoint()
    {
        var (ok, gx, gy) = Gamma(Resistance, Reactance);
        if (!ok) return;
        if (_trail.Count > 0)
        {
            var last = _trail[^1];
            var dx = gx - last.x;
            var dy = gy - last.y;
            if (dx * dx + dy * dy < TrailEps * TrailEps) return;   // barely moved — don't spam points
        }
        _trail.Add((gx, gy, DateTime.UtcNow));
        if (_trail.Count > 120) _trail.RemoveAt(0);

        _trailTimer ??= CreateTrailTimer();
        if (!_trailTimer.IsEnabled) _trailTimer.Start();
        InvalidateVisual();
    }

    private DispatcherTimer CreateTrailTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        t.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            _trail.RemoveAll(p => now - p.t > TrailLife);
            InvalidateVisual();
            if (_trail.Count == 0) _trailTimer?.Stop();
        };
        return t;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _trailTimer?.Stop();
        _trail.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private static void DrawText(DrawingContext context, string text, Point at, IBrush brush, double size, bool centered)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);
        var origin = centered ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at;
        context.DrawText(ft, origin);
    }
}
