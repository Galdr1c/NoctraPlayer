using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IPTVPlayer.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        this.InitializeComponent();
        ViewModel = App.Instance.Services.GetRequiredService<SettingsViewModel>();
    }
}
