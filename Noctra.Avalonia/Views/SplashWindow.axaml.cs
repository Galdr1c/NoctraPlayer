using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Noctra.Avalonia.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
