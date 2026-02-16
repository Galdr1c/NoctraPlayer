using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Avalonia.Controls;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Avalonia;

public partial class MainWindow : Window
{
    private readonly IVideoPlayerService _videoPlayerService;
    private readonly MainViewModel _mainViewModel;
    private readonly PlayerViewModel _playerViewModel;
    private CancellationTokenSource? _imageWarmupCts;

    public MainWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<MainViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<PlayerViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<IVideoPlayerService>())
    {
    }

    public MainWindow(MainViewModel mainViewModel, PlayerViewModel playerViewModel, IVideoPlayerService videoPlayerService)
    {
        InitializeComponent();

        _mainViewModel = mainViewModel;
        _playerViewModel = playerViewModel;
        _videoPlayerService = videoPlayerService;
        DataContext = _mainViewModel;

        PlayerOverlayLayer.DataContext = _playerViewModel;
        OverlayControl.DataContext = _playerViewModel;
        NextEpisodePrompt.DataContext = _playerViewModel;
        VideoSurface.MediaPlayer = _videoPlayerService.GetMediaPlayer();
        MiniVideoSurface.MediaPlayer = null;
        AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Closed += OnClosed;
        _mainViewModel.OnMediaSelected += MainViewModel_OnMediaSelected;
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        _mainViewModel.RequestEditChannel += MainViewModel_RequestEditChannel;
        _playerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;
        _playerViewModel.CloseRequested += PlayerViewModel_CloseRequested;
        _playerViewModel.EpisodeRequested += PlayerViewModel_EpisodeRequested;
        _playerViewModel.EpisodeProgressUpdated += PlayerViewModel_EpisodeProgressUpdated;
        _playerViewModel.NextEpisodeRequested += PlayerViewModel_NextEpisodeRequested;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _mainViewModel.OnMediaSelected -= MainViewModel_OnMediaSelected;
        _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
        _mainViewModel.RequestEditChannel -= MainViewModel_RequestEditChannel;
        _playerViewModel.PropertyChanged -= PlayerViewModel_PropertyChanged;
        _playerViewModel.CloseRequested -= PlayerViewModel_CloseRequested;
        _playerViewModel.EpisodeRequested -= PlayerViewModel_EpisodeRequested;
        _playerViewModel.EpisodeProgressUpdated -= PlayerViewModel_EpisodeProgressUpdated;
        _playerViewModel.NextEpisodeRequested -= PlayerViewModel_NextEpisodeRequested;
        VideoSurface.MediaPlayer = null;
        MiniVideoSurface.MediaPlayer = null;
        _imageWarmupCts?.Cancel();
        _imageWarmupCts?.Dispose();
        _imageWarmupCts = null;
    }

    // === Window Chrome ===
    private void HeaderBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    // === Settings ===
    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = ((App)Application.Current!).Services.GetRequiredService<Views.SettingsWindow>();
        await settingsWindow.ShowDialog(this);
    }

    // === Group Filter Reset ===
    private void ClearGroupSelection_Click(object? sender, RoutedEventArgs e)
    {
        _mainViewModel.SelectedGroup = null;
    }

    // === Search Overlay Keyboard ===
    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _mainViewModel.CommitSearchCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            _mainViewModel.CloseSearchCommand.Execute(null);
        }
    }

    // === Media Selection ===
    private async void MainViewModel_OnMediaSelected(object? media)
    {
        if (media is not Channel channel)
        {
            return;
        }

        _playerViewModel.CurrentProfileId = _mainViewModel.CurrentProfileId;
        if (channel.Type == ChannelType.Series && _mainViewModel.CurrentEpisodePlaybackContext == null)
        {
            _mainViewModel.TryPrepareEpisodePlaybackContext(channel);
        }

        if (channel.Type == ChannelType.Series && _mainViewModel.CurrentEpisodePlaybackContext != null)
        {
            _playerViewModel.SetCurrentEpisode(
                _mainViewModel.CurrentEpisodePlaybackContext,
                _mainViewModel.NextEpisodePlaybackContext,
                _mainViewModel.CurrentSeriesPlaybackContext);
        }
        else
        {
            _playerViewModel.SetCurrentEpisode(null, null);
        }

        HideMiniPlayer();
        PlayerArea.IsVisible = true;
        _playerViewModel.IsLocked = false;
        _playerViewModel.UserInteractionCommand.Execute(null);
        await _playerViewModel.PlayChannelAsync(channel);
        Dispatcher.UIThread.Post(() => OverlayControl.Focus(), DispatcherPriority.Input);
    }

    private void PlayerViewModel_NextEpisodeRequested(object? sender, Episode episode)
    {
        _mainViewModel.PlayEpisodeCommand.Execute(episode);
    }

    private void PlayerViewModel_EpisodeRequested(object? sender, Episode episode)
    {
        _mainViewModel.PlayEpisodeCommand.Execute(episode);
    }

    private void PlayerViewModel_EpisodeProgressUpdated(object? sender, Episode episode)
    {
        _mainViewModel.SyncEpisodeProgress(episode);
    }

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.FilteredChannels) or
            nameof(MainViewModel.TrendingChannels) or
            nameof(MainViewModel.LatestMovies) or
            nameof(MainViewModel.LatestSeries) or
            nameof(MainViewModel.SeriesViewItems) or
            nameof(MainViewModel.FeaturedChannel))
        {
            ScheduleImageWarmup();
        }
    }

    private void PlayerViewModel_CloseRequested(object? sender, EventArgs e)
    {
        PlayerArea.IsVisible = false;
        _playerViewModel.IsLocked = false;
        UpdateMiniPlayerVisibility();
    }

    private async void MainViewModel_RequestEditChannel(Channel channel)
    {
        try
        {
            var services = ((App)Application.Current!).Services;
            var viewModel = services.GetRequiredService<EditChannelViewModel>();
            viewModel.Initialize(channel);

            var window = new Views.EditChannelWindow(viewModel);
            await window.ShowDialog<bool>(this);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Failed to open EditChannelWindow.", ex);
        }
    }

    private void ScheduleImageWarmup()
    {
        _imageWarmupCts?.Cancel();
        _imageWarmupCts?.Dispose();
        _imageWarmupCts = new CancellationTokenSource();
        var token = _imageWarmupCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(140, token).ConfigureAwait(false);
                await WarmupVisibleImagesAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async Task WarmupVisibleImagesAsync(CancellationToken cancellationToken)
    {
        var urls = new List<string?>(220);

        if (_mainViewModel.FeaturedChannel != null)
        {
            urls.Add(_mainViewModel.FeaturedChannel.CoverUrl);
            urls.Add(_mainViewModel.FeaturedChannel.LogoUrl);
        }

        urls.AddRange(_mainViewModel.TrendingChannels.Take(50).Select(c => c.LogoUrl ?? c.CoverUrl));
        urls.AddRange(_mainViewModel.LatestMovies.Take(50).Select(c => c.CoverUrl ?? c.LogoUrl));
        urls.AddRange(_mainViewModel.FilteredChannels.Take(120).Select(c => c.CoverUrl ?? c.LogoUrl));
        urls.AddRange(_mainViewModel.LatestSeries.Take(60).Select(s => s.CoverUrl));
        urls.AddRange(_mainViewModel.SeriesViewItems.Take(60).Select(s => s.CoverUrl));

        await RemoteImage.PreloadAsync(urls, maxCount: 180, cancellationToken).ConfigureAwait(false);
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !PlayerArea.IsVisible)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                _playerViewModel.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                if (_playerViewModel.IsFullScreen)
                {
                    _playerViewModel.ToggleFullScreenCommand.Execute(null);
                }
                else
                {
                    _playerViewModel.ClosePlayerCommand.Execute(null);
                }
                e.Handled = true;
                break;
            case Key.F:
                _playerViewModel.ToggleFullScreenCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                if (!_playerViewModel.IsLiveContent)
                {
                    _playerViewModel.SkipBackwardCommand.Execute(10);
                    e.Handled = true;
                }
                break;
            case Key.Right:
                if (!_playerViewModel.IsLiveContent)
                {
                    _playerViewModel.SkipForwardCommand.Execute(10);
                    e.Handled = true;
                }
                break;
            case Key.M:
                _playerViewModel.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                _playerViewModel.Volume = Math.Min(100, _playerViewModel.Volume + 5);
                _playerViewModel.UserInteractionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                _playerViewModel.Volume = Math.Max(0, _playerViewModel.Volume - 5);
                _playerViewModel.UserInteractionCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // === Player ===
    private void PlayerViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsFullScreen))
        {
            WindowState = _playerViewModel.IsFullScreen
                ? WindowState.FullScreen
                : WindowState.Normal;
        }

        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            UpdateMiniPlayerVisibility();
        }

        if (e.PropertyName == nameof(PlayerViewModel.IsNextEpisodePromptVisible))
        {
            MouseCaptureLayer.IsHitTestVisible = !_playerViewModel.IsNextEpisodePromptVisible;
        }
    }

    // === Navigation ===
    private void NavigateHome_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Home);
    private void NavigateLive_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Live);
    private void NavigateMovies_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Movies);
    private void NavigateSeries_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Series);
    private void NavigateSearch_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Search);
    private void NavigateMyList_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.MyList);
    private void NavigateFavorites_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Favorites);
    private void NavigateHistory_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.History);

    public void OpenProfileSelection()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var profilesWindow = ((App)Application.Current!).Services.GetRequiredService<Views.ProfilesWindow>();
        profilesWindow.DisableAutoSelect = true;
        desktop.MainWindow = profilesWindow;
        profilesWindow.Show();
        Hide();
    }

    private void SwitchProfile_Click(object? sender, RoutedEventArgs e)
    {
        OpenProfileSelection();
    }

    private void HideMiniPlayer()
    {
        MiniPlayer.IsVisible = false;
        MiniVideoSurface.MediaPlayer = null;
    }

    private void UpdateMiniPlayerVisibility()
    {
        if (PlayerArea.IsVisible || !_videoPlayerService.IsPlaying)
        {
            HideMiniPlayer();
            return;
        }

        MiniVideoSurface.MediaPlayer = _videoPlayerService.GetMediaPlayer();
        MiniPlayer.IsVisible = true;
    }

    private void MouseCaptureLayer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!PlayerArea.IsVisible)
        {
            return;
        }

        var pointerPoint = e.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _playerViewModel.ToggleFullScreenCommand.Execute(null);
            return;
        }

        _playerViewModel.PlayPauseCommand.Execute(null);
        _playerViewModel.UserInteractionCommand.Execute(null);
    }

    private void MouseCaptureLayer_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!PlayerArea.IsVisible)
        {
            return;
        }

        _playerViewModel.UserInteractionCommand.Execute(null);
    }

    private void CloseMiniPlayer_Click(object? sender, RoutedEventArgs e)
    {
        HideMiniPlayer();
        if (_videoPlayerService.IsPlaying)
        {
            _playerViewModel.StopCommand.Execute(null);
        }
    }

    private void HomeView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var verticalOffset = scrollViewer.Offset.Y;
        HeroGrid.Opacity = Math.Max(0.2, 1.0 - (verticalOffset / 800.0));

        if (HeroGrid.RenderTransform is not TransformGroup transforms)
        {
            return;
        }

        if (transforms.Children.Count > 0 && transforms.Children[0] is TranslateTransform parallax)
        {
            parallax.Y = verticalOffset * 0.3;
        }

        if (transforms.Children.Count > 1 && transforms.Children[1] is ScaleTransform zoom)
        {
            var zoomFactor = Math.Clamp(1.0 + (verticalOffset / 2500.0), 1.0, 1.16);
            zoom.ScaleX = zoomFactor;
            zoom.ScaleY = zoomFactor;
        }
    }

    private async void LiveView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        await LoadMoreChannelsFromScrollAsync(sender as ScrollViewer);
    }

    private async void MoviesView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        await LoadMoreChannelsFromScrollAsync(sender as ScrollViewer);
    }

    private async void SeriesView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var scrollableHeight = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        await _mainViewModel.LoadMoreSeriesIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
    }

    private async Task LoadMoreChannelsFromScrollAsync(ScrollViewer? scrollViewer)
    {
        if (scrollViewer == null)
        {
            return;
        }

        var scrollableHeight = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        await _mainViewModel.LoadMoreChannelsIfNeededAsync(scrollViewer.Offset.Y, scrollableHeight);
    }
}
