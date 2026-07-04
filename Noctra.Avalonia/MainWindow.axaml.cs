using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.Core.Services;
using Noctra.ViewModels;
using Noctra.Avalonia.Services;
using Noctra.Avalonia.Localization;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Avalonia;

public partial class MainWindow : Window
{
    private static readonly TimeSpan PointerInteractionThrottle = TimeSpan.FromMilliseconds(100);

    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IWatchHistoryService _watchHistoryService;
    private readonly ISettingsService _settingsService;
    private readonly IReviewPromptService _reviewPromptService;
    private readonly MainViewModel _mainViewModel;
    private readonly PlayerViewModel _playerViewModel;
    private readonly WindowResizeService _windowResizeService;
    private CancellationTokenSource _mediaSelectionCts = new();
    private readonly CancellationTokenSource _reviewPromptCts = new();
    private DateTime _lastPointerInteractionUtc = DateTime.MinValue;

    internal bool IsVideoPlaybackSurfaceVisible =>
        PlayerArea.IsVisible ||
        _playerViewModel.IsPlaying ||
        _playerViewModel.IsPiPMode;

    internal bool IsReviewPromptAllowedSurface =>
        IsVisible &&
        HeaderBar.IsVisible &&
        MainContentArea.IsVisible &&
        StatusBar.IsVisible &&
        !_mainViewModel.IsGlobalLoading &&
        !_mainViewModel.IsSeriesDetailVisible &&
        !IsVideoPlaybackSurfaceVisible &&
        _mainViewModel.ActiveView is AppView.Home or
            AppView.Live or
            AppView.Movies or
            AppView.Series or
            AppView.Search or
            AppView.MyList or
            AppView.Favorites or
            AppView.History or
            AppView.Downloads;

