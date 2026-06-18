using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.ViewModels;
using MobileMainViewModel = Noctra.Mobile.ViewModels.MainViewModel;

namespace Noctra.Mobile.Views;

public partial class MainView : UserControl
{
    private const double TabletBreakpoint = 720;

    public MainView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateNavigationMode(e.NewSize.Width);
    }

    private void UpdateNavigationMode(double width)
    {
        var useNavigationRail = width >= TabletBreakpoint;
        NavigationRail.IsVisible = useNavigationRail;
        BottomNavigation.IsVisible = !useNavigationRail;
    }

    private void OnDestinationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string destination } &&
            DataContext is MobileMainViewModel viewModel)
        {
            viewModel.SelectDestination(destination);
            if (destination == "More" &&
                Application.Current is App app &&
                app.Services is not null)
            {
                MobileProfileList.DataContext = app.Services.GetRequiredService<ProfilesViewModel>();
            }
        }
    }
}
