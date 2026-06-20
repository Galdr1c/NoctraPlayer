using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.Mobile.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using CoreMainViewModel = Noctra.ViewModels.MainViewModel;
using MobileMainViewModel = Noctra.Mobile.ViewModels.MainViewModel;

namespace Noctra.Mobile.Views;

public partial class MainView : UserControl
{
    private const double TabletBreakpoint = 720;
    private CoreMainViewModel? _coreMainViewModel;
    private PlayerViewModel? _playerViewModel;
    private MobileViewModelResolver? _viewModelResolver;
    private MobilePlatformServiceResolver? _platformServiceResolver;
    private bool _isPlayerFullScreen;

    public MainView()
    {
        InitializeComponent();
        MobileSettingsContent.BackToProfilesRequested += (_, _) => ShowProfileSelection();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateNavigationMode(e.NewSize.Width);
    }

    private void UpdateNavigationMode(double width)
    {
        var useNavigationRail = width >= TabletBreakpoint;
        NavigationRail.IsVisible = useNavigationRail && !_isPlayerFullScreen;
        BottomNavigation.IsVisible = !useNavigationRail && !_isPlayerFullScreen;
    }

    private void OnDestinationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string destination })
        {
            NavigateToDestination(destination);
        }
    }

    private void UpdateContentVisibility(string destination)
    {
        var showCoreContent = destination is "Home" or "Live" or "Movies" or "Series" or "Search" or "Favorites" or "MyList" or "History" or "Downloads" or "Settings";
        ShellContent.IsVisible = !showCoreContent;
        CoreContentHost.IsVisible = showCoreContent;
        MobileHomeContent.IsVisible = destination == "Home";
        MobileLiveContent.IsVisible = destination == "Live";
        MobileMoviesContent.IsVisible = destination == "Movies";
        MobileSeriesContent.IsVisible = destination == "Series";
        MobileSearchContent.IsVisible = destination == "Search";
        MobileFavoritesContent.IsVisible = destination == "Favorites";
        MobileMyListContent.IsVisible = destination == "MyList";
        MobileHistoryContent.IsVisible = destination == "History";
        MobileDownloadsContent.IsVisible = destination == "Downloads";
        MobileSettingsContent.IsVisible = destination == "Settings";
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MobileMainViewModel viewModel)
        {
            viewModel.SelectDestination("Settings");
            var resolver = GetViewModelResolver();
            if (resolver is not null)
            {
                MobileSettingsContent.DataContext = resolver.GetSettingsViewModel();
            }

            UpdateContentVisibility("Settings");
        }
    }

    private void ShowProfileSelection()
    {
        if (DataContext is MobileMainViewModel viewModel)
        {
            viewModel.SelectDestination("More");
        }

        var resolver = GetViewModelResolver();
        if (resolver is not null)
        {
            MobileProfileList.DataContext = resolver.GetProfilesViewModel();
        }

        UpdateContentVisibility("More");
    }

    private MobileViewModelResolver? GetViewModelResolver()
    {
        if (_viewModelResolver is not null)
        {
            return _viewModelResolver;
        }

        if (Application.Current is not App { Services: not null } app)
        {
            return null;
        }

        _viewModelResolver = app.Services.GetRequiredService<MobileViewModelResolver>();
        return _viewModelResolver;
    }

    private MobilePlatformServiceResolver? GetPlatformServiceResolver()
    {
        if (_platformServiceResolver is not null)
        {
            return _platformServiceResolver;
        }

        if (Application.Current is not App { Services: not null } app)
        {
            return null;
        }

        _platformServiceResolver = app.Services.GetRequiredService<MobilePlatformServiceResolver>();
        return _platformServiceResolver;
    }

    internal void NavigateToDestination(string destination)
    {
        if (DataContext is not MobileMainViewModel viewModel)
        {
            return;
        }

        viewModel.SelectDestination(destination);
        var resolver = GetViewModelResolver();
        if (resolver is null)
        {
            return;
        }

        if (destination == "More")
        {
            MobileProfileList.DataContext = resolver.GetProfilesViewModel();
        }

        if (destination == "Settings")
        {
            MobileSettingsContent.DataContext = resolver.GetSettingsViewModel();
        }

        if (destination is "Home" or "Live" or "Movies" or "Series" or "Search" or "Favorites" or "MyList" or "History" or "Downloads")
        {
            _coreMainViewModel ??= resolver.GetCoreMainViewModel();
            _coreMainViewModel.OnMediaSelected -= CoreMainViewModel_OnMediaSelected;
            _coreMainViewModel.OnMediaSelected += CoreMainViewModel_OnMediaSelected;
            CoreContentHost.DataContext = _coreMainViewModel;
            MobileHomeContent.DataContext = _coreMainViewModel;
            MobileLiveContent.DataContext = _coreMainViewModel;
            MobileMoviesContent.DataContext = _coreMainViewModel;
            MobileSeriesContent.DataContext = _coreMainViewModel;
            MobileSearchContent.DataContext = _coreMainViewModel;
            MobileFavoritesContent.DataContext = _coreMainViewModel;
            MobileMyListContent.DataContext = _coreMainViewModel;
            MobileHistoryContent.DataContext = _coreMainViewModel;
            MobileDownloadsContent.DataContext = _coreMainViewModel;

            var targetView = destination switch
            {
                "Live" => AppView.Live,
                "Movies" => AppView.Movies,
                "Series" => AppView.Series,
                "Search" => AppView.Search,
                "Favorites" => AppView.Favorites,
                "MyList" => AppView.MyList,
                "History" => AppView.History,
                "Downloads" => AppView.Downloads,
                _ => AppView.Home
            };
            _coreMainViewModel.NavigateCommand.Execute(targetView);
        }

        UpdateContentVisibility(destination);
    }

    private async void CoreMainViewModel_OnMediaSelected(object media)
    {
        switch (media)
        {
            case Channel channel:
                SelectedMediaTitle.Text = channel.Name;
                SelectedMediaSubtitle.Text = "Starting Android playback.";
                await PlaySelectedChannelAsync(channel);
                break;
            case Series series:
                SelectedMediaTitle.Text = series.Name;
                SelectedMediaSubtitle.Text = "Series detail selection is ready.";
                break;
            default:
                SelectedMediaTitle.Text = media.GetType().Name;
                SelectedMediaSubtitle.Text = "Media selection is ready.";
                break;
        }

        SelectedMediaHost.IsVisible = true;
    }

    private async Task PlaySelectedChannelAsync(Channel channel)
    {
        var resolver = GetViewModelResolver();
        if (resolver is null)
        {
            return;
        }

        _playerViewModel ??= resolver.GetPlayerViewModel();

        var platformResolver = GetPlatformServiceResolver();
        var videoSurfaceService = platformResolver?.GetVideoSurfaceService();
        if (videoSurfaceService is not null)
        {
            await videoSurfaceService.ShowAsync();
        }

        _playerViewModel.CloseRequested -= PlayerViewModel_CloseRequested;
        _playerViewModel.CloseRequested += PlayerViewModel_CloseRequested;
        _playerViewModel.NextLiveChannelRequested -= PlayerViewModel_NextLiveChannelRequested;
        _playerViewModel.NextLiveChannelRequested += PlayerViewModel_NextLiveChannelRequested;
        _playerViewModel.PreviousLiveChannelRequested -= PlayerViewModel_PreviousLiveChannelRequested;
        _playerViewModel.PreviousLiveChannelRequested += PlayerViewModel_PreviousLiveChannelRequested;
        _playerViewModel.NextEpisodeRequested -= PlayerViewModel_NextEpisodeRequested;
        _playerViewModel.NextEpisodeRequested += PlayerViewModel_NextEpisodeRequested;
        _playerViewModel.EpisodeRequested -= PlayerViewModel_EpisodeRequested;
        _playerViewModel.EpisodeRequested += PlayerViewModel_EpisodeRequested;
        _playerViewModel.PiPRequested -= PlayerViewModel_PiPRequested;
        _playerViewModel.PiPRequested += PlayerViewModel_PiPRequested;
        _playerViewModel.PropertyChanged -= PlayerViewModel_PropertyChanged;
        _playerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;

        var pictureInPictureService = platformResolver?.GetPictureInPictureService();
        if (pictureInPictureService is not null)
        {
            pictureInPictureService.PictureInPictureModeChanged -= PictureInPictureService_ModeChanged;
            pictureInPictureService.PictureInPictureModeChanged += PictureInPictureService_ModeChanged;
        }

        var coreViewModel = _coreMainViewModel;
        _playerViewModel.CurrentProfileId = coreViewModel?.CurrentProfileId;

        if (channel.Type == ChannelType.Series && coreViewModel?.CurrentEpisodePlaybackContext is null)
        {
            coreViewModel?.TryPrepareEpisodePlaybackContext(channel);
        }

        if (channel.Type == ChannelType.Series && coreViewModel?.CurrentEpisodePlaybackContext is not null)
        {
            _playerViewModel.SetCurrentEpisode(
                coreViewModel.CurrentEpisodePlaybackContext,
                coreViewModel.NextEpisodePlaybackContext,
                coreViewModel.CurrentSeriesPlaybackContext);
        }
        else
        {
            _playerViewModel.SetCurrentEpisode(null, null);
        }

        MobilePlayerContent.DataContext = _playerViewModel;
        PlayerHost.IsVisible = true;
        UpdatePlayerChromeState();
        await _playerViewModel.PlayChannelAsync(channel);
    }

    private void PlayerViewModel_CloseRequested(object? sender, EventArgs e)
    {
        if (_playerViewModel is not null)
        {
            _playerViewModel.IsFullScreen = false;
            _playerViewModel.IsLocked = false;
            _playerViewModel.IsPiPMode = false;
        }

        PlayerHost.IsVisible = false;
        UpdatePlayerChromeState();
        GetPlatformServiceResolver()?.GetVideoSurfaceService()?.Hide();
    }

    private void PlayerViewModel_NextLiveChannelRequested(object? sender, EventArgs e)
    {
        _coreMainViewModel?.PlayNextLiveChannelCommand.Execute(null);
    }

    private void PlayerViewModel_PreviousLiveChannelRequested(object? sender, EventArgs e)
    {
        _coreMainViewModel?.PlayPreviousLiveChannelCommand.Execute(null);
    }

    private void PlayerViewModel_NextEpisodeRequested(object? sender, Episode episode)
    {
        _coreMainViewModel?.PlayEpisodeCommand.Execute(episode);
    }

    private void PlayerViewModel_EpisodeRequested(object? sender, Episode episode)
    {
        _coreMainViewModel?.PlayEpisodeCommand.Execute(episode);
    }

    private async void PlayerViewModel_PiPRequested(object? sender, EventArgs e)
    {
        var pictureInPictureService = GetPlatformServiceResolver()?.GetPictureInPictureService();
        if (pictureInPictureService is null)
        {
            return;
        }

        var entered = await pictureInPictureService.EnterPictureInPictureAsync();
        if (_playerViewModel is not null)
        {
            _playerViewModel.IsPiPMode = entered;
        }
    }

    private void PictureInPictureService_ModeChanged(
        object? sender,
        PictureInPictureModeChangedEventArgs e)
    {
        if (_playerViewModel is not null)
        {
            _playerViewModel.IsPiPMode = e.IsInPictureInPictureMode;
        }
    }

    private void PlayerViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsFullScreen))
        {
            UpdatePlayerChromeState();
        }
    }

    private void UpdatePlayerChromeState()
    {
        _isPlayerFullScreen = PlayerHost.IsVisible && _playerViewModel?.IsFullScreen == true;
        HeaderBar.IsVisible = !_isPlayerFullScreen;
        SelectedMediaHost.IsVisible = !_isPlayerFullScreen && SelectedMediaHost.IsVisible;
        UpdateNavigationMode(Bounds.Width);
    }
}