    public MainWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<MainViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<PlayerViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<IVideoPlayerService>(),
            ((App)Application.Current!).Services.GetRequiredService<IWatchHistoryService>(),
            ((App)Application.Current!).Services.GetRequiredService<ISettingsService>(),
            ((App)Application.Current!).Services.GetRequiredService<IReviewPromptService>())
    {
    }

    public MainWindow(
        MainViewModel mainViewModel, 
        PlayerViewModel playerViewModel, 
        IVideoPlayerService videoPlayerService,
        IWatchHistoryService watchHistoryService,
        ISettingsService settingsService,
        IReviewPromptService reviewPromptService)
    {
        InitializeComponent();

        _mainViewModel = mainViewModel;
        _playerViewModel = playerViewModel;
        _videoPlayerService = videoPlayerService;
        _watchHistoryService = watchHistoryService;
        _settingsService = settingsService;
        _reviewPromptService = reviewPromptService;
        _windowResizeService = new WindowResizeService(this);
        DataContext = _mainViewModel;

        PlayerOverlayLayer.DataContext = _playerViewModel;
        OverlayControl.DataContext = _playerViewModel;
        OverlayControl.EpgChannelSelected += MainWindow_EpgChannelSelected;
        ResumeDialog.DataContext = _playerViewModel;
        NextEpisodePrompt.DataContext = _playerViewModel;
        PiPCentralControls.DataContext = _playerViewModel;
        PiPBottomControls.DataContext = _playerViewModel;
        PiPWatermark.DataContext = _mainViewModel.WatermarkViewModel;
        VideoSurface.MediaPlayer = GetDesktopMediaPlayer();
        _videoPlayerService.PlayerReady += VideoPlayerService_PlayerReady;
        // MiniVideoSurface.MediaPlayer = null;

        AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += MainWindow_Opened;
        PositionChanged += MainWindow_PositionChanged;
        Closed += OnClosed;
        _mainViewModel.OnMediaSelected += MainViewModel_OnMediaSelected;
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        _mainViewModel.RequestEditChannel += MainViewModel_RequestEditChannel;
        _playerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;
        _playerViewModel.CloseRequested += PlayerViewModel_CloseRequested;
        _playerViewModel.EpisodeRequested += PlayerViewModel_EpisodeRequested;
        _playerViewModel.EpisodeProgressUpdated += PlayerViewModel_EpisodeProgressUpdated;
        _playerViewModel.NextEpisodeRequested += PlayerViewModel_NextEpisodeRequested;
        _playerViewModel.NextLiveChannelRequested += PlayerViewModel_NextLiveChannelRequested;
        _playerViewModel.PreviousLiveChannelRequested += PlayerViewModel_PreviousLiveChannelRequested;
        _playerViewModel.PiPRequested += PlayerViewModel_PiPRequested;
        
        _playerViewModel.PremiumUpsellRequested += async (_, _) =>
        {
            var dialogService = ((App)Application.Current!).Services.GetRequiredService<IDialogService>();
            await dialogService.ShowUpsellAsync();
        };

        var playlistService = ((App)Application.Current!).Services.GetRequiredService<IPlaylistService>();
        _playerViewModel.LiveChannelsLoader = async () =>
        {
            var playlistId =
                _playerViewModel.CurrentChannel?.PlaylistId > 0 ? _playerViewModel.CurrentChannel.PlaylistId :
                _mainViewModel.SelectedChannel?.PlaylistId > 0 ? _mainViewModel.SelectedChannel.PlaylistId :
                _mainViewModel.SelectedPlaylist?.Id ?? 0;

            if (playlistId <= 0)
                return new List<Channel>();

            var activeGroup = !string.IsNullOrWhiteSpace(_mainViewModel.SelectedGroup)
                ? _mainViewModel.SelectedGroup
                : _playerViewModel.CurrentChannel?.GroupTitle;

            return await playlistService.GetChannelsFilteredAsync(
                playlistId,
                group: activeGroup,
                type: ChannelType.Live,
                limit: 500,
                sortOrder: ChannelSortOrder.NameAsc);
        };

        UpdateDownloadBadgeVisibility();
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        _ = _reviewPromptService.TryShowMainWindowPromptAsync(_reviewPromptCts.Token);
    }

    private void MainWindow_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        // Pencere hareket ettiğinde (sürükleme dahil) PiP kontrollerini yenile
        if (_playerViewModel?.IsPiPMode == true)
        {
            _playerViewModel.UserInteractionCommand.Execute(null);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _mainViewModel.CancelProfileBackgroundLoading();
        _reviewPromptCts.Cancel();
        _reviewPromptCts.Dispose();

        // Flush watch position before closing
        try
        {
            _playerViewModel.FlushWatchHistoryAsync(force: true).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Failed to flush watch history on window close", ex);
        }

        _mainViewModel.OnMediaSelected -= MainViewModel_OnMediaSelected;
        _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
        _mainViewModel.RequestEditChannel -= MainViewModel_RequestEditChannel;
        Opened -= MainWindow_Opened;
        OverlayControl.EpgChannelSelected -= MainWindow_EpgChannelSelected;
        _playerViewModel.PropertyChanged -= PlayerViewModel_PropertyChanged;
        _playerViewModel.CloseRequested -= PlayerViewModel_CloseRequested;
        _playerViewModel.EpisodeRequested -= PlayerViewModel_EpisodeRequested;
        _playerViewModel.EpisodeProgressUpdated -= PlayerViewModel_EpisodeProgressUpdated;
        _playerViewModel.NextEpisodeRequested -= PlayerViewModel_NextEpisodeRequested;
        _playerViewModel.NextLiveChannelRequested -= PlayerViewModel_NextLiveChannelRequested;
        _playerViewModel.PreviousLiveChannelRequested -= PlayerViewModel_PreviousLiveChannelRequested;
        _playerViewModel.PiPRequested -= PlayerViewModel_PiPRequested;
        _videoPlayerService.PlayerReady -= VideoPlayerService_PlayerReady;

        // VideoSurface.MediaPlayer = null; // Handled in ClosePiP or let it be cleared
        ClosePiP(false); 
        VideoSurface.MediaPlayer = null;
        // _pipWindow?.ClosePiP(); // Removed old method call

    }

    private void VideoPlayerService_PlayerReady(object? sender, EventArgs e)
    {
        VideoSurface.MediaPlayer = GetDesktopMediaPlayer();
    }

    private LibVLCSharp.Shared.MediaPlayer? GetDesktopMediaPlayer() =>
        (_videoPlayerService as VideoPlayerService)?.GetDesktopMediaPlayer();

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
        try
        {
            var settingsWindow = ((App)Application.Current!).Services.GetRequiredService<Views.SettingsWindow>();
            await settingsWindow.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _mainViewModel.StatusMessage = $"{LocalizationSource.Instance["Settings.Error.OpenFailed"]}: {ex.Message}";
            StartupDiagnostics.LogException("Failed to open SettingsWindow.", ex);
        }
    }


    // === Search Overlay Keyboard ===
    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _mainViewModel.CloseSeriesDetailCommand.Execute(null);
            _mainViewModel.CommitSearchCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            _mainViewModel.SearchQuery = string.Empty;
        }
    }

    // === Media Selection ===
    private async void MainViewModel_OnMediaSelected(object? media)
    {
        if (media is not Channel channel)
            return;

        // ── 1. Anında preempt: version artır, eski dialog'u kapat ───────────
        // Bu satır, hâlâ devam eden PlayChannelAsync akışlarının URL çözümleme
        // sonrasındaki guard'da durmasını sağlar.
        // PreemptCurrentPlayback() CancelResumeDialog()'u da çağırır.
        _playerViewModel.PreemptCurrentPlayback();

        // ── 2. Bu handler instance'ını iptal edilebilir yap ─────────────────
        // Yeni kanal seçildiğinde önceki handler VideoSurface veya
        // resume dialog await'inde durur.
        var cts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _mediaSelectionCts, cts);
        try
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to replace media selection token: {ex.Message}");
        }
        var token = cts.Token;

        try
        {
            _playerViewModel.CurrentProfileId = _mainViewModel.CurrentProfileId;
            if (channel.Type == ChannelType.Series && _mainViewModel.CurrentEpisodePlaybackContext == null)
                _mainViewModel.TryPrepareEpisodePlaybackContext(channel);

            if (channel.Type == ChannelType.Series && _mainViewModel.CurrentEpisodePlaybackContext != null)
                _playerViewModel.SetCurrentEpisode(
                    _mainViewModel.CurrentEpisodePlaybackContext,
                    _mainViewModel.NextEpisodePlaybackContext,
                    _mainViewModel.CurrentSeriesPlaybackContext);
            else
                _playerViewModel.SetCurrentEpisode(null, null);

            PlayerArea.IsVisible = true;
            _playerViewModel.IsLocked = false;

            // Native handle hazır olana kadar bekle (iptal edilebilir).
            try
            {
                await Task.WhenAny(
                    VideoSurface.WaitForHandleReadyAsync(),
                    Task.Delay(1000, token));
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { return; }

            _playerViewModel.UserInteractionCommand.Execute(null);

            double? finalStartPos = null;

            // --- RESUME DIALOG ---
            var resumePosition = await ResolveResumePositionAsync(channel, token);
            if (resumePosition > 120)
            {
                bool shouldResume;
                try
                {
                    shouldResume = await _playerViewModel.ShowResumeDialogAsync(resumePosition, token);
                }
                catch (OperationCanceledException) { return; }

                if (shouldResume)
                {
                    finalStartPos = resumePosition;
                    _playerViewModel.SetResumePosition(resumePosition);
                }
            }

            // Dialog kapandıktan sonra son token kontrolü.
            token.ThrowIfCancellationRequested();

            await _playerViewModel.PlayChannelAsync(channel, finalStartPos);
            Dispatcher.UIThread.Post(() => OverlayControl.Focus(), DispatcherPriority.Input);
        }
        catch (OperationCanceledException)
        {
            // Yeni kanal seçimi bu handler'ı durdurdu.
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Media playback failed in MainWindow_OnMediaSelected.", ex);
            PlayerArea.IsVisible = false;
            _mainViewModel.StatusMessage = UserFriendlyErrorMessage.WithPrefix(
                LocalizationSource.Instance["Player.Error.PlaybackFailed"], ex);
        }
    }

    private async Task<double> ResolveResumePositionAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (channel.Type == ChannelType.Live || !_mainViewModel.CurrentProfileId.HasValue)
        {
            return 0;
        }

        var profileId = _mainViewModel.CurrentProfileId.Value;

        // Only a real episode selection may use episode progress.
        // A stale CurrentEpisodePlaybackContext from a previously opened series must never
        // trigger a continue prompt for a different VOD/series item.
        if (channel.Type == ChannelType.Series)
        {
            var episode = _mainViewModel.CurrentEpisodePlaybackContext;
            if (episode != null &&
                !string.IsNullOrWhiteSpace(episode.StreamUrl) &&
                string.Equals(episode.StreamUrl, channel.StreamUrl, StringComparison.OrdinalIgnoreCase))
            {
                var episodeHistory = episode.Id > 0
                    ? await _watchHistoryService.GetLatestForMediaAsync(profileId, null, episode.Id, cancellationToken)
                    : null;

                var position = episodeHistory?.StoppedAt ?? episode.WatchedPosition ?? TimeSpan.Zero;
                var completed = episodeHistory?.Completed ?? episode.IsCompleted;
                var hasRealWatchSignal = episodeHistory != null || episode.LastWatched.HasValue;

                if (ShouldOfferResume(position, episode.Duration, completed, hasRealWatchSignal))
                {
                    return position.TotalSeconds;
                }
            }

            // Virtual series episode cards built for Continue Watching may carry their own
            // progress. Require a real watch signal so playlist refresh/user-data merges cannot
            // show continue prompts on content the user never opened.
            if (IsSameStream(channel.StreamUrl, _mainViewModel.CurrentEpisodePlaybackContext?.StreamUrl))
            {
                var channelHistory = channel.Id > 0
                    ? await _watchHistoryService.GetLatestForMediaAsync(profileId, channel.Id, null, cancellationToken)
                    : null;

                var position = channelHistory?.StoppedAt ?? channel.WatchedPosition ?? TimeSpan.Zero;
                var completed = channelHistory?.Completed ?? channel.IsCompleted;
                var hasRealWatchSignal = channelHistory != null || channel.LastWatched.HasValue;

                if (ShouldOfferResume(position, channel.Duration, completed, hasRealWatchSignal))
                {
                    return position.TotalSeconds;
                }
            }

            return 0;
        }

        if (channel.Type == ChannelType.VOD)
        {
            var history = channel.Id > 0
                ? await _watchHistoryService.GetLatestForMediaAsync(profileId, channel.Id, null, cancellationToken)
                : null;

            var position = history?.StoppedAt ?? channel.WatchedPosition ?? TimeSpan.Zero;
            var completed = history?.Completed ?? channel.IsCompleted;
            var hasRealWatchSignal = history != null || channel.LastWatched.HasValue;

            if (ShouldOfferResume(position, channel.Duration, completed, hasRealWatchSignal))
            {
                return position.TotalSeconds;
            }
        }

        return 0;
    }

    private static bool IsSameStream(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldOfferResume(TimeSpan position, TimeSpan? duration, bool completed, bool hasRealWatchSignal)
    {
        if (!hasRealWatchSignal || completed || position.TotalSeconds <= 120)
        {
            return false;
        }

        if (duration.HasValue && duration.Value.TotalSeconds > 0)
        {
            var durationSeconds = duration.Value.TotalSeconds;

            if (position.TotalSeconds >= durationSeconds - 30)
            {
                return false;
            }

            if (position.TotalSeconds / durationSeconds >= 0.95)
            {
                return false;
            }
        }

        return true;
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

    private void PlayerViewModel_NextLiveChannelRequested(object? sender, EventArgs e)
    {
        _mainViewModel.PlayNextLiveChannelCommand.Execute(null);
    }

    private void PlayerViewModel_PreviousLiveChannelRequested(object? sender, EventArgs e)
    {
        _mainViewModel.PlayPreviousLiveChannelCommand.Execute(null);
    }

    private void MainWindow_EpgChannelSelected(object? sender, Views.EpgChannelSelectedRoutedEventArgs e)
    {
        _mainViewModel.SelectChannelFromEpgCommand.Execute(e.Channel);
    }

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveDownloadCount))
        {
            UpdateDownloadBadgeVisibility();
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentProfileId))
        {
            var profileId = _mainViewModel.CurrentProfileId;
            var retentionDays = _settingsService.Settings.WatchHistoryRetentionDays;

            if (profileId.HasValue && retentionDays > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _watchHistoryService.CleanupOlderThanDaysAsync(profileId.Value, retentionDays);
                    }
                    catch (Exception ex)
                    {
                        StartupDiagnostics.LogException("Event-driven history cleanup failed.", ex);
                    }
                });
            }
        }
    }

    private void UpdateDownloadBadgeVisibility()
    {
        bool hasDownloads = _mainViewModel.ActiveDownloadCount > 0;
        
        if (_isSidebarOpen)
        {
            NavDownloadsBadgeMini.IsVisible = false;
            NavDownloadsBadgeFull.IsVisible = hasDownloads;
        }
        else
        {
            NavDownloadsBadgeFull.IsVisible = false;
            NavDownloadsBadgeMini.IsVisible = hasDownloads;
        }
    }

    private void PlayerViewModel_CloseRequested(object? sender, EventArgs e)
    {
        PlayerArea.IsVisible = false;
        _playerViewModel.IsLocked = false;
        ClosePiP(false);
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
                else if (_playerViewModel.IsPiPMode)
                {
                    ClosePiP(true);
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
                _playerViewModel.Volume = Math.Min(100, _playerViewModel.Volume + 2);
                _playerViewModel.UserInteractionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                _playerViewModel.Volume = Math.Max(0, _playerViewModel.Volume - 2);
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
            // UpdateMiniPlayerVisibility(); // Removed
        }

        if (e.PropertyName == nameof(PlayerViewModel.IsNextEpisodePromptVisible))
        {
            MouseCaptureLayer.IsHitTestVisible = !_playerViewModel.IsNextEpisodePromptVisible;
        }
    }

    // === Navigation ===
    private bool _isSidebarOpen = false;

    private void HamburgerBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_isSidebarOpen)
            CloseSidebar();
        else
            OpenSidebar();
    }

    private void SidebarDismissOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseSidebar();
    }

    private void OpenSidebar()
    {
        _isSidebarOpen = true;
        SideBar.Width = 280;
        SidebarDismissOverlay.IsVisible = true;
        SetNavTextVisibility(true);
    }

    private void CloseSidebar()
    {
        _isSidebarOpen = false;
        SideBar.Width = 60;
        SidebarDismissOverlay.IsVisible = false;
        SetNavTextVisibility(false);
    }

    private void SetNavTextVisibility(bool visible)
    {
        NavLabel1.IsVisible = visible;
        NavLabel2.IsVisible = visible;
        NavHomeText.IsVisible = visible;
        NavLiveText.IsVisible = visible;
        NavMoviesText.IsVisible = visible;
        NavSeriesText.IsVisible = visible;
        NavMyListText.IsVisible = visible;
        NavFavText.IsVisible = visible;
        NavHistoryText.IsVisible = visible;
        NavDownloadsText.IsVisible = visible;

        // Hamburger ikonunu duruma göre değiştir
        HamburgerIcon.Kind = visible ? Material.Icons.MaterialIconKind.MenuOpen : Material.Icons.MaterialIconKind.Menu;

        // Navigasyon tooltiplerini menü kapalıyken göster, açıkken gizle
        NavHomeBtn.SetValue(ToolTip.TipProperty, visible ? null : LocalizationSource.Instance["Shell.Nav.Home"]);
        NavLiveBtn.SetValue(ToolTip.TipProperty, visible ? null : LocalizationSource.Instance["Shell.Nav.Live"]);
        NavMoviesBtn.SetValue(ToolTip.TipProperty, visible ? null : LocalizationSource.Instance["Shell.Nav.Movies"]);
        NavSeriesBtn.SetValue(ToolTip.TipProperty, visible ? null : LocalizationSource.Instance["Shell.Nav.Series"]);
        NavMyListBtn.SetValue(ToolTip.TipProperty, visible ? null : LocalizationSource.Instance["Shell.Nav.MyList"]);
        NavFavBtn.SetValue(ToolTip.TipProperty, visible ? null : LocalizationSource.Instance["Shell.Nav.Favorites"]);
        NavHistoryBtn.SetValue(ToolTip.TipProperty, visible ? null : LocalizationSource.Instance["Shell.Nav.History"]);
        NavDownloadsBtn.SetValue(ToolTip.TipProperty, visible ? null : LocalizationSource.Instance["Shell.Nav.Downloads"]);

        UpdateDownloadBadgeVisibility();
    }

    private void NavigateHome_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.Home); }
    private void NavigateLive_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.Live); }
    private void NavigateMovies_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.Movies); }
    private void NavigateSeries_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.Series); }
    private void NavigateSearch_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.Search); }
    private void NavigateMyList_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.MyList); }
    private void NavigateFavorites_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.Favorites); }
    private void NavigateHistory_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.History); }
    private void NavigateDownloads_Click(object? sender, RoutedEventArgs e) { CloseSidebar(); _mainViewModel.CloseSeriesDetailCommand.Execute(null); _mainViewModel.NavigateCommand.Execute(AppView.Downloads); }

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

    private WindowState _savedWindowState;
    private Size _savedWindowSize;
    private PixelPoint _savedWindowPosition;
    private WindowDecorations _savedWindowDecorations;

    private void PlayerViewModel_PiPRequested(object? sender, EventArgs e)
    {
        OpenPiP();
    }

    public void OpenPiP()
    {
        if (_playerViewModel.IsPiPMode) return;
        if (!_videoPlayerService.IsPlaying) return;

        _playerViewModel.IsPiPMode = true;

        // 1. Mevcut durumu kaydet
        _savedWindowState = WindowState;
        _savedWindowSize = new Size(Width, Height);
        _savedWindowPosition = Position;
        _savedWindowDecorations = WindowDecorations;

        // 2. Normal moda geç ve dekorasyonları kaldır
        WindowState = WindowState.Normal;
        WindowDecorations = WindowDecorations.None;

        // 3. UI bileşenlerini gizle, sadece PlayerArea kalsın
        HeaderBar.IsVisible = false;
        MainContentArea.IsVisible = false;
        StatusBar.IsVisible = false;
        PiPWatermark.IsVisible = false; // PiP modunda watermark ekranı kapatıyor
        MouseCaptureLayer.IsHitTestVisible = true; // PiP modunda tıklayınca sürüklemeyi sağlayacağız
        Grid.SetRowSpan(PlayerArea, 3); // Ensure it covers everything regardless of hidden rows

        // 4. Küçük PiP boyutuna geç
        const double pipWidth = 400;
        const double pipHeight = 225;

        // MinWidth/Height kısıtlamalarını geçici olarak esnet
        MinWidth = 0;
        MinHeight = 0;

        Width = pipWidth;
        Height = pipHeight;

        // 5. Ekranın sağ alt köşesine taşı
        var screen = Screens.ScreenFromVisual(this) ?? Screens.Primary;
        if (screen != null)
        {
            var wa = screen.WorkingArea;
            Position = new PixelPoint(
                (int)(wa.X + wa.Width - pipWidth - 24),
                (int)(wa.Y + wa.Height - pipHeight - 24)
            );
        }

        // 6. PiP Kontrollerini ve Çerçeveyi göster
        PiPContainer.CornerRadius = new CornerRadius(0);
        PlayerOverlayLayer.IsVisible = false; // Tüm overlay katmanını gizle (pip'te sadece pip kontrolleri)

        // 7. En üstte tut
        Topmost = true;
        
        // WindowState'in Normal olduğundan emin ol (bazı platformlarda MinWidth/Height sonrası bozulabiliyor)
        WindowState = WindowState.Normal;
    }

    private void ClosePiP(bool returnToMain)
    {
        if (!_playerViewModel.IsPiPMode) return;
        _playerViewModel.IsPiPMode = false;

        // 1. Önce pencere boyutlarını ve dekorasyonları geri al (Layout için kritik)
        Topmost = false;
        WindowDecorations = _savedWindowDecorations;
        
        MinWidth = 1000; // Orijinal min değerler
        MinHeight = 600;
        
        Width = _savedWindowSize.Width;
        Height = _savedWindowSize.Height;
        Position = _savedWindowPosition;
        WindowState = _savedWindowState;

        // 2. RowSpan'i sıfırla ki tüm alanı kaplamaya devam etsin (Orijinal değer 3)
        Grid.SetRowSpan(PlayerArea, 3);

        // 3. Görünürlüğü GÜVENLİ bir şekilde geri al (Layout bozulmasını önlemek için gecikmeli)
        PiPContainer.CornerRadius = new CornerRadius(0);
        
        // Dispatcher ile bir sonraki frame'e atarsak pencere boyutları tam oturmuş olur
        Dispatcher.UIThread.Post(() => {
            HeaderBar.IsVisible = true;
            MainContentArea.IsVisible = true;
            StatusBar.IsVisible = true;
            PiPWatermark.IsVisible = true;
            MouseCaptureLayer.IsHitTestVisible = true;
            PlayerOverlayLayer.IsVisible = true;
            
            _playerViewModel.UserInteractionCommand.Execute(null); // Timer'ı 2.5s sıfırla
            // Layout geçişini zorla tazele
            InvalidateVisual();
        }, DispatcherPriority.Background);

        // 4. Eğer kapatıldıysa (return değilse) player'ı da durdur
        if (!returnToMain)
        {
            _playerViewModel.ClosePlayerCommand.Execute(null);
        }
    }

    private void PlayPreviousLiveChannelPiP_Click(object? sender, RoutedEventArgs e)
    {
        if (_playerViewModel != null && _playerViewModel.IsLiveContent)
        {
            _playerViewModel.PlayPreviousLiveChannelCommand.Execute(null);
            _playerViewModel.UserInteractionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PlayNextLiveChannelPiP_Click(object? sender, RoutedEventArgs e)
    {
        if (_playerViewModel != null && _playerViewModel.IsLiveContent)
        {
            _playerViewModel.PlayNextLiveChannelCommand.Execute(null);
            _playerViewModel.UserInteractionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PiPReturn_Click(object? sender, RoutedEventArgs e) => ClosePiP(returnToMain: true);
    
    private void PiPClose_Click(object? sender, RoutedEventArgs e) => ClosePiP(returnToMain: false);

    private void PiPCornerResize_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_playerViewModel?.IsPiPMode != true) return;
        if (_playerViewModel != null)
        {
            _playerViewModel.IsResizing = true;
            _playerViewModel.UserInteractionCommand.Execute(null);
        }
        _windowResizeService.BeginResize(sender, e);
    }

    private void PiPCornerResize_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_playerViewModel?.IsPiPMode != true) return;
        if (_playerViewModel != null)
            _playerViewModel.UserInteractionCommand.Execute(null);
        _windowResizeService.UpdateResize(e);
    }

    private void PiPCornerResize_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_playerViewModel?.IsPiPMode != true) return;
        if (_playerViewModel != null)
        {
            _playerViewModel.IsResizing = false;
            _playerViewModel.UserInteractionCommand.Execute(null);
        }
        _windowResizeService.EndResize(e);
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

        // PiP modunda tüm yüzeyden sürükleme yap
        if (_playerViewModel?.IsPiPMode == true)
        {
            Activate();
            Focus();
            _playerViewModel.UserInteractionCommand.Execute(null);
            
            if (e.ClickCount >= 2)
            {
                ClosePiP(true);
            }
            else if (WindowState == WindowState.Normal)
            {
                if (_playerViewModel != null) _playerViewModel.IsDragging = true;
                BeginMoveDrag(e);
                if (_playerViewModel != null) _playerViewModel.IsDragging = false;
            }
            return;
        }

        if (e.ClickCount >= 2)
        {
            _playerViewModel.ToggleFullScreenCommand?.Execute(null);
            e.Handled = true;
            return;
        }

        _playerViewModel.PlayPauseCommand?.Execute(null);
        _playerViewModel.UserInteractionCommand?.Execute(null);
        e.Handled = true;
    }

    private void MouseCaptureLayer_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!PlayerArea.IsVisible)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastPointerInteractionUtc < PointerInteractionThrottle)
        {
            return;
        }

        _lastPointerInteractionUtc = now;
        _playerViewModel.UserInteractionCommand.Execute(null);
    }

    private void MouseCaptureLayer_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!PlayerArea.IsVisible)
        {
            return;
        }

        _playerViewModel.UserInteractionCommand.Execute(null);
    }




}
