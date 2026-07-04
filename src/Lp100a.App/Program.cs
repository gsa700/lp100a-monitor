using Avalonia;

namespace Lp100a.App;

internal static class Program
{
    // Avalonia entry point. Don't use any Avalonia/UI types before AppMain is called.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
