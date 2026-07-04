using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lp100a.App.Views;

public partial class VectorWindow : Window
{
    public VectorWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (Application.Current as App)?.NotifyVectorClosing(this);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
