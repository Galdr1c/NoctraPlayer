using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
#if DEBUG
using HotAvalonia;
#endif

using Microsoft.Extensions.DependencyInjection;
using Noctra.Mobile.Behaviors;
using Noctra.Mobile.Controls;
using Noctra.Models;
using Noctra.Mobile.Localization;
using Noctra.Mobile.Services;
using Noctra.Services;
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
    private ScopedServiceLease<SettingsViewModel>? _settingsViewModelLease;
    private MobilePlatformServiceResolver? _platformServiceResolver;
    private IPlayerWindowService? _playerWindowService;
    private PlayerResumeResolver? _playerResumeResolver;
    private CancellationTokenSource? _playbackSelectionCts;
    private MobileBackNavigationService? _backNavigationService;
    private ReviewPromptFallbackHandler? _fallbackHandler;
    private bool _isPlayerFullScreen;
    private string _currentDestination = "Home";
    private DateTime _lastBackExitPromptUtc = DateTime.MinValue;
    private readonly DispatcherTimer _backExitToastTimer;
    private readonly Thickness _headerBasePadding;
    private readonly Thickness _bottomNavBasePadding;
    private Thickness _lastSafeArea;
    private int _profileSelectionRetryCount;
    private bool _startupFlowStarted;
    private readonly Stack<string> _navigationHistory = new();
    private bool _isNavigatingBack;
    private MobileCollapsibleNavigationRail? _navigationRailController;
    private readonly MobileScrollEdgeFeedbackController _scrollEdgeFeedbackController;
    private long _navigationVersion;
    private readonly object _settingsReleaseSync = new();
    private Task _pendingSettingsRelease = Task.CompletedTask;

    // Holds the currently active profiles view model when showing the profiles overlay.
    private ProfilesViewModel? _activeProfilesViewModel;

    /// <summary>
    /// Hot Avalonia XAML reload sonrası UI state'i yeniden uygular.
    /// XAML yeniden yüklendiğinde tüm panellerin IsVisible'ı false olur.
    /// Startup flow'u tekrar başlatmak yerine sadece mevcut durumu yeniden uygular.
    /// </summary>
#if DEBUG
    [AvaloniaHotReload]
