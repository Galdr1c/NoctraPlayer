using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IPTVPlayer.WinUI.Pages;

public sealed partial class HomePage : Page
{
    public MainViewModel ViewModel { get; }

    public HomePage()
    {
        this.InitializeComponent();
        ViewModel = App.Instance.Services.GetRequiredService<MainViewModel>();
        
        // Load data when page loads
        this.Loaded += (s, e) => _ = ViewModel.InitializeAsync();
    }
}
