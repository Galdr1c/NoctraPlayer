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
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.Core.Services;
using Noctra.ViewModels;
using Noctra.Avalonia.Services;
using Noctra.Avalonia.Localization;
using System;

namespace Noctra.Avalonia;

public partial class MainWindow : Window
{
    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IWatchHistoryService _watchHistoryService;
    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private readonly PlayerViewModel _playerViewModel;
    private readonly WindowResizeService _windowResizeService;
    private CancellationTokenSource? _imageWarmupCts;

    public MainWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<MainViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<PlayerViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<IVideoPlayerService>(),
            ((App)Application.Current!).Services.GetRequiredService<IWatchHistoryService>(),
            ((App)Application.Current!).Services.GetRequiredService<ISettingsService>())
    {
    }

    public MainWindow(
        MainViewModel mainViewModel, 
        PlayerViewModel playerViewModel, 
        IVideoPlayerService videoPlayerService,
        IWatchHistoryService watchHistoryService,
        ISettingsService settingsService)
    {
        InitializeComponent();

        _mainViewModel = mainViewModel;
        _playerViewModel = playerViewModel;
        _videoPlayerService = videoPlayerService;
        _watchHistoryService = watchHistoryService;
        _settingsService = settingsService;
        _windowResizeService = new WindowResizeService(this);
        DataContext = _mainViewModel;

        PlayerOverlayLayer.DataContext = _playerViewModel;
        OverlayControl.DataContext = _playerViewModel;
        NextEpisodePrompt.DataContext = _playerViewModel;
        PiPCentralControls.DataContext = _playerViewModel;
        PiPBottomControls.DataContext = _playerViewModel;
        VideoSurface.MediaPlayer = _videoPlayerService.GetMediaPlayer();
        _videoPlayerService.MediaPlayerReady += VideoPlayerService_MediaPlayerReady;
        // MiniVideoSurface.MediaPlayer = null;

        AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
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

        UpdateDownloadBadgeVisibility();
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
        _mainViewModel.OnMediaSelected -= MainViewModel_OnMediaSelected;
        _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
        _mainViewModel.RequestEditChannel -= MainViewModel_RequestEditChannel;
        _playerViewModel.PropertyChanged -= PlayerViewModel_PropertyChanged;
        _playerViewModel.CloseRequested -= PlayerViewModel_CloseRequested;
        _playerViewModel.EpisodeRequested -= PlayerViewModel_EpisodeRequested;
        _playerViewModel.EpisodeProgressUpdated -= PlayerViewModel_EpisodeProgressUpdated;
        _playerViewModel.NextEpisodeRequested -= PlayerViewModel_NextEpisodeRequested;
        _playerViewModel.NextLiveChannelRequested -= PlayerViewModel_NextLiveChannelRequested;
        _playerViewModel.PreviousLiveChannelRequested -= PlayerViewModel_PreviousLiveChannelRequested;
        _playerViewModel.PiPRequested -= PlayerViewModel_PiPRequested;
        _videoPlayerService.MediaPlayerReady -= VideoPlayerService_MediaPlayerReady;

        // VideoSurface.MediaPlayer = null; // Handled in ClosePiP or let it be cleared
        ClosePiP(false); 
        VideoSurface.MediaPlayer = null;
        // _pipWindow?.ClosePiP(); // Removed old method call

        _imageWarmupCts?.Cancel();
        _imageWarmupCts?.Dispose();
        _imageWarmupCts = null;
    }

    private void VideoPlayerService_MediaPlayerReady(object? sender, LibVLCSharp.Shared.MediaPlayer? mp)
    {
        VideoSurface.MediaPlayer = mp;
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
        {
            return;
        }

        try
        {
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

            // HideMiniPlayer();
            PlayerArea.IsVisible = true;
            _playerViewModel.IsLocked = false;
            
            // Wait for native control to be created by Avalonia before playing.
            // This prevents LibVLC from falling back to pop-up Direct3D windows.
            var timeoutTask = Task.Delay(1000);
            var readyTask = VideoSurface.WaitForHandleReadyAsync();
            await Task.WhenAny(readyTask, timeoutTask);
            
            _playerViewModel.UserInteractionCommand.Execute(null);

            double? finalStartPos = null;

            // --- RESUME DIALOG KONTROLÜ ---
            var resumePosition = ResolveResumePosition(channel);
            if (resumePosition > 120)
            {
                bool shouldResume;
                try
                {
                    shouldResume = await _playerViewModel.ShowResumeDialogAsync(resumePosition);
                }
                catch (OperationCanceledException)
                {
                    // Kullanıcı dialog'u kapatmadan kanal değiştirdi
                    return;
                }

                if (shouldResume)
                {
                    finalStartPos = resumePosition;
                    _playerViewModel.SetResumePosition(resumePosition);
                }
            }
            // --- RESUME DIALOG KONTROLÜ SONU ---

            await _playerViewModel.PlayChannelAsync(channel, finalStartPos);
            Dispatcher.UIThread.Post(() => OverlayControl.Focus(), DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Media playback failed in MainWindow_OnMediaSelected.", ex);
            PlayerArea.IsVisible = false;
            _mainViewModel.StatusMessage = UserFriendlyErrorMessage.WithPrefix(LocalizationSource.Instance["Player.Error.PlaybackFailed"], ex);
        }
    }

    private double ResolveResumePosition(Channel channel)
    {
        // Episode oynatılıyorsa episode'un progress'i
        var episode = _mainViewModel.CurrentEpisodePlaybackContext;
        if (episode != null &&
            episode.WatchedPosition.HasValue &&
            episode.WatchedPosition.Value.TotalSeconds > 120 &&
            !episode.IsCompleted)
        {
            return episode.WatchedPosition.Value.TotalSeconds;
        }

        // Eğer Dizi içeriği açılıyorsa fakat CurrentEpisodePlaybackContext boş/yeniyse,
        // (örneğin Home sayfasındaki Continue Watching bölümünden BuildSeriesEpisodeChannel ile üretilmiş sanal kanal)
        if (channel.Type == ChannelType.Series && 
            channel.WatchedPosition.HasValue && 
            channel.WatchedPosition.Value.TotalSeconds > 120 &&
            !channel.IsCompleted)
        {
            return channel.WatchedPosition.Value.TotalSeconds;
        }

        // VOD için channel'ın progress'i
        if (channel.Type == ChannelType.VOD &&
            channel.WatchedPosition.HasValue &&
            channel.WatchedPosition.Value.TotalSeconds > 120 &&
            !channel.IsCompleted)
        {
            return channel.WatchedPosition.Value.TotalSeconds;
        }

        return 0;
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

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.FilteredChannels) or
            nameof(MainViewModel.SeriesViewItems))
        {
            ScheduleImageWarmup();
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveDownloadCount))
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
                await Task.Delay(50, token).ConfigureAwait(false);
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


        urls.AddRange(_mainViewModel.FilteredChannels.Take(120).Select(c => c.CoverUrl ?? c.LogoUrl));
        urls.AddRange(_mainViewModel.SeriesViewItems.Take(60).Select(s => s.CoverUrl));

        await RemoteImage.PreloadAsync(urls, maxCount: 220, cancellationToken).ConfigureAwait(false);
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
    private SystemDecorations _savedSystemDecorations;

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
        _savedSystemDecorations = SystemDecorations;

        // 2. Normal moda geç ve dekorasyonları kaldır
        WindowState = WindowState.Normal;
        SystemDecorations = SystemDecorations.None;

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
        SystemDecorations = _savedSystemDecorations;
        
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
