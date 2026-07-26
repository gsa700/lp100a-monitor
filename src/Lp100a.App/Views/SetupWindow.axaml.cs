using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lp100a.App.Views;

public partial class SetupWindow : Window
{
    /// <summary>Breathing room under the tallest tab's last control.</summary>
    private const double BottomPadding = 12;

    private TabControl? _tabs;

    public SetupWindow()
    {
        InitializeComponent();
        // Close for real (its VM is retained in App, so reopening is cheap). Canceling the
        // close here would also cancel the owner's close -> the "two clicks to exit" bug.
        Closing += (_, _) => (Application.Current as App)?.NotifySetupClosing(this);
        Opened += (_, _) => LevelTabHeights();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _tabs = this.FindControl<TabControl>("Tabs");
    }

    /// <summary>
    /// Give every tab the height of the tallest one, so the window fits its biggest page and
    /// doesn't resize as you click between tabs.
    ///
    /// This replaces a hand-measured Height on the window, which silently went stale the moment
    /// anyone added a line of text to a tab — the symptom being a scrollbar reappearing on
    /// whichever page had grown. Measuring the real controls keeps it honest for free.
    ///
    /// Each tab is selected in turn to force its content to be built (a TabControl only realizes
    /// the selected page). The loop never yields, so no intermediate frame reaches the screen.
    /// </summary>
    private void LevelTabHeights()
    {
        if (_tabs is null || _tabs.ItemCount == 0) return;

        var contents = new List<Control>();
        var tallest = 0.0;
        var original = _tabs.SelectedIndex;

        for (var i = 0; i < _tabs.ItemCount; i++)
        {
            if (_tabs.ContainerFromIndex(i) is not TabItem tab) continue;
            _tabs.SelectedIndex = i;
            UpdateLayout();

            // The page itself, inside the tab's ScrollViewer.
            if (tab.Content is not ScrollViewer sv || sv.Content is not Control page) continue;
            contents.Add(page);
            tallest = Math.Max(tallest, page.Bounds.Height);
        }

        _tabs.SelectedIndex = original;

        if (tallest <= 0) return;
        foreach (var page in contents) page.MinHeight = tallest + BottomPadding;
        UpdateLayout();
    }
}
