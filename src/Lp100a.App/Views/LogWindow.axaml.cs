using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lp100a.App.ViewModels;

namespace Lp100a.App.Views;

public partial class LogWindow : Window
{
    private bool _scrollQueued;

    public LogWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (Application.Current as App)?.NotifyLogClosing(this);
        DataContextChanged += (_, _) => HookRows();
        HookRows();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void HookRows()
    {
        if (DataContext is LogViewModel vm)
            vm.Rows.CollectionChanged += OnRowsChanged;
    }

    // Rows are chronological, so the interesting end is the bottom. Scroll there whenever the list
    // reloads (window opening, Refresh, a new over arriving).
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollQueued) return;   // a reload raises Clear + one event per row; scroll once
        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            if (DataContext is not LogViewModel { Rows.Count: > 0 } vm) return;
            Grid.ScrollIntoView(vm.Rows[^1], null);
        }, DispatcherPriority.Background);   // after the grid has realized the new rows
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
