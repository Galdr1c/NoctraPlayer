using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.ViewModels;
using CoreMainViewModel = Noctra.ViewModels.MainViewModel;
using MobileMainViewModel = Noctra.Mobile.ViewModels.MainViewModel;

namespace Noctra.Mobile.Views;

public partial class MainView : UserControl
{
    private const double TabletBreakpoint = 720;
    private CoreMainViewModel? _coreMainViewModel;

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
            if (Application.Current is not App app ||
                app.Services is null)
            {
                return;
            }

            if (destination == "More")
            {
                MobileProfileList.DataContext = app.Services.GetRequiredService<ProfilesViewModel>();
            }

            if (destination is "Live" or "Movies" or "Series")
            {
                _coreMainViewModel ??= app.Services.GetRequiredService<CoreMainViewModel>();
                _coreMainViewModel.OnMediaSelected -= CoreMainViewModel_OnMediaSelected;
                _coreMainViewModel.OnMediaSelected += CoreMainViewModel_OnMediaSelected;
                CoreContentHost.DataContext = _coreMainViewModel;
                MobileLiveContent.DataContext = _coreMainViewModel;
                MobileMoviesContent.DataContext = _coreMainViewModel;
                MobileSeriesContent.DataContext = _coreMainViewModel;

                var targetView = destination switch
                {
                    "Live" => AppView.Live,
                    "Movies" => AppView.Movies,
                    "Series" => AppView.Series,
                    _ => AppView.Home
                };
                _coreMainViewModel.NavigateCommand.Execute(targetView);
            }

            UpdateContentVisibility(destination);
        }
    }

    private void UpdateContentVisibility(string destination)
    {
        var showCoreContent = destination is "Live" or "Movies" or "Series";
        ShellContent.IsVisible = !showCoreContent;
        CoreContentHost.IsVisible = showCoreContent;
        MobileLiveContent.IsVisible = destination == "Live";
        MobileMoviesContent.IsVisible = destination == "Movies";
        MobileSeriesContent.IsVisible = destination == "Series";
    }

    private void CoreMainViewModel_OnMediaSelected(object media)
    {
        switch (media)
        {
            case Channel channel:
                SelectedMediaTitle.Text = channel.Name;
                SelectedMediaSubtitle.Text = channel.Type == ChannelType.Live
                    ? "Live selection is ready for Android playback."
                    : "Movie selection is ready for Android playback.";
                break;
            case Series series:
                SelectedMediaTitle.Text = series.Name;
                SelectedMediaSubtitle.Text = "Series detail selection is ready for Android playback.";
                break;
            default:
                SelectedMediaTitle.Text = media.GetType().Name;
                SelectedMediaSubtitle.Text = "Media selection is ready.";
                break;
        }

        SelectedMediaHost.IsVisible = true;
    }
}
