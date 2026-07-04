using Avalonia.Media;

namespace Lp100a.App;

/// <summary>
/// Shared palette, matched to W2 Monitor so the two station tools look like a family.
/// XAML pulls the same colors from App.axaml resources; this is the code-side mirror.
/// </summary>
public static class Palette
{
    // Original pre-W2 palette experiment (kept in sync with App.axaml). W2 values in the comment
    // there; restore both files to revert. "Amber" now holds the original orange-red accent.
    public static readonly Color Bg = Color.FromRgb(23, 23, 23);
    public static readonly Color Panel = Color.FromRgb(30, 30, 30);
    public static readonly Color Track = Color.FromRgb(51, 51, 51);
    public static readonly Color Text = Color.FromRgb(234, 234, 234);
    public static readonly Color Amber = Color.FromRgb(255, 90, 42);
    public static readonly Color Green = Color.FromRgb(60, 200, 80);
    public static readonly Color Red = Color.FromRgb(230, 76, 76);
    public static readonly Color Dim = Color.FromRgb(153, 153, 153);
    public static readonly Color Cyan = Color.FromRgb(42, 200, 200);

    public static readonly IBrush AmberBrush = new SolidColorBrush(Amber);
    public static readonly IBrush GreenBrush = new SolidColorBrush(Green);
    public static readonly IBrush RedBrush = new SolidColorBrush(Red);
    public static readonly IBrush DimBrush = new SolidColorBrush(Dim);
    public static readonly IBrush TextBrush = new SolidColorBrush(Text);
    public static readonly IBrush CyanBrush = new SolidColorBrush(Cyan);
}
