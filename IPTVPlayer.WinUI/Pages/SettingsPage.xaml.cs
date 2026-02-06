using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        this.InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
    }
}
