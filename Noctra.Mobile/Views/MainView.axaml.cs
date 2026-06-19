using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
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
    private bool _isPlayerFullScreen;

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
        NavigationRail.IsVisible = useNavigationRail && !_isPlayerFullScreen;
        BottomNavigation.IsVisible = !useNavigationRail && !_isPlayerFullScreen;
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

            if (destination is "Live" or "Movies" or "Series" or "Search" or "Favorites" or "MyList" or "History")
            {
                _coreMainViewModel ??= app.Services.GetRequiredService<CoreMainViewModel>();
                _coreMainViewModel.OnMediaSelected -= CoreMainViewModel_OnMediaSelected;
                _coreMainViewModel.OnMediaSelected += CoreMainViewModel_OnMediaSelected;
                CoreContentHost.DataContext = _coreMainViewModel;
                MobileLiveContent.DataContext = _coreMainViewModel;
                MobileMoviesContent.DataContext = _coreMainViewModel;
                MobileSeriesContent.DataContext = _coreMainViewModel;
                MobileSearchContent.DataContext = _coreMainViewModel;
                MobileFavoritesContent.DataContext = _coreMainViewModel;
                MobileMyListContent.DataContext = _coreMainViewModel;
                MobileHistoryContent.DataContext = _coreMainViewModel;

                var targetView = destination switch
                {
                    "Live" => AppView.Live,
                    "Movies" => AppView.Movies,
                    "Series" => AppView.Series,
                    "Search" => AppView.Search,
                    "Favorites" => AppView.Favorites,
                    "MyList" => AppView.MyList,
                    "History" => AppView.History,
                    _ => AppView.Home
                };
                _coreMainViewModel.NavigateCommand.Execute(targetView);
            }

            UpdateContentVisibility(destination);
        }
    }

    private void UpdateContentVisibility(string destination)
    {
        var showCoreContent = destination is "Live" or "Movies" or "Series" or "Search" or "Favorites" or "MyList" or "History";
        ShellContent.IsVisible = !showCoreContent;
        CoreContentHost.IsVisible = showCoreContent;
        MobileLiveContent.IsVisible = destination == "Live";
        MobileMoviesContent.IsVisible = destination == "Movies";
        MobileSeriesContent.IsVisible = destination == "Series";
        MobileSearchContent.IsVisible = destination == "Search";
        MobileFavoritesContent.IsVisible = destination == "Favorites";
        MobileMyListContent.IsVisible = destination == "MyList";
        MobileHistoryContent.IsVisible = destination == "History";
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
        if (Application.Current is not App app || app.Services is null)
        {
            return;
        }

        _playerViewModel ??= app.Services.GetRequiredService<PlayerViewModel>();
        var videoSurfaceService = app.Services.GetService<IVideoSurfaceService>();
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

        var pictureInPictureService = app.Services.GetService<IPictureInPictureService>();
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
        if (Application.Current is App app && app.Services is not null)
        {
            app.Services.GetService<IVideoSurfaceService>()?.Hide();
        }
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
        if (Application.Current is not App app || app.Services is null)
        {
            return;
        }

        var pictureInPictureService = app.Services.GetService<IPictureInPictureService>();
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
