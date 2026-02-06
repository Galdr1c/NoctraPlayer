using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer.WinUI.Pages;

public sealed partial class PlayerPage : Page
{
    public PlayerViewModel ViewModel { get; }

    public PlayerPage()
    {
        this.InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<PlayerViewModel>();
    }
}
