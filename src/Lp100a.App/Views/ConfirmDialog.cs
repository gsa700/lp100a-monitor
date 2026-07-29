using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lp100a.App.Views;

/// <summary>
/// The small modal dialogs the install flow needs. Avalonia ships no message box, and these are
/// the only places the app wants one, so they are built in code rather than earning .axaml views.
///
/// Two shapes: a question with two answers (<see cref="ShowAsync"/>) and a statement with one
/// (<see cref="ShowNoticeAsync"/>). A statement must not be dressed as a question — a dialog with
/// two identical buttons asks the reader to choose between two things that are the same.
/// </summary>
public static class ConfirmDialog
{
    /// <summary>
    /// Ask a yes/no question. Returns true for the affirmative answer.
    /// </summary>
    /// <remarks>
    /// The affirmative is never the focused button. These dialogs ask about copying files onto the
    /// machine and about deleting operating history; leaning on the keyboard must not agree to
    /// either, and closing with the title-bar X reads as the negative answer.
    /// </remarks>
    public static Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string affirmative,
        string negative,
        string? detail = null,
        bool danger = false)
    {
        var no = MakeButton(negative);
        var yes = MakeButton(affirmative);
        if (danger) yes.Foreground = new SolidColorBrush(Palette.Red);

        var dialog = Build(title, message, detail, [no, yes]);

        no.Click += (_, _) => dialog.Close(false);
        yes.Click += (_, _) => dialog.Close(true);
        dialog.Opened += (_, _) => no.Focus();

        return dialog.ShowDialog<bool>(owner);
    }

    /// <summary>
    /// State something and wait for acknowledgement. One button, because there is only one
    /// possible response.
    /// </summary>
    public static Task ShowNoticeAsync(
        Window owner,
        string title,
        string message,
        string? detail = null,
        string dismiss = "OK")
    {
        var ok = MakeButton(dismiss);
        var dialog = Build(title, message, detail, [ok]);

        ok.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => ok.Focus();

        return dialog.ShowDialog(owner);
    }

    private static Button MakeButton(string text) =>
        new() { Content = text, MinWidth = 92, Padding = new Avalonia.Thickness(14, 6) };

    private static Window Build(string title, string message, string? detail, IReadOnlyList<Button> buttons)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Palette.Bg),
        };

        var body = new StackPanel { Margin = new Avalonia.Thickness(22, 20, 22, 18), Spacing = 12 };

        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = new SolidColorBrush(Palette.Text),
        });

        if (!string.IsNullOrWhiteSpace(detail))
        {
            body.Children.Add(new TextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(Palette.Dim),
            });
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
        };
        foreach (var b in buttons) row.Children.Add(b);
        body.Children.Add(row);

        dialog.Content = body;
        return dialog;
    }
}
