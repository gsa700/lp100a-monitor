using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Lp100a.App.Views;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (Application.Current as App)?.NotifyLogClosing(this);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
