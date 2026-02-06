using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer.WinUI.Pages;

public sealed partial class HomePage : Page
{
    public MainViewModel ViewModel { get; }

    public HomePage()
    {
        this.InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<MainViewModel>();
        
        // Load data when page loads
        this.Loaded += (s, e) => _ = ViewModel.InitializeAsync();
    }
}
