using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
    private static readonly TimeSpan BackExitPromptWindow = TimeSpan.FromSeconds(2);
    private const int MaxProfileSelectionRetries = 20;
    private CoreMainViewModel? _coreMainViewModel;
    private PlayerViewModel? _playerViewModel;
    private MobileViewModelResolver? _viewModelResolver;
    private MobilePlatformServiceResolver? _platformServiceResolver;
    private IPlayerWindowService? _playerWindowService;
    private MobileBackNavigationService? _backNavigationService;
    private bool _isPlayerFullScreen;
    private string _currentDestination = "Home";
    private DateTime _lastBackExitPromptUtc = DateTime.MinValue;
    private readonly DispatcherTimer _backExitToastTimer;
    private readonly Thickness _headerBasePadding;
    private readonly Thickness _bottomNavBasePadding;
    private Thickness _lastSafeArea;
    private int _profileSelectionRetryCount;
    private bool _startupFlowStarted;

    // Holds the currently active profiles view model when showing the profiles overlay.
    private ProfilesViewModel? _activeProfilesViewModel;

    public MainView()
    {
        InitializeComponent();
        MobileSettingsContent.BackToProfilesRequested += (_, _) => ShowProfileSelection();
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        _backExitToastTimer = new DispatcherTimer { Interval = BackExitPromptWindow };
        _backExitToastTimer.Tick += (_, _) =>
        {
            _backExitToastTimer.Stop();
            BackExitToast.IsVisible = false;
        };

        // Safe-area hesaplaması için temel (tasarım) padding değerlerini sakla.
        _headerBasePadding = HeaderBar.Padding;
        _bottomNavBasePadding = BottomNavigation.Padding;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Android donanım/jest geri tuşunu bu view'e bağla.
        RegisterBackHandler();
        OverlayProfileList.ProfileLoaded -= OverlayProfileList_ProfileLoaded;
        OverlayProfileList.ProfileLoaded += OverlayProfileList_ProfileLoaded;

        // Çentik / sistem çubukları (safe-area) padding'lerini uygula ve değişimleri dinle.
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.InsetsManager is { } insets)
        {
            insets.SafeAreaChanged -= OnSafeAreaChanged;
            insets.SafeAreaChanged += OnSafeAreaChanged;
            ApplySafeArea(insets.SafeAreaPadding);
        }

        StartStartupFlow();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
        => StartStartupFlow();

    private void StartStartupFlow()
    {
        if (_startupFlowStarted)
        {
            return;
        }

        _startupFlowStarted = true;
        Dispatcher.UIThread.Post(
            () => _ = RunStartupFlowAsync(),
            DispatcherPriority.Loaded);
    }

    private async Task RunStartupFlowAsync()
    {
        try
        {
            await ShowLegalConsentIfNeededAsync();

            await Dispatcher.UIThread.InvokeAsync(ShowProfileSelection);
            _ = TryShowReviewPromptAsync();
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(ShowProfileSelection);
        }
    }

    private async Task ShowLegalConsentIfNeededAsync()
    {
        await LegalConsentOverlay.ShowConsentFlowAsync();
    }

    private async Task TryShowReviewPromptAsync()
    {
        try
        {
            if (Application.Current is not App app)
            {
                return;
            }

            var reviewService = app.EnsureServices()?.GetService<IReviewPromptService>();
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

        OverlayProfileList.ProfileLoaded -= OverlayProfileList_ProfileLoaded;
        _backExitToastTimer.Stop();

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

        LegalConsentOverlay.Padding = new Thickness(safe.Left, safe.Top, safe.Right, safe.Bottom);
        ProfilesOverlay.Padding = new Thickness(safe.Left, safe.Top, safe.Right, safe.Bottom);
        _lastSafeArea = safe;
        UpdatePlayerWatermarkInsets();
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
        if (ProfilesOverlay.IsVisible)
        {
            if (_activeProfilesViewModel is { IsManageMode: true })
            {
                _activeProfilesViewModel.ToggleManageModeCommand.Execute(null);
            }

            return true;
        }

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
        if (IsSeriesDetailOpen())
        {
            CloseSeriesDetailIfOpen();
            return true;
        }

        if (!string.Equals(_currentDestination, "Home", StringComparison.Ordinal))
        {
            NavigateToDestination("Home");
            return true;
        }

        // 5) Ana sayfadayiz -> ilk geri basista uyar, kisa sure icindeki ikinci basista Android'e birak.
        var now = DateTime.UtcNow;
        if (now - _lastBackExitPromptUtc <= BackExitPromptWindow)
        {
            BackExitToast.IsVisible = false;
            _backExitToastTimer.Stop();
            _lastBackExitPromptUtc = DateTime.MinValue;
            return false;
        }

        _lastBackExitPromptUtc = now;
        ShowBackExitToast();
        if (BackExitToast.IsVisible)
        {
            return true;
        }

        // 5) Ana sayfadayız -> varsayılan davranış
        return false;
    }

    private void ShowBackExitToast()
    {
        BackExitToast.IsVisible = true;
        _backExitToastTimer.Stop();
        _backExitToastTimer.Start();
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
        var canShowNavigation = CanShowNavigationChrome();
        NavigationRail.IsVisible = useNavigationRail && canShowNavigation;
        BottomNavigation.IsVisible = !useNavigationRail && canShowNavigation;
    }

    private bool CanShowNavigationChrome()
        => !_isPlayerFullScreen &&
           HeaderBar.IsVisible &&
           !ProfilesOverlay.IsVisible &&
           !LegalConsentOverlay.IsVisible;

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
        UpdateNavigationSelection(destination);
    }

    private void UpdateNavigationSelection(string destination)
    {
        var bottomNavVisibleDestinations = new[] { "Home", "Live", "Movies", "Series" };
        var activateMoreInBottomNav = !bottomNavVisibleDestinations.Contains(destination, StringComparer.Ordinal);

        foreach (var root in new Control[] { NavigationRail, BottomNavigation, ShellContent })
        {
            foreach (var button in root.GetVisualDescendants().OfType<Button>())
            {
                if (button.Tag is not string tag)
                {
                    continue;
                }

                var isBottomMore = ReferenceEquals(root, BottomNavigation) &&
                    string.Equals(tag, "More", StringComparison.Ordinal) &&
                    activateMoreInBottomNav;
                var isExactDestination = string.Equals(tag, destination, StringComparison.Ordinal);
                button.Classes.Set("active", isExactDestination || isBottomMore);
            }
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MobileMainViewModel viewModel)
        {
            viewModel.SelectDestination("Settings");
            CloseSeriesDetailIfOpen();
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
        // Profil seçiminde oynatmayı durdur — profil değişimi playback context'ini sıfırlar.
        if (PlayerHost.IsVisible)
        {
            _playerViewModel?.ClosePlayerCommand.Execute(null);
        }
        CloseSeriesDetailIfOpen();

        ProfilesOverlay.IsVisible = true;
        HeaderBar.IsVisible = false;
        NavigationRail.IsVisible = false;
        BottomNavigation.IsVisible = false;
        ShellContent.IsVisible = false;
        CoreContentHost.IsVisible = false;

        // Obtain the profiles view model from the service provider.  Unhook any
        // previous subscriptions so multiple invocations do not accumulate
        // handlers.
        var resolver = GetViewModelResolver();
        if (resolver is null)
        {
            if (_profileSelectionRetryCount < MaxProfileSelectionRetries)
            {
                _profileSelectionRetryCount++;
                Dispatcher.UIThread.Post(ShowProfileSelection, DispatcherPriority.Background);
            }

            return;
        }

        _profileSelectionRetryCount = 0;

        if (resolver != null)
        {
            _activeProfilesViewModel = resolver.GetProfilesViewModel();

            // Bind the overlay list's DataContext to the view model so it
            // populates the profiles.
            ProfilesOverlay.DataContext = _activeProfilesViewModel;
            OverlayProfileList.SetProfilesViewModel(_activeProfilesViewModel);
            _activeProfilesViewModel.RefreshProfiles();
        }

        // Normal navigation remains hidden while the full-screen profiles overlay is active.
    }

    private void OverlayProfileList_ProfileLoaded(object? sender, EventArgs e)
    {
        // Hide overlay
        ProfilesOverlay.IsVisible = false;
        ProfilesOverlay.DataContext = null;

        // Restore nav/header
        HeaderBar.IsVisible = true;

        // Show appropriate navigation rails based on device size and player state
        UpdateNavigationMode(Bounds.Width);

        // Show core content (home) and hide shell content
        ShellContent.IsVisible = false;
        CoreContentHost.IsVisible = true;

        // Navigate to the home screen by selecting the Home destination
        NavigateToDestination("Home");

        // Unhook events to avoid memory leaks
        _activeProfilesViewModel = null;
        OverlayProfileList.ClearProfilesViewModel();
    }



    private MobileViewModelResolver? GetViewModelResolver()
    {
        if (_viewModelResolver is not null)
        {
            return _viewModelResolver;
        }

        if (Application.Current is not App app)
        {
            return null;
        }

        _viewModelResolver = app.EnsureServices()?.GetRequiredService<MobileViewModelResolver>();
        return _viewModelResolver;
    }

    private MobilePlatformServiceResolver? GetPlatformServiceResolver()
    {
        if (_platformServiceResolver is not null)
        {
            return _platformServiceResolver;
        }

        if (Application.Current is not App app)
        {
            return null;
        }

        _platformServiceResolver = app.EnsureServices()?.GetRequiredService<MobilePlatformServiceResolver>();
        return _platformServiceResolver;
    }

    /// <summary>
    /// Navigates to the specified content destination.
    ///
    /// DESIGN DECISION — Player state:
    ///   Player is NOT closed during content navigation (Home, Live, Movies, Series,
    ///   Search, Settings, etc.) to match desktop behavior and standard media-app UX.
    ///   The user can close the player manually via back button or the player's close
    ///   button. Player IS closed on profile selection (ShowProfileSelection) since
    ///   switching profiles resets the playback context.
    /// </summary>
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

        if (!string.Equals(destination, "Series", StringComparison.Ordinal))
        {
            CloseSeriesDetailIfOpen();
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

    private void OnProfilesClick(object? sender, RoutedEventArgs e)
    {
        ShowProfileSelection();
    }

    private void CloseSeriesDetailIfOpen()
    {
        _coreMainViewModel ??= GetViewModelResolver()?.GetCoreMainViewModel();
        _coreMainViewModel?.CloseSeriesDetailCommand.Execute(null);
    }

    private bool IsSeriesDetailOpen()
    {
        _coreMainViewModel ??= GetViewModelResolver()?.GetCoreMainViewModel();
        return _coreMainViewModel?.IsSeriesDetailVisible == true;
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
        UpdatePlayerWatermarkInsets();

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

        // Oynatıcı kapanınca: ekranı uyanık tutmayı bırak, tam ekran/immersive modundan çık
        // ve parlaklığı sistem varsayılanına sıfırla (-1).
        var windowService = GetPlayerWindowService();
        windowService?.SetKeepScreenOn(false);
        windowService?.SetFullScreenMode(false);
        windowService?.SetBrightness(-1);
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
            UpdatePlayerWatermarkInsets();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.IsPiPMode))
        {
            UpdatePlayerWatermarkInsets();
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
            CanEnterPictureInPicture = PlayerHost.IsVisible && vm.CurrentChannel is not null && vm.IsPlaying,
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
        UpdatePlayerWatermarkInsets();
    }

    private void UpdatePlayerWatermarkInsets()
    {
        MobilePlayerContent.ApplyWatermarkInsets(
            _lastSafeArea,
            PlayerHost.IsVisible && _playerViewModel?.IsFullScreen == true,
            _playerViewModel?.IsPiPMode == true);
    }
}
