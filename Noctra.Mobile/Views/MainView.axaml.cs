using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;

using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.Mobile.Localization;
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
    private IPlayerWindowService? _playerWindowService;
    private MobileBackNavigationService? _backNavigationService;
    private bool _isPlayerFullScreen;
    private string _currentDestination = "Home";
    private readonly Thickness _headerBasePadding;
    private readonly Thickness _bottomNavBasePadding;

    public MainView()
    {
        InitializeComponent();
        MobileSettingsContent.BackToProfilesRequested += (_, _) => ShowProfileSelection();
        SizeChanged += OnSizeChanged;

        // Safe-area hesaplaması için temel (tasarım) padding değerlerini sakla.
        _headerBasePadding = HeaderBar.Padding;
        _bottomNavBasePadding = BottomNavigation.Padding;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Android donanım/jest geri tuşunu bu view'e bağla.
        RegisterBackHandler();

        // Çentik / sistem çubukları (safe-area) padding'lerini uygula ve değişimleri dinle.
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.InsetsManager is { } insets)
        {
            insets.SafeAreaChanged -= OnSafeAreaChanged;
            insets.SafeAreaChanged += OnSafeAreaChanged;
            ApplySafeArea(insets.SafeAreaPadding);
        }

        // Show legal consent on first launch
        _ = ShowLegalConsentIfNeededAsync();

        // Try showing review prompt after a delay (same logic as desktop)
        _ = TryShowReviewPromptAsync();
    }

    private async Task ShowLegalConsentIfNeededAsync()
    {
        // Small delay to let the UI settle
        await Task.Delay(500);
        await LegalConsentOverlay.ShowConsentFlowAsync();
    }

    private async Task TryShowReviewPromptAsync()
    {
        try
        {
            if (Application.Current is not App { Services: not null } app)
            {
                return;
            }

            var reviewService = app.Services.GetService<IReviewPromptService>();
            if (reviewService is not null)
            {
                // Surface state check — mirrors desktop's IsReviewPromptAllowedSurface.
                // Checked here (before the 3-min delay) so we skip early if not ready.
                if (!IsReviewSurfaceReady())
                    return;

                await reviewService.TryShowMainWindowPromptAsync();
            }
        }
        catch
        {
            // Non-critical: ignore review prompt errors silently.
        }
    }

    /// <summary>
    /// Checks if the current UI surface is suitable for showing a review prompt.
    /// Mirrors desktop's IsReviewPromptAllowedSurface logic.
    /// </summary>
    private bool IsReviewSurfaceReady()
    {
        // Legal consent overlay is showing
        if (LegalConsentOverlay.IsVisible)
            return false;

        // Player is visible (playing, fullscreen, PiP, etc.)
        if (PlayerHost.IsVisible)
            return false;

        // Core content is not loaded yet
        if (!CoreContentHost.IsVisible)
            return false;

        // Main content area should be visible (not in a sub-page like profile list)
        if (!HeaderBar.IsVisible)
            return false;

        return true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.InsetsManager is { } insets)
        {
            insets.SafeAreaChanged -= OnSafeAreaChanged;
        }

        if (_backNavigationService is not null)
        {
            _backNavigationService.BackRequested = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void RegisterBackHandler()
    {
        _backNavigationService ??= GetPlatformServiceResolver()?.GetBackNavigationService();
        if (_backNavigationService is not null)
        {
            _backNavigationService.BackRequested = TryHandleBack;
        }
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e)
        => ApplySafeArea(e.SafeAreaPadding);

    /// <summary>
    /// Çentik / gesture bar ile çakışmayı önlemek için header üst + yanlar,
    /// alt navigasyon alt + yanlar safe-area kadar genişletilir.
    /// </summary>
    private void ApplySafeArea(Thickness safe)
    {
        HeaderBar.Padding = new Thickness(
            _headerBasePadding.Left + safe.Left,
            _headerBasePadding.Top + safe.Top,
            _headerBasePadding.Right + safe.Right,
            _headerBasePadding.Bottom);

        BottomNavigation.Padding = new Thickness(
            _bottomNavBasePadding.Left + safe.Left,
            _bottomNavBasePadding.Top,
            _bottomNavBasePadding.Right + safe.Right,
            _bottomNavBasePadding.Bottom + safe.Bottom);
    }

    /// <summary>
    /// Geri tuşu önceliği: tam ekran → EPG paneli → oynatıcıyı kapat → alt sayfadan ana sayfaya.
    /// Hiçbiri uygulanmıyorsa false döner (uygulamadan çıkış).
    /// </summary>
    internal bool TryHandleBack()
    {
        // 0) Yasal onay ekranı açıksa, geri tuşunu tüket (kullanıcı onay vermeden devam edemez)
        if (LegalConsentOverlay.IsVisible)
        {
            return true;
        }

        // 1) Oynatıcı tam ekrandaysa -> tam ekrandan çık
        if (PlayerHost.IsVisible && _playerViewModel is { IsFullScreen: true })
        {
            _playerViewModel.IsFullScreen = false;
            return true;
        }

        // 2) Oynatıcıda bir alt panel açıksa -> önce paneli kapat.
        // Mobil UX'te Android geri hareketi doğrudan player'ı kapatmamalı;
        // ses/altyazı, kalite, bilgi, bölüm, zamanlayıcı veya EPG paneli önce kapanır.
        if (PlayerHost.IsVisible && IsAnyPlayerPanelOpen())
        {
            _playerViewModel?.ClosePanelsCommand.Execute(null);
            return true;
        }

        // 3) Oynatıcı görünürse -> oynatıcıyı kapat
        if (PlayerHost.IsVisible)
        {
            _playerViewModel?.ClosePlayerCommand.Execute(null);
            return true;
        }

        // 4) Alt sayfadaysak -> Ana sayfaya dön
        if (!string.Equals(_currentDestination, "Home", StringComparison.Ordinal))
        {
            NavigateToDestination("Home");
            return true;
        }

        // 5) Ana sayfadayız -> varsayılan davranış
        return false;
    }


    private bool IsAnyPlayerPanelOpen()
    {
        var vm = _playerViewModel;
        return vm is not null &&
            (vm.IsEpgPanelOpen ||
             vm.IsAudioSettingsOpen ||
             vm.IsQualitySettingsOpen ||
             vm.IsInfoPanelOpen ||
             vm.IsEpisodesPanelOpen ||
             vm.IsSleepTimerPanelOpen);
    }

    private IPlayerWindowService? GetPlayerWindowService()
    {
        _playerWindowService ??= GetPlatformServiceResolver()?.GetPlayerWindowService();
        return _playerWindowService;
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
        _currentDestination = destination;
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
                SelectedMediaSubtitle.Text = LocalizationSource.Instance["Mobile.Status.Playback.Starting"];
                await PlaySelectedChannelAsync(channel);
                break;
            case Series series:
                SelectedMediaHost.IsVisible = false;
                return;
            default:
                SelectedMediaTitle.Text = media.GetType().Name;
                SelectedMediaSubtitle.Text = LocalizationSource.Instance["Mobile.Status.Media.Ready"];
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

        // EPG timeline'dan kanal seçimi event'i
        MobilePlayerContent.ChannelSelected -= MobilePlayerContent_ChannelSelected;
        MobilePlayerContent.ChannelSelected += MobilePlayerContent_ChannelSelected;

        MobilePlayerContent.DataContext = _playerViewModel;
        PlayerHost.IsVisible = true;
        UpdatePlayerChromeState();

        // Wire watermark DataContext from Core MainViewModel
        if (_coreMainViewModel?.WatermarkViewModel is { } watermarkVm)
        {
            MobilePlayerContent.MobileWatermark.DataContext = watermarkVm;
        }

        await _playerViewModel.PlayChannelAsync(channel);
        UpdatePictureInPictureState();
    }

    /// <summary>
    /// EPG timeline'dan kanal seçildiğinde çağrılır.
    /// </summary>
    private async void MobilePlayerContent_ChannelSelected(Channel channel)
    {
        if (_coreMainViewModel is null || _playerViewModel is null)
            return;

        // Mevcut player'ı kapat, yeni kanalı oynat
        await PlaySelectedChannelAsync(channel);
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

        // Oynatıcı kapanınca: ekranı uyanık tutmayı bırak ve tam ekran/immersive modundan çık.
        var windowService = GetPlayerWindowService();
        windowService?.SetKeepScreenOn(false);
        windowService?.SetFullScreenMode(false);
        UpdatePictureInPictureState();
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

        UpdatePictureInPictureState();
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

        UpdatePlayerChromeState();
        UpdatePictureInPictureState();
    }

    private void PlayerViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsFullScreen))
        {
            UpdatePlayerChromeState();
            // Tam ekranda yatay yön + immersive sistem çubukları.
            GetPlayerWindowService()?.SetFullScreenMode(_playerViewModel?.IsFullScreen == true);
        }
        else if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            // Oynatma sürerken ekranı uyanık tut.
            GetPlayerWindowService()?.SetKeepScreenOn(_playerViewModel?.IsPlaying == true);
            UpdatePictureInPictureState();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.CurrentChannel) ||
                 e.PropertyName == nameof(PlayerViewModel.CurrentProgram) ||
                 e.PropertyName == nameof(PlayerViewModel.OverlaySecondaryText) ||
                 e.PropertyName == nameof(PlayerViewModel.IsLiveContent) ||
                 e.PropertyName == nameof(PlayerViewModel.IsSeriesContent))
        {
            UpdatePictureInPictureState();
        }
    }

    private void UpdatePictureInPictureState()
    {
        var pictureInPictureService = GetPlatformServiceResolver()?.GetPictureInPictureService();
        var vm = _playerViewModel;
        if (pictureInPictureService is null || vm is null)
        {
            return;
        }

        var channelName = vm.CurrentChannel?.Name ?? vm.ChannelName ?? string.Empty;
        var subtitle = vm.IsLiveContent
            ? FirstNonEmpty(vm.CurrentProgram?.Title, vm.ConnectionStatus, vm.OverlaySecondaryText)
            : FirstNonEmpty(vm.CurrentEpisodeDisplayTitle, vm.OverlaySecondaryText, vm.ConnectionStatus);

        pictureInPictureService.UpdatePictureInPictureState(new PictureInPicturePlaybackState
        {
            CanEnterPictureInPicture = PlayerHost.IsVisible && vm.CurrentChannel is not null,
            IsPlaying = vm.IsPlaying,
            IsLiveContent = vm.IsLiveContent,
            IsSeriesContent = vm.IsSeriesContent,
            Title = channelName,
            Subtitle = subtitle
        });
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private void UpdatePlayerChromeState()
    {
        _isPlayerFullScreen = PlayerHost.IsVisible && _playerViewModel?.IsFullScreen == true;
        HeaderBar.IsVisible = !_isPlayerFullScreen;
        SelectedMediaHost.IsVisible = !_isPlayerFullScreen && SelectedMediaHost.IsVisible;
        UpdateNavigationMode(Bounds.Width);
    }
}
