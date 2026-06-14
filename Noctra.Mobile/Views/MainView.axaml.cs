using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Mobile.ViewModels;
using AddProfileViewModel = Noctra.ViewModels.AddProfileViewModel;

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
            DataContext is MainViewModel viewModel)
        {
            viewModel.SelectDestination(destination);
        }
    }

    private void OnOpenProfileSetup(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app || app.Services is null)
        {
            return;
        }

        var viewModel = app.Services.GetRequiredService<AddProfileViewModel>();
        viewModel.RequestClose += OnProfileSetupClosed;
        ProfileSetupContent.Content = new ProfileSetupView
        {
            DataContext = viewModel
        };
        ProfileSetupHost.IsVisible = true;
    }

    private void OnProfileSetupClosed(object? sender, EventArgs e)
    {
        if (sender is AddProfileViewModel viewModel)
        {
            viewModel.RequestClose -= OnProfileSetupClosed;
        }

        ProfileSetupHost.IsVisible = false;
        ProfileSetupContent.Content = null;
    }
}
