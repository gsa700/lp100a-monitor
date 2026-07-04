using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lp100a.App.Views;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        // Close for real (its VM is retained in App, so reopening is cheap). Canceling the
        // close here would also cancel the owner's close -> the "two clicks to exit" bug.
        Closing += (_, _) => (Application.Current as App)?.NotifySetupClosing(this);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
