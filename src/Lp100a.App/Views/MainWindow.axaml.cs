using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Lp100a.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = v.IndexOf('+');            // strip any SourceLink "+<hash>" build suffix
        if (plus >= 0) v = v[..plus];
        Title = string.IsNullOrEmpty(v) ? "LP-100A Monitor" : $"LP-100A Monitor v{v}";
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSetupClick(object? sender, RoutedEventArgs e) =>
        (Application.Current as App)?.ShowSetup();

    private void OnVectorClick(object? sender, RoutedEventArgs e) =>
        (Application.Current as App)?.ShowVector();
}
