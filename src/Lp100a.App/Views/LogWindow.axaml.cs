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
    // Resolved explicitly from the name scope. This class hand-writes InitializeComponent (like the
    // app's other windows), which shadows the XAML-generated one that would assign x:Name fields —
    // so the generated `Grid` field is never set. Relying on it crashed the app on the first
    // refresh (v0.9.11-beta). Look the control up instead, and treat it as optional.
    private DataGrid? _grid;
    private bool _scrollQueued;

    public LogWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (Application.Current as App)?.NotifyLogClosing(this);
        DataContextChanged += (_, _) => HookRows();
        // The view model loads its rows before the window is built, so that first fill raises no
        // CollectionChanged here — scroll once on open to land on the newest row.
        Opened += (_, _) => ScrollToNewest();
        HookRows();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _grid = this.FindControl<DataGrid>("Grid");
    }

    private void HookRows()
    {
        if (DataContext is LogViewModel vm)
        {
            vm.Rows.CollectionChanged -= OnRowsChanged;   // no double-subscribe if DataContext is reset
            vm.Rows.CollectionChanged += OnRowsChanged;
        }
    }

    // Rows are chronological, so the interesting end is the bottom. Scroll there whenever the list
    // reloads (Refresh, or a new over arriving).
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollQueued) return;   // a reload raises Clear + one event per row; scroll once
        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            ScrollToNewest();
        }, DispatcherPriority.Background);   // after the grid has realized the new rows
    }

    private void ScrollToNewest()
    {
        if (_grid is null) return;
        if (DataContext is not LogViewModel { Rows.Count: > 0 } vm) return;
        try { _grid.ScrollIntoView(vm.Rows[^1], null); }
        catch { /* grid not realized yet; the next reload will scroll */ }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