#endif
    private void OnHotReload()
    {
        // Event aboneliklerini yeniden kur (XAML reload sırasında kaybolabilir)
        OverlayProfileList.ProfileLoaded -= OverlayProfileList_ProfileLoaded;
        OverlayProfileList.ProfileLoaded += OverlayProfileList_ProfileLoaded;
        WireCategorySelectionEvents();

        if (_navigationRailController is null ||
            !_navigationRailController.IsAttachedTo(NavigationRail))
        {
            var isExpanded = _navigationRailController?.IsExpanded ?? true;
            _navigationRailController = new MobileCollapsibleNavigationRail(
                NavigationRail,
                isExpanded);
        }
        else
        {
            _navigationRailController.ApplyCurrentState();
        }

        _scrollEdgeFeedbackController.RefreshVisualTree();

        UpdateNavigationMode(Bounds.Width);
        UpdateContentVisibility(_currentDestination);
        UpdatePlayerChromeState();
    }

    public MainView()
    {
        InitializeComponent();
        WireCategorySelectionEvents();
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

        _navigationRailController = new MobileCollapsibleNavigationRail(NavigationRail);
        _scrollEdgeFeedbackController = new MobileScrollEdgeFeedbackController(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Android donanım/jest geri tuşunu bu view'e bağla.
        RegisterBackHandler();
        AddHandler(InputElement.GotFocusEvent, TextBox_GotFocus);
        OverlayProfileList.ProfileLoaded -= OverlayProfileList_ProfileLoaded;
        OverlayProfileList.ProfileLoaded += OverlayProfileList_ProfileLoaded;

        // Çentik / sistem çubukları (safe-area) padding'lerini uygula ve değişimleri dinle.
        var topLevel = TopLevel.GetTopLevel(this);
        if (OperatingSystem.IsAndroid() && topLevel is not null)
        {
            // The native TextureView lives below Avalonia's SurfaceView so player
            // controls can stay above the video. Transparent top-level composition
            // lets pixels not painted by the player overlay reveal that native view.
            topLevel.TransparencyLevelHint =
            [
                WindowTransparencyLevel.Transparent,
                WindowTransparencyLevel.None
            ];
            topLevel.Background = Brushes.Transparent;
        }

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
        var resolver = GetViewModelResolver();
        var coreVm = resolver?.GetCoreMainViewModel();
        if (coreVm?.CurrentProfile is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProfilesOverlay.IsVisible = false;
                ProfilesOverlay.DataContext = null;
                HeaderBar.IsVisible = true;
                HeaderProfileButton.DataContext = coreVm;
                UpdateNavigationMode(Bounds.Width);

                string dest = "Home";
                if (DataContext is MobileMainViewModel viewModel)
                {
                    dest = viewModel.SelectedDestination;
                }
                NavigateToDestination(dest);
            });
            return;
        }

        try
        {
            await ShowLegalConsentIfNeededAsync();

            await Dispatcher.UIThread.InvokeAsync(ShowProfileSelection);
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

            // Register the Avalonia fallback handler once so the Android service
            // can delegate to our bottom sheet instead of a native dialog.
            RegisterFallbackHandlerIfNeeded(app);

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

    private void RegisterFallbackHandlerIfNeeded(App app)
    {
        if (_fallbackHandler is not null)
            return;

        _fallbackHandler = app.EnsureServices()?.GetService<ReviewPromptFallbackHandler>();
        if (_fallbackHandler is not null)
        {
            _fallbackHandler.Register(ShowReviewPromptOverlayAsync);
            _fallbackHandler.RegisterSurfaceCheck(IsReviewSurfaceReady);
        }
    }

    private async Task<ReviewPromptResult> ShowReviewPromptOverlayAsync(CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ReviewPromptOverlay.WaitForResultAsync(cancellationToken);
        }

        var resultTask = await Dispatcher.UIThread.InvokeAsync(
            () => ReviewPromptOverlay.WaitForResultAsync(cancellationToken));
        return resultTask;
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

        // A TextBox is focused — user is actively editing a form field
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox)
            return false;

        return true;
    }

    private void TextBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox textBox)
        {
            DispatcherTimer.RunOnce(() =>
            {
                if (textBox.IsFocused)
                {
                    // ScrollViewer'ın kendi BringIntoViewOnFocusChange davranışı zaten
                    // TextBox görünür alana kaydırdıysa tekrar çağırmayalım (çift kayma/zıplama önlemi).
                    if (!IsTextBoxVisibleInViewport(textBox))
                    {
                        bool isPinOrPassword = textBox.PasswordChar != '\0';
                        if (isPinOrPassword)
                        {
                            var bounds = textBox.Bounds;
                            var height = bounds.Height > 0 ? bounds.Height : 48;
                            textBox.BringIntoView(new Rect(0, 0, bounds.Width, height + 24));
                        }
                        else
                        {
                            textBox.BringIntoView();
                        }
                    }
                }
            }, TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>
    /// TextBox'ın üst üste binen ScrollViewer içinde görünür alanda (viewport)
    /// olup olmadığını kontrol eder. Böylece hem BringIntoViewOnFocusChange hem de
    /// global GotFocus handler aynı anda çalıştığında çift kayma yaşanmaz.
    /// </summary>
    private static bool IsTextBoxVisibleInViewport(TextBox textBox)
    {
        var scrollViewer = textBox.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer is null)
        {
            return false;
        }

        // TextBox'ın köşelerini ScrollViewer koordinat sistemine dönüştür
        var topLeft = textBox.TranslatePoint(new Point(0, 0), scrollViewer);
        var bottomRight = textBox.TranslatePoint(
            new Point(textBox.Bounds.Width, textBox.Bounds.Height), scrollViewer);

        if (topLeft is not { } top || bottomRight is not { } bottom)
        {
            return false;
        }

        var scrollOffset = scrollViewer.Offset.Y;
        var viewportHeight = scrollViewer.Bounds.Height;
        if (viewportHeight <= 0)
        {
            return false;
        }

        // TextBox tamamen viewport içinde mi?
        return top.Y >= scrollOffset && bottom.Y <= scrollOffset + viewportHeight;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, TextBox_GotFocus);
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
        _scrollEdgeFeedbackController.Hide();
        CategorySelectionOverlay.TryClose();

        CancelAndDisposePlaybackSelection(
            Interlocked.Exchange(ref _playbackSelectionCts, null));
        _fallbackHandler?.Unregister();
        ReleaseSettingsViewModel();

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
        ReviewPromptOverlay.Padding = new Thickness(safe.Left, 0, safe.Right, safe.Bottom);
        ProfilesOverlay.Padding = new Thickness(safe.Left, safe.Top, safe.Right, safe.Bottom);
        CategorySelectionOverlay.ApplySafeArea(safe);
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

        // 1) Profil ekranı açıksa -> onun kendi iç geri yönlendirmesini çalıştır
        if (ProfilesOverlay.IsVisible)
        {
            if (OverlayProfileList.TryHandleBack())
            {
                return true;
            }

            // Profil listesinde kapatılacak başka bir alt ekran yoksa:
            _coreMainViewModel ??= GetViewModelResolver()?.GetCoreMainViewModel();
            if (_coreMainViewModel?.CurrentProfile is not null)
            {
                // Eğer halihazırda yüklü bir profil varsa (örneğin ayarlardan profil değiştirmeye girildiyse),
                // profil seçim ekranını kapat ve ana akışa dön.
                ProfilesOverlay.IsVisible = false;
                ProfilesOverlay.DataContext = null;
                HeaderBar.IsVisible = true;
                HeaderProfileButton.DataContext = _coreMainViewModel;
                UpdateNavigationMode(Bounds.Width);
                ShellContent.IsVisible = false;
                CoreContentHost.IsVisible = true;
                NavigateToDestination("Home");
                return true;
            }

            // Yüklü profil yoksa, geri tuşu uygulamadan çıkış yapmalıdır (toast veya çıkış).
            var nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastBackExitPromptUtc <= BackExitPromptWindow)
            {
                BackExitToast.IsVisible = false;
                _backExitToastTimer.Stop();
                _lastBackExitPromptUtc = DateTime.MinValue;
                return false;
            }

            _lastBackExitPromptUtc = nowUtc;
            ShowBackExitToast();
            return true;
        }

        if (CategorySelectionOverlay.TryClose())
        {
            RestoreChromeAfterCategorySelection();
            return true;
        }

        if (MobileLiveContent.IsVisible && MobileLiveContent.TryHandleBack())
        {
            return true;
        }

        if (MobileMoviesContent.IsVisible && MobileMoviesContent.TryHandleBack())
        {
            return true;
        }

        if (MobileSeriesContent.IsVisible && MobileSeriesContent.TryHandleBack())
        {
            return true;
        }

        if (MobileDownloadsContent.IsVisible && MobileDownloadsContent.TryHandleBack())
        {
            return true;
        }

        // Settings alt katmanlari (selection, upsell ve legal document) once kapanir.
        if (MobileSettingsContent.IsVisible && MobileSettingsContent.TryHandleBack())
        {
            return true;
        }

        if (PlayerHost.IsVisible && MobilePlayerContent.TryHandleBack())
        {
            return true;
        }

        if (PlayerHost.IsVisible &&
            _playerViewModel?.ActiveMobilePanelState != PlayerViewModel.MobilePanelState.None)
        {
            _playerViewModel.BackFromPlayerPanelCommand.Execute(null);
            return true;
        }
        if (PlayerHost.IsVisible && _playerViewModel is { IsFullScreen: true })
        {
            _playerViewModel.IsFullScreen = false;
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
            if (_navigationHistory.Count > 0)
            {
                var previous = _navigationHistory.Pop();
                _isNavigatingBack = true;
                try
                {
                    NavigateToDestination(previous);
                }
                finally
                {
                    _isNavigatingBack = false;
                }
                return true;
            }

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

    private IPlayerWindowService? GetPlayerWindowService()
    {
        _playerWindowService ??= GetPlatformServiceResolver()?.GetPlayerWindowService();
        return _playerWindowService;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // DeviceMetricsService: cihaz sınıfını güncelle (responsive token'lar için)
        DeviceMetricsService.Instance.ApplySize(e.NewSize.Width, e.NewSize.Height);
        UpdateNavigationMode(e.NewSize.Width);
    }

    private void UpdateNavigationMode(double width)
    {
        var useNavigationRail = width >= TabletBreakpoint;
        var canShowNavigation = CanShowNavigationChrome();
        NavigationRail.IsVisible = useNavigationRail && canShowNavigation;
        BottomNavigation.IsVisible = !useNavigationRail && canShowNavigation;

        if (useNavigationRail)
        {
            _navigationRailController?.ApplyCurrentState();
        }
    }

    private bool CanShowNavigationChrome()
        => !PlayerHost.IsVisible &&
           !_isPlayerFullScreen &&
           !CategorySelectionOverlay.IsVisible &&
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
        CloseCategorySelection();

        if (destination != "Live")
        {
            MobileLiveContent.TryHandleBack();
        }

        if (destination != "Movies")
        {
            MobileMoviesContent.TryHandleBack();
        }

        if (destination != "Series")
        {
            MobileSeriesContent.TryHandleBack();
        }

        if (destination != "Downloads")
        {
            MobileDownloadsContent.TryHandleBack();
        }

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
        MobileSlideTransitionBehavior.SetTriggerValue(showCoreContent ? CoreContentHost : ShellContent, destination);
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
        NavigateToDestination("Settings");
    }

    private void ShowProfileSelection()
    {
        CloseCategorySelection();

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
        _coreMainViewModel ??= GetViewModelResolver()?.GetCoreMainViewModel();
        HeaderProfileButton.DataContext = _coreMainViewModel;

        // Show appropriate navigation rails based on device size and player state
        UpdateNavigationMode(Bounds.Width);

        // Show core content (home) and hide shell content
        ShellContent.IsVisible = false;
        CoreContentHost.IsVisible = true;

        // Navigate to the home screen by selecting the Home destination
        NavigateToDestination("Home");
        _ = TryShowReviewPromptAsync();

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

    private PlayerResumeResolver? GetPlayerResumeResolver()
    {
        if (_playerResumeResolver is not null)
        {
            return _playerResumeResolver;
        }

        if (Application.Current is not App app)
        {
            return null;
        }

        var watchHistoryService = app.EnsureServices()?.GetService<IWatchHistoryService>();
        if (watchHistoryService is null)
        {
            return null;
        }

        return _playerResumeResolver = new PlayerResumeResolver(watchHistoryService);
    }

    private static void CancelAndDisposePlaybackSelection(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed selection may have disposed itself concurrently.
        }
        finally
        {
            cancellation.Dispose();
        }
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
        _ = NavigateToDestinationAsync(destination);
    }

    private async Task NavigateToDestinationAsync(string destination)
    {
        if (DataContext is not MobileMainViewModel viewModel)
        {
            return;
        }

        var version = Interlocked.Increment(ref _navigationVersion);

        // Navigation history management: push when navigating from More to a sub-page,
        // clear when switching tabs. Skip during back navigation to avoid double-pushing.
        if (!_isNavigatingBack)
        {
            if (string.Equals(_currentDestination, "More", StringComparison.Ordinal) &&
                destination is not ("Home" or "Live" or "Movies" or "Series" or "More"))
            {
                _navigationHistory.Push(_currentDestination);
            }
            else if (destination is "Home" or "Live" or "Movies" or "Series" or "More")
            {
                _navigationHistory.Clear();
            }
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

        if (string.Equals(destination, "Settings", StringComparison.Ordinal))
        {
            // Snapshot the current lease on the UI thread and join the serialized
            // release chain. A newer navigation request can supersede this await,
            // but a new Settings scope is never created before the old one is fully
            // flushed and disposed.
            var settingsRelease = QueueSettingsRelease();
            await settingsRelease;
            if (version != Volatile.Read(ref _navigationVersion))
            {
                return;
            }

            _settingsViewModelLease = resolver.CreateSettingsViewModelScope();
            MobileSettingsContent.DataContext = _settingsViewModelLease.Service;
        }
        else
        {
            // Non-Settings navigation does not block the page transition, but the
            // returned task remains in the release chain for a later Settings open.
            QueueSettingsRelease();
        }

        if (version != Volatile.Read(ref _navigationVersion))
        {
            return;
        }

        // BUG FIX: "More" menüsündeki profil kartı (avatar+ad) Core MainViewModel'e
        // ihtiyaç duyuyor — MainView'ın kendi DataContext'i (lightweight Mobile VM)
        // CurrentProfile bilgisini içermiyor. Diğer içerik panelleri gibi açıkça atıyoruz.
        if (destination == "More")
        {
            _coreMainViewModel ??= resolver.GetCoreMainViewModel();
            HeaderProfileButton.DataContext = _coreMainViewModel;
            MoreProfileCard.DataContext = _coreMainViewModel;
        }

        if (destination is "Home" or "Live" or "Movies" or "Series" or "Search" or "Favorites" or "MyList" or "History" or "Downloads")
        {
            _coreMainViewModel ??= resolver.GetCoreMainViewModel();
            HeaderProfileButton.DataContext = _coreMainViewModel;
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

    private Task QueueSettingsRelease()
    {
        lock (_settingsReleaseSync)
        {
            // Capture and disconnect the lease synchronously while we are still on
            // the Avalonia UI thread. The asynchronous continuation below never
            // touches UI.
            var lease = Interlocked.Exchange(ref _settingsViewModelLease, null);
            MobileSettingsContent.DataContext = null;

            if (lease is null)
            {
                return _pendingSettingsRelease;
            }

            _pendingSettingsRelease = ReleaseSettingsLeaseAfterAsync(
                _pendingSettingsRelease,
                lease);

            return _pendingSettingsRelease;
        }
    }

    private static async Task ReleaseSettingsLeaseAfterAsync(
        Task previousRelease,
        ScopedServiceLease<SettingsViewModel> lease)
    {
        try
        {
            await previousRelease.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A previous release must not poison the chain and permanently block
            // reopening Settings.
            System.Diagnostics.Debug.WriteLine(
                $"[MainView] Previous Settings release failed: {ex}");
        }

        try
        {
            if (lease.Service is { } viewModel)
            {
                await viewModel.FlushPendingAutoSaveAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MainView] FlushPendingAutoSave failed: {ex}");
        }

        try
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Keep the serialized task successfully completed so a disposal failure
            // cannot lock the user out of Settings on the next navigation.
            System.Diagnostics.Debug.WriteLine(
                $"[MainView] Settings scope disposal failed: {ex}");
        }
    }

    private void ReleaseSettingsViewModel()
    {
        // Queue the current lease instead of launching an untracked release task.
        QueueSettingsRelease();
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
        try
        {
            switch (media)
            {
                case Channel channel:
                    SelectedMediaHost.IsVisible = false;
                    SelectedMediaTitle.Text = channel.Name;
                    SelectedMediaSubtitle.Text = LocalizationSource.Instance["Mobile.Status.Playback.Starting"];
                    await PlaySelectedChannelAsync(channel);
                    SelectedMediaHost.IsVisible = false;
                    return;
                case Series series:
                    _ = series;
                    SelectedMediaHost.IsVisible = false;
                    return;
                default:
                    SelectedMediaTitle.Text = media.GetType().Name;
                    SelectedMediaSubtitle.Text = LocalizationSource.Instance["Mobile.Status.Media.Ready"];
                    SelectedMediaHost.IsVisible = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowPlaybackStartupError(media, ex);
        }
    }

    private async Task PlaySelectedChannelAsync(Channel channel)
    {
        var resolver = GetViewModelResolver();
        if (resolver is null)
        {
            return;
        }

        _playerViewModel ??= resolver.GetPlayerViewModel();
        var selectionCts = new CancellationTokenSource();
        var cancellationToken = selectionCts.Token;
        var previousSelection = Interlocked.Exchange(ref _playbackSelectionCts, selectionCts);
        CancelAndDisposePlaybackSelection(previousSelection);

        var playbackIntent = _playerViewModel.BeginPlaybackIntent(stopCurrentPlayback: true);

        var platformResolver = GetPlatformServiceResolver();
        var videoSurfaceService = platformResolver?.GetVideoSurfaceService();
        try
        {
            if (videoSurfaceService is not null)
            {
                await videoSurfaceService.ShowAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!_playerViewModel.IsPlaybackIntentCurrent(playbackIntent))
            {
                return;
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

        var coreViewModel = _coreMainViewModel ??= resolver.GetCoreMainViewModel();
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

            double? startPosition = null;
            var resumeResolver = GetPlayerResumeResolver();
            if (resumeResolver is not null)
            {
                var resumePosition = await resumeResolver.ResolveAsync(
                    coreViewModel?.CurrentProfileId,
                    channel,
                    coreViewModel?.CurrentEpisodePlaybackContext,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (!_playerViewModel.IsPlaybackIntentCurrent(playbackIntent))
                {
                    return;
                }

                if (resumePosition.HasValue)
                {
                    var shouldResume = await _playerViewModel.ShowResumeDialogAsync(
                        resumePosition.Value,
                        cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_playerViewModel.IsPlaybackIntentCurrent(playbackIntent))
                    {
                        return;
                    }

                    startPosition = shouldResume
                        ? resumePosition.Value
                        : 0d;
                }
            }

            await _playerViewModel.PlayChannelAsync(channel, startPosition, playbackIntent);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_playerViewModel.IsPlaybackIntentCurrent(playbackIntent))
            {
                return;
            }

            UpdatePictureInPictureState();
        }
        catch (OperationCanceledException)
        {
            if (_playerViewModel.IsPlaybackIntentCurrent(playbackIntent))
            {
                videoSurfaceService?.Hide();
                PlayerHost.IsVisible = false;
                UpdatePlayerChromeState();
            }
        }
        catch (Exception ex)
        {
            if (_playerViewModel.IsPlaybackIntentCurrent(playbackIntent))
            {
                ShowPlaybackStartupError(channel, ex);
            }
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _playbackSelectionCts, null, selectionCts),
                    selectionCts))
            {
                selectionCts.Dispose();
            }
        }
    }

    private void ShowPlaybackStartupError(object media, Exception ex)
    {
        var title = media is Channel channel
            ? channel.Name
            : media.GetType().Name;
        var message = UserFriendlyErrorMessage.FromException(ex);

        SelectedMediaTitle.Text = title;
        SelectedMediaSubtitle.Text = message;
        SelectedMediaHost.IsVisible = true;

        if (_playerViewModel is not null)
        {
            _playerViewModel.ConnectionStatus = message;
            _playerViewModel.IsBuffering = false;
            _playerViewModel.BufferingProgress = 0;
        }
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
        var platform = GetPlatformServiceResolver();
        var window = GetPlayerWindowService();

        platform?.GetVideoSurfaceService()?.ResetInteractionTransform();
        platform?.GetVideoSurfaceService()?.Hide();

        PlayerHost.IsVisible = false;

        if (_playerViewModel is not null)
        {
            _playerViewModel.IsFullScreen = false;
            _playerViewModel.IsLocked = false;
            _playerViewModel.IsPiPMode = false;
        }

        window?.SetKeepScreenOn(false);
        window?.SetFullScreenMode(false);
        window?.SetBrightness(-1);

        UpdatePlayerChromeState();
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
        else if (e.PropertyName == nameof(PlayerViewModel.IsVisible) ||
                 e.PropertyName == nameof(PlayerViewModel.IsMobileDetailPanelOpen) ||
                 e.PropertyName == nameof(PlayerViewModel.IsActionsPanelOpen))
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
            // Manual PiP must also work for paused, already-loaded media so the
            // PiP play action can resume it. Auto-enter remains playing-only in
            // AndroidPictureInPictureService.
            CanEnterPictureInPicture =
                PlayerHost.IsVisible &&
                vm.CurrentChannel is not null &&
                (vm.IsPlaying || vm.VideoPlayerService.HasLoadedMedia),
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
        var isPlayerVisible = PlayerHost.IsVisible;
        _isPlayerFullScreen = isPlayerVisible && _playerViewModel?.IsFullScreen == true;

        ShellLayer.IsVisible = !isPlayerVisible;
        HeaderBar.IsVisible = !isPlayerVisible;
        if (isPlayerVisible)
        {
            NavigationRail.IsVisible = false;
            BottomNavigation.IsVisible = false;
            MobilePlayerContent.QueueVideoSurfaceLayoutUpdate();
        }
        else
        {
            UpdateNavigationMode(Bounds.Width);
        }

        UpdatePlayerWatermarkInsets();
    }

    private void UpdatePlayerWatermarkInsets()
    {
        MobilePlayerContent.ApplyWatermarkInsets(
            _lastSafeArea,
            PlayerHost.IsVisible && _playerViewModel?.IsFullScreen == true,
            _playerViewModel?.IsPiPMode == true);
    }

    private void WireCategorySelectionEvents()
    {
        MobileLiveContent.CategorySelectionRequested -= Content_CategorySelectionRequested;
        MobileMoviesContent.CategorySelectionRequested -= Content_CategorySelectionRequested;
        MobileSeriesContent.CategorySelectionRequested -= Content_CategorySelectionRequested;
        CategorySelectionOverlay.CloseRequested -= CategorySelectionOverlay_CloseRequested;

        MobileLiveContent.CategorySelectionRequested += Content_CategorySelectionRequested;
        MobileMoviesContent.CategorySelectionRequested += Content_CategorySelectionRequested;
        MobileSeriesContent.CategorySelectionRequested += Content_CategorySelectionRequested;
        CategorySelectionOverlay.CloseRequested += CategorySelectionOverlay_CloseRequested;
    }

    private void Content_CategorySelectionRequested(
        object? sender,
        MobileCategorySelectionRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: CoreMainViewModel viewModel })
        {
            return;
        }

        if (sender is MobileLiveView liveView)
        {
            liveView.TryHandleBack();
        }
        else if (sender is MobileMoviesView moviesView)
        {
            moviesView.TryHandleBack();
        }
        else if (sender is MobileSeriesView seriesView)
        {
            seriesView.TryHandleBack();
        }

        CategorySelectionOverlay.Show(viewModel, LocalizationSource.Instance[e.TitleKey]);
        HeaderBar.IsVisible = false;
        NavigationRail.IsVisible = false;
        BottomNavigation.IsVisible = false;
        MobileSlideTransitionBehavior.SetTriggerValue(
            CategorySelectionOverlay,
            $"Category:{e.TitleKey}:{DateTime.UtcNow.Ticks}");
    }

    private void CategorySelectionOverlay_CloseRequested(object? sender, EventArgs e)
        => CloseCategorySelection();

    private bool CloseCategorySelection()
    {
        if (!CategorySelectionOverlay.TryClose())
        {
            return false;
        }

        MobileSlideTransitionBehavior.SetTriggerValue(CategorySelectionOverlay, null);
        RestoreChromeAfterCategorySelection();
        return true;
    }

    private void RestoreChromeAfterCategorySelection()
    {
        HeaderBar.IsVisible = !PlayerHost.IsVisible &&
                              !ProfilesOverlay.IsVisible &&
                              !LegalConsentOverlay.IsVisible &&
                              !ReviewPromptOverlay.IsVisible;
        UpdateNavigationMode(Bounds.Width);
    }
}
