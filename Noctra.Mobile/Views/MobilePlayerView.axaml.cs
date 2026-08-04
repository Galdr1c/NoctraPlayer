using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Mobile.Localization;
using Noctra.Mobile.Services;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using Material.Icons;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobilePlayerView : UserControl
{
    public static readonly StyledProperty<bool> IsVolumeToastVisibleProperty =
        AvaloniaProperty.Register<MobilePlayerView, bool>(nameof(IsVolumeToastVisible));

    public static readonly StyledProperty<bool> IsSeekToastVisibleProperty =
        AvaloniaProperty.Register<MobilePlayerView, bool>(nameof(IsSeekToastVisible));

    public static readonly StyledProperty<bool> IsDownloadToastVisibleProperty =
        AvaloniaProperty.Register<MobilePlayerView, bool>(nameof(IsDownloadToastVisible));

    public static readonly StyledProperty<bool> IsGestureToastVisibleProperty =
        AvaloniaProperty.Register<MobilePlayerView, bool>(nameof(IsGestureToastVisible));

    public static readonly StyledProperty<string> SeekToastTextProperty =
        AvaloniaProperty.Register<MobilePlayerView, string>(nameof(SeekToastText), "+10s");

    public static readonly StyledProperty<string> GestureToastTextProperty =
        AvaloniaProperty.Register<MobilePlayerView, string>(nameof(GestureToastText), string.Empty);

    public static readonly StyledProperty<MaterialIconKind> SeekToastIconProperty =
        AvaloniaProperty.Register<MobilePlayerView, MaterialIconKind>(nameof(SeekToastIcon), MaterialIconKind.FastForward10);

    public bool IsVolumeToastVisible
    {
        get => GetValue(IsVolumeToastVisibleProperty);
        set => SetValue(IsVolumeToastVisibleProperty, value);
    }

    public bool IsSeekToastVisible
    {
        get => GetValue(IsSeekToastVisibleProperty);
        set => SetValue(IsSeekToastVisibleProperty, value);
    }

    public bool IsDownloadToastVisible
    {
        get => GetValue(IsDownloadToastVisibleProperty);
        set => SetValue(IsDownloadToastVisibleProperty, value);
    }

    public bool IsGestureToastVisible
    {
        get => GetValue(IsGestureToastVisibleProperty);
        set => SetValue(IsGestureToastVisibleProperty, value);
    }

    public string SeekToastText
    {
        get => GetValue(SeekToastTextProperty);
        set => SetValue(SeekToastTextProperty, value);
    }

    public string GestureToastText
    {
        get => GetValue(GestureToastTextProperty);
        set => SetValue(GestureToastTextProperty, value);
    }

    public MaterialIconKind SeekToastIcon
    {
        get => GetValue(SeekToastIconProperty);
        set => SetValue(SeekToastIconProperty, value);
    }

    /// <summary>
    /// EPG timeline'dan kanal seçildiğinde tetiklenir.
    /// MainView bu event'e abone olup kanalı oynatır.
    /// </summary>
    public event Action<Channel>? ChannelSelected;


    // ── Swipe (kaydırma) jest durumu ───────────────────────────────────────
    // Sağ yarı dikey = ses, sol yarı dikey = parlaklık.
    private const double SwipeActivationThreshold = 18d;
    private const double VerticalIntentRatio = 1.35d;
    private const double SwipeSensitivityDivisor = 1.75d;
    private static readonly TimeSpan PostGestureTapSuppression = TimeSpan.FromMilliseconds(350);

    private bool _isSwiping;
    private bool _swipeCandidate;
    private bool _swipeRejected;
    private bool _swipeIsLeftZone;
    private Point _swipeStartPoint;
    private int _swipeStartVolume;
    private double _swipeStartBrightness;
    private DateTime _suppressTapUntilUtc = DateTime.MinValue;

    private readonly Dictionary<long, Point> _activePointers = new();
    
    // Pinch zoom devre dışı - ses/parlaklık gesture'ları ile karışıyordu.
    // private bool _isPinchZooming;
    // private double _pinchStartDistance;
    // private double _pinchStartZoom = 1.0;
    // private Point _pinchStartCenter;
    // private double _currentZoom = 1.0;
    // private double _currentPanX;
    // private double _currentPanY;

    // Yatay sarma önizlemesi: sürükleme sırasında hedef pozisyonu canlı göster.
    private readonly DispatcherTimer _volumeToastTimer;
    private readonly DispatcherTimer _seekToastTimer;
    private readonly DispatcherTimer _downloadToastTimer;
    private readonly DispatcherTimer _gestureToastTimer;
    private readonly DispatcherTimer _singleTapTimer;
    private int? _lastObservedVolume;
    private bool? _lastObservedIsMuted;
    private DateTime _lastVolumeToastShownUtc = DateTime.MinValue;
    private static readonly TimeSpan VolumeToastThrottleInterval = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan SingleTapDelay = TimeSpan.FromMilliseconds(300);

    private IPlayerWindowService? _playerWindowService;
    private IVideoSurfaceService? _videoSurfaceService;
    private ISettingsService? _settingsService;
    private PlayerViewModel? _boundVm;
    private Rect _lastSurfaceRect;
    private bool _gestureHintCheckStarted;

    public MobilePlayerView()
    {
        InitializeComponent();

        EpgPanel.ChannelSelected += channel => ChannelSelected?.Invoke(channel);

        _volumeToastTimer = CreateToastTimer(() => IsVolumeToastVisible = false, TimeSpan.FromMilliseconds(900));
        _seekToastTimer = CreateToastTimer(() => IsSeekToastVisible = false, TimeSpan.FromMilliseconds(850));
        _downloadToastTimer = CreateToastTimer(() => IsDownloadToastVisible = false, TimeSpan.FromMilliseconds(2200));
        _gestureToastTimer = CreateToastTimer(() => IsGestureToastVisible = false, TimeSpan.FromMilliseconds(1200));
        _singleTapTimer = new DispatcherTimer { Interval = SingleTapDelay };
        _singleTapTimer.Tick += (_, _) =>
        {
            _singleTapTimer.Stop();
            ExecuteSingleTapToggle();
        };

        LayoutUpdated += OnLayoutUpdated;
    }

    public void ApplyWatermarkInsets(Thickness safeArea, bool isFullScreen, bool isPictureInPicture)
    {
        const double normalRight = 24;
        const double normalBottom = 120;
        const double controlsVisibleBottom = 180;
        const double detailPanelBottom = 560;
        const double fullScreenBottom = 72;
        const double pictureInPictureRight = 14;
        const double pictureInPictureBottom = 18;

        var right = (isPictureInPicture ? pictureInPictureRight : normalRight) + safeArea.Right;
        var normalPlayerBottom = _boundVm?.IsMobileDetailPanelOpen == true
            ? detailPanelBottom
            : _boundVm?.AreMobileControlsVisible == true
                ? controlsVisibleBottom
                : normalBottom;

        var bottom = (isPictureInPicture
            ? pictureInPictureBottom
            : isFullScreen
                ? fullScreenBottom
                : normalPlayerBottom) + safeArea.Bottom;

        MobileWatermark.Margin = new Thickness(0, 0, right, bottom);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged -= OnPlayerPropertyChanged;
            _boundVm.SkipOverlayRequested -= OnSkipOverlayRequested;
            _boundVm.PremiumUpsellRequested -= OnPremiumUpsellRequested;
        }

        PlayerUpsellHost.TryClose();
        _boundVm = DataContext as PlayerViewModel;
        _lastObservedVolume = _boundVm?.Volume;
        _lastObservedIsMuted = _boundVm?.IsMuted;

        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged += OnPlayerPropertyChanged;
            _boundVm.SkipOverlayRequested += OnSkipOverlayRequested;
            _boundVm.PremiumUpsellRequested += OnPremiumUpsellRequested;
            TryShowGestureHintsOnceAsync();
            QueueVideoSurfaceLayoutUpdate();
        }
    }

    private void OnPremiumUpsellRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => PlayerUpsellHost.Show());
    }

    public bool TryHandleBack()
    {
        return PlayerUpsellHost.TryClose();
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.Volume) or nameof(PlayerViewModel.IsMuted))
        {
            ShowVolumeToastIfVolumeStateChanged();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.DownloadStatusMessage)
                 && !string.IsNullOrWhiteSpace(_boundVm?.DownloadStatusMessage))
        {
            ShowDownloadToast();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.IsDownloadInProgress)
                 && _boundVm?.IsDownloadInProgress == true)
        {
            ShowDownloadToast();
        }

        if (e.PropertyName == nameof(PlayerViewModel.IsLockIndicatorVisible))
        {
            if (_boundVm?.IsLockIndicatorVisible == true)
            {
                _ = PlayLockShakeAnimation();
            }
            else
            {
                _lockAnimationCts?.Cancel();
                ResetLockIndicator();
                LockIndicator.IsVisible = false;
            }
        }

        if (e.PropertyName == nameof(PlayerViewModel.IsEpgPanelOpen))
        {
            if (_boundVm?.IsEpgPanelOpen == true)
            {
                EpgPanel.InitializeTimelineHeader();
                _lastSurfaceRect = default;
                QueueVideoSurfaceLayoutUpdate();
            }
            else
            {
                // EPG kapandı -> native surface normal player slotuna döner.
                _lastSurfaceRect = default;
                QueueVideoSurfaceLayoutUpdate();
            }

            return;
        }

        if (e.PropertyName == nameof(PlayerViewModel.EpgFocusRowIndex)
            && _boundVm?.IsEpgPanelOpen == true)
        {
            EpgPanel.QueueFocusCurrentRow();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // Native Android video surface lives outside Avalonia; keep it aligned
        // with the active player slot on layout and rotation changes.
        if (_boundVm is not null)
        {
            UpdateVideoSurfaceLayout();
            PushPlayerControlsHeight();
        }
    }

    /// <summary>
    /// Alt kontrol barının gerçek yüksekliğini ViewModel'e aktarır. Altyazı overlay'i
    /// tahmini sabit (180) yerine ölçülen değeri kullanır; böylece farklı ekran
    /// yoğunluğu, landscape veya kontrol tasarımı değişikliğinde altyazı ile
    /// kontroller çakışmaz.
    /// </summary>
    private void PushPlayerControlsHeight()
    {
        if (_boundVm is null)
        {
            return;
        }

        var height = PlayerControls.Bounds.Height;
        if (height > 0 && Math.Abs(height - _boundVm.MobilePlayerControlsHeight) > 0.5)
        {
            _boundVm.MobilePlayerControlsHeight = height;
        }
    }

    public void QueueVideoSurfaceLayoutUpdate()
    {
        Dispatcher.UIThread.Post(UpdateVideoSurfaceLayout, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(UpdateVideoSurfaceLayout, DispatcherPriority.Background);
    }

    private void UpdateVideoSurfaceLayout()
    {
        if (_boundVm?.IsPiPMode == true)
        {
            return;
        }

        if (_boundVm?.IsEpgPanelOpen == true)
        {
            UpdateEpgVideoLayout();
            return;
        }

        UpdateNormalVideoLayout();
    }

    private void UpdateNormalVideoLayout()
    {
        // The normal player always fills Android's content root. Keeping the
        // native TextureView on MatchParent lets Android resize it atomically
        // during orientation changes, instead of copying a transient/stale
        // Avalonia pixel rectangle into its LayoutParams.
        var fullScreenRect = new Rect(0, 0, -1, -1);
        if (_lastSurfaceRect == fullScreenRect)
        {
            return;
        }

        _lastSurfaceRect = fullScreenRect;
        GetVideoSurfaceService()?.SetBounds(0, 0, -1, -1);
    }

    /// <summary>
    /// EPG split görünümünde üstteki şeffaf VideoSlot'un ekran (piksel) dikdörtgenini
    /// hesaplar ve native video yüzeyini oraya küçültür. Böylece masaüstündeki
    /// "video üstüne yarı saydam panel" yerine mobilde "video üstte küçülür, EPG altta" olur.
    /// </summary>
    private void UpdateEpgVideoLayout()
    {
        var videoSlot = EpgPanel.VideoSlotControl;
        if (videoSlot is null)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var totalWidth = Bounds.Width;
        var totalHeight = Bounds.Height;
        if (totalWidth <= 0 || totalHeight <= 0)
        {
            return;
        }

        // Video yüksekliği: 16:9, ancak ekranın yarısını geçmesin.
        var desiredHeight = Math.Min(totalWidth * 9.0 / 16.0, totalHeight * 0.5);
        if (Math.Abs(videoSlot.Height - desiredHeight) > 0.5)
        {
            videoSlot.Height = desiredHeight;
            return; // yükseklik değişti; yeni layout pass UpdateEpgVideoLayout'u tekrar tetikler
        }

        // VideoSlot'un pencereye göre konumunu al, piksel ölçeğine çevir.
        var topLeft = videoSlot.TranslatePoint(new Point(0, 0), topLevel);
        if (topLeft is null)
        {
            return;
        }

        var scaling = topLevel.RenderScaling;
        var px = (int)Math.Round(topLeft.Value.X * scaling);
        var py = (int)Math.Round(topLeft.Value.Y * scaling);
        var pw = (int)Math.Round(videoSlot.Bounds.Width * scaling);
        var ph = (int)Math.Round(videoSlot.Bounds.Height * scaling);
        if (pw <= 0 || ph <= 0)
        {
            return;
        }

        var rect = new Rect(px, py, pw, ph);
        if (rect == _lastSurfaceRect)
        {
            return;
        }

        _lastSurfaceRect = rect;
        GetVideoSurfaceService()?.SetBounds(px, py, pw, ph);
    }

    private IVideoSurfaceService? GetVideoSurfaceService()
    {
        if (_videoSurfaceService is not null)
        {
            return _videoSurfaceService;
        }

        if (Application.Current is App { Services: not null } app)
        {
            _videoSurfaceService = app.Services
                .GetService<MobilePlatformServiceResolver>()?
                .GetVideoSurfaceService();
        }

        return _videoSurfaceService;
    }

    private static DispatcherTimer CreateToastTimer(Action elapsed, TimeSpan interval)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            elapsed();
        };
        return timer;
    }

    private void ShowVolumeToast()
    {
        var now = DateTime.UtcNow;
        if (now - _lastVolumeToastShownUtc < VolumeToastThrottleInterval)
        {
            return;
        }

        _lastVolumeToastShownUtc = now;
        IsVolumeToastVisible = true;
        _volumeToastTimer.Stop();
        _volumeToastTimer.Start();
    }

    private void ShowVolumeToastIfVolumeStateChanged()
    {
        if (_boundVm is null)
        {
            return;
        }

        var volumeChanged = _lastObservedVolume != _boundVm.Volume;
        var muteChanged = _lastObservedIsMuted != _boundVm.IsMuted;
        _lastObservedVolume = _boundVm.Volume;
        _lastObservedIsMuted = _boundVm.IsMuted;

        if (volumeChanged || muteChanged)
        {
            ShowVolumeToast();
        }
    }

    private void ShowSeekToast(double seconds)
    {
        SeekToastText = FormatSeekToast(seconds);
        IsSeekToastVisible = true;
        _seekToastTimer.Stop();
        _seekToastTimer.Start();
    }

    private void ShowDownloadToast()
    {
        IsDownloadToastVisible = true;
        _downloadToastTimer.Stop();
        _downloadToastTimer.Start();
    }

    private void ShowGestureToast(string text)
    {
        GestureToastText = text;
        IsGestureToastVisible = true;
        _gestureToastTimer.Stop();
        _gestureToastTimer.Start();
    }

    private void TryShowGestureHintsOnceAsync()
    {
        if (_gestureHintCheckStarted)
        {
            return;
        }

        var settingsService = GetSettingsService();
        if (settingsService is null || settingsService.Settings.HasSeenMobilePlayerGestureHints)
        {
            return;
        }

        _gestureHintCheckStarted = true;
        settingsService.Settings.HasSeenMobilePlayerGestureHints = true;
        _ = settingsService.SaveAsync();

        Dispatcher.UIThread.Post(() =>
        {
            ShowGestureToast(TranslateOrDefault(
                "Player.Mobile.Toast.GestureHints",
                "Left side: brightness. Right side: volume. Double tap: seek."));
        }, DispatcherPriority.Background);
    }

    private void OnSkipOverlayRequested(object? sender, PlayerViewModel.SkipOverlayEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            SeekToastIcon = e.Seconds >= 0 ? MaterialIconKind.FastForward10 : MaterialIconKind.Rewind10;
            ShowSeekToast(e.Seconds);
        });

    private static string FormatSeekToast(double seconds)
    {
        var sign = seconds >= 0 ? "+" : "-";
        var totalSeconds = (int)Math.Round(Math.Abs(seconds));
        if (totalSeconds < 60)
        {
            return $"{sign}{totalSeconds}s";
        }

        var ts = TimeSpan.FromSeconds(totalSeconds);
        return $"{sign}{(int)ts.TotalMinutes}:{ts.Seconds:00}";
    }

    private static string TranslateOrDefault(string key, string fallback)
    {
        var value = LocalizationSource.Instance[key];
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void OnLeftDoubleTapped(object? sender, TappedEventArgs e)
    {
        _singleTapTimer.Stop(); // Çift dokunma: bekleyen tek dokunma toggle'ını iptal et.
        HandleDoubleTapSeek(forward: false);
        e.Handled = true;
    }

    private void OnRightDoubleTapped(object? sender, TappedEventArgs e)
    {
        _singleTapTimer.Stop(); // Çift dokunma: bekleyen tek dokunma toggle'ını iptal et.
        HandleDoubleTapSeek(forward: true);
        e.Handled = true;
    }

    private void HandleDoubleTapSeek(bool forward)
    {
        if (DataContext is not PlayerViewModel playerVm)
        {
            return;
        }

        if (playerVm.IsLocked)
        {
            ShowGestureToast(TranslateOrDefault("Player.Mobile.Toast.Locked", "Kontroller kilitli"));
            return;
        }

        if (playerVm.IsLiveContent)
        {
            ShowGestureToast(TranslateOrDefault("Player.Mobile.Toast.LiveSeekUnavailable", "Canlı yayında ileri/geri sarma kullanılamaz"));
            return;
        }

        // Toast ikonunu yönüne göre ayarla ki geri sarmada ileri oku göstermesin.
        SeekToastIcon = forward ? MaterialIconKind.FastForward10 : MaterialIconKind.Rewind10;

        const string tenSeconds = "10";
        if (forward)
        {
            if (playerVm.SkipForwardCommand.CanExecute(tenSeconds))
            {
                playerVm.SkipForwardCommand.Execute(tenSeconds);
            }
        }
        else if (playerVm.SkipBackwardCommand.CanExecute(tenSeconds))
        {
            playerVm.SkipBackwardCommand.Execute(tenSeconds);
        }
    }

    /// <summary>
    /// Video yüzeyine tek dokunuş: kontrol katmanını (bottom sheet) aç/kapat.
    /// Çift dokunma ile sarma jestiyle çakışmaması için kısa bir gecikmeyle dispatch
    /// edilir; süre dolmadan bir çift dokunma gelirse toggle iptal edilir.
    /// </summary>
    private void OnPlayerBackgroundTapped(object? sender, TappedEventArgs e)
    {
        // PointerReleased sonrasında üretilebilen sahte tap'i de kısa süre engelle.
        if (_isSwiping || DateTime.UtcNow < _suppressTapUntilUtc)
        {
            e.Handled = true;
            return;
        }

        _singleTapTimer.Stop();
        _singleTapTimer.Start();
    }

    private void ExecuteSingleTapToggle()
    {
        if (DataContext is PlayerViewModel playerVm)
        {
            playerVm.ToggleControlsCommand.Execute(null);
        }
    }

    // ── Swipe jestleri ──────────────────────────────────────────────────────

    private void OnZonePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        if (vm.IsLocked)
            return;

        var point = e.GetCurrentPoint(this);
        _activePointers[point.Pointer.Id] = point.Position;
        
        // Pinch zoom devre dışı: ses/parlaklık gesture'ları ile karışıyor.
        // if (_activePointers.Count >= 2)
        // {
        //     BeginPinchZoom();
        //     e.Handled = true;
        //     return;
        // }

        // Timeline veya başka bir alt kontrol birkaç piksel kaçırılsa bile bu alan
        // parlaklık/ses hareketine dönüşmemeli.
        if (IsInsidePlayerControls(point.Position, vm))
        {
            ResetSwipeState();
            e.Handled = true;
            return;
        }

        _swipeCandidate = true;
        _swipeRejected = false;
        _isSwiping = false;
        _swipeStartPoint = point.Position;
        _swipeIsLeftZone = ReferenceEquals(sender, LeftZone);
        _swipeStartVolume = vm.Volume;
        _swipeStartBrightness = GetPlayerWindowService()?.GetBrightness() ?? 0.5;
    }

    private void OnZonePointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (_activePointers.ContainsKey(point.Pointer.Id))
        {
            _activePointers[point.Pointer.Id] = point.Position;
        }

        // Pinch zoom devre dışı.
        // if (_isPinchZooming && _activePointers.Count >= 2)
        // {
        //     HandlePinchZoom();
        //     e.Handled = true;
        //     return;
        // }

        if (!_swipeCandidate ||
            _swipeRejected ||
            DataContext is not PlayerViewModel vm)
            return;

        var dx = point.Position.X - _swipeStartPoint.X;
        var dy = point.Position.Y - _swipeStartPoint.Y;

        if (!_isSwiping)
        {
            var intent = PlayerGesturePolicy.Classify(
                dx,
                dy,
                SwipeActivationThreshold,
                VerticalIntentRatio);

            if (intent == PlayerGestureIntent.Pending)
                return;

            if (intent == PlayerGestureIntent.Rejected)
            {
                _swipeRejected = true;
                _suppressTapUntilUtc = DateTime.UtcNow + PostGestureTapSuppression;
                e.Handled = true;
                return;
            }

            _isSwiping = true;
        }

        var height = Math.Max(1, Bounds.Height);
        var fraction = -dy / (height * SwipeSensitivityDivisor);

        if (_swipeIsLeftZone)
        {
            var brightness = Math.Clamp(_swipeStartBrightness + fraction, 0.0, 1.0);
            GetPlayerWindowService()?.SetBrightness(brightness);
            ShowGestureToast(string.Format(CultureInfo.InvariantCulture, "☀ {0:0}%", brightness * 100));
        }
        else
        {
            var volume = (int)Math.Clamp(_swipeStartVolume + (fraction * 100), 0, 100);
            vm.Volume = volume;
        }

        e.Handled = true;
    }

    private void OnZonePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        _activePointers.Remove(point.Pointer.Id);

        // Pinch zoom devre dışı.
        // if (_isPinchZooming)
        // {
        //     if (_activePointers.Count < 2)
        //     {
        //         _isPinchZooming = false;
        //         _suppressTapUntilUtc = DateTime.UtcNow + PostGestureTapSuppression;
        //         ResetSwipeState();
        //     }
        //
        //     e.Handled = true;
        //     return;
        // }

        if (_isSwiping || _swipeRejected)
        {
            _suppressTapUntilUtc = DateTime.UtcNow + PostGestureTapSuppression;
            e.Handled = true;
        }

        ResetSwipeState();
    }

    // Pinch zoom devre dışı.
    // private void BeginPinchZoom()
    // {
    //     var (first, second) = GetFirstTwoPointers();
    //     _isPinchZooming = true;
    //     ResetSwipeState();
    //
    //     _pinchStartDistance = Distance(first, second);
    //     _pinchStartCenter = Midpoint(first, second);
    //     _pinchStartZoom = _currentZoom;
    // }

    private bool IsInsidePlayerControls(Point position, PlayerViewModel vm)
    {
        if (!vm.IsBottomControlsVisible || !PlayerControls.IsVisible)
            return false;

        var origin = PlayerControls.TranslatePoint(new Point(0, 0), this);
        if (origin is null)
            return false;

        var bounds = new Rect(
            origin.Value.X,
            origin.Value.Y,
            PlayerControls.Bounds.Width,
            PlayerControls.Bounds.Height);

        return bounds.Contains(position);
    }

    private void ResetSwipeState()
    {
        _isSwiping = false;
        _swipeCandidate = false;
        _swipeRejected = false;
    }

    // Pinch zoom devre dışı.
    // private void HandlePinchZoom()
    // {
    //     var (first, second) = GetFirstTwoPointers();
    //     var distance = Distance(first, second);
    //     if (_pinchStartDistance <= 1 || distance <= 1)
    //     {
    //         return;
    //     }
    //
    //     var center = Midpoint(first, second);
    //     _currentZoom = Math.Clamp(_pinchStartZoom * (distance / _pinchStartDistance), 1.0, 3.0);
    //
    //     if (_currentZoom <= 1.001)
    //     {
    //         ResetInteractionTransform();
    //         return;
    //     }
    //
    //     _currentPanX += center.X - _pinchStartCenter.X;
    //     _currentPanY += center.Y - _pinchStartCenter.Y;
    //     _pinchStartCenter = center;
    //
    //     GetVideoSurfaceService()?.SetInteractionTransform(
    //         (float)_currentZoom,
    //         (float)_currentPanX,
    //         (float)_currentPanY);
    // }
    //
    // private void ResetInteractionTransform()
    // {
    //     _currentZoom = 1.0;
    //     _currentPanX = 0.0;
    //     _currentPanY = 0.0;
    //     GetVideoSurfaceService()?.ResetInteractionTransform();
    // }

    // Pinch zoom devre dışı - helper metodları.
    // private (Point First, Point Second) GetFirstTwoPointers()
    // {
    //     using var enumerator = _activePointers.Values.GetEnumerator();
    //     enumerator.MoveNext();
    //     var first = enumerator.Current;
    //     enumerator.MoveNext();
    //     var second = enumerator.Current;
    //     return (first, second);
    // }
    //
    // private static double Distance(Point first, Point second)
    // {
    //     var dx = first.X - second.X;
    //     var dy = first.Y - second.Y;
    //     return Math.Sqrt(dx * dx + dy * dy);
    // }
    //
    // private static Point Midpoint(Point first, Point second)
    //     => new((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0);

    /// <summary>
    /// Yatay sürükleme sırasında hedef sarma miktarını canlı toast olarak gösterir.
    /// Bu yalnızca görsel önizlemedir; asıl seek parmak kalkınca uygulanır.
    /// </summary>
    private IPlayerWindowService? GetPlayerWindowService()
    {
        if (_playerWindowService is not null)
            return _playerWindowService;

        if (Application.Current is App { Services: not null } app)
        {
            _playerWindowService = app.Services
                .GetService<MobilePlatformServiceResolver>()?
                .GetPlayerWindowService();
        }

        return _playerWindowService;
    }

    private ISettingsService? GetSettingsService()
    {
        if (_settingsService is not null)
            return _settingsService;

        if (Application.Current is App { Services: not null } app)
        {
            _settingsService = app.Services.GetService<ISettingsService>();
        }

        return _settingsService;
    }

    private CancellationTokenSource? _lockAnimationCts;

    private ScaleTransform? _lockScaleTransform;
    private RotateTransform? _lockRotateTransform;
    private TranslateTransform? _lockTranslateTransform;

    private ScaleTransform LockScaleTransform
        => _lockScaleTransform ??= ((TransformGroup)LockIndicator.RenderTransform!).Children[0] as ScaleTransform
            ?? throw new InvalidOperationException("LockIndicator ScaleTransform not found");
    private RotateTransform LockRotateTransform
        => _lockRotateTransform ??= ((TransformGroup)LockIndicator.RenderTransform!).Children[1] as RotateTransform
            ?? throw new InvalidOperationException("LockIndicator RotateTransform not found");
    private TranslateTransform LockTranslateTransform
        => _lockTranslateTransform ??= ((TransformGroup)LockIndicator.RenderTransform!).Children[2] as TranslateTransform
            ?? throw new InvalidOperationException("LockIndicator TranslateTransform not found");

    private async Task PlayLockShakeAnimation()
    {
        _lockAnimationCts?.Cancel();

        var cts = new CancellationTokenSource();
        _lockAnimationCts = cts;

        try
        {
            var ct = cts.Token;

            ResetLockIndicator();

            LockIndicator.IsVisible = true;
            LockIndicator.Opacity = 0;

            await AnimateAsync(
                durationMs: 110,
                update: progress =>
                {
                    var eased = EaseOutBack(progress);
                    var scale = Lerp(0.78, 1.08, eased);
                    LockScaleTransform.ScaleX = scale;
                    LockScaleTransform.ScaleY = scale;
                    LockIndicator.Opacity = Lerp(0, 1, EaseOutCubic(progress));
                },
                ct);

            await AnimateAsync(
                durationMs: 560,
                update: progress =>
                {
                    const double amplitude = 16;
                    const double oscillations = 4.25;
                    const double damping = 4.5;

                    var envelope = Math.Exp(-damping * progress);
                    var phase = progress * Math.PI * 2 * oscillations;
                    var shakeX = Math.Sin(phase) * amplitude * envelope;
                    var shakeY = Math.Sin((phase * 1.7) + 0.8) * 1.4 * envelope;
                    var rotation = -shakeX * 0.28;
                    var scalePulse = 1 + (0.055 * envelope * Math.Cos(phase));

                    LockTranslateTransform.X = shakeX;
                    LockTranslateTransform.Y = shakeY;
                    LockRotateTransform.Angle = rotation;
                    LockScaleTransform.ScaleX = scalePulse;
                    LockScaleTransform.ScaleY = scalePulse;
                    LockIndicator.Opacity = 1;
                },
                ct);

            var startX = LockTranslateTransform.X;
            var startY = LockTranslateTransform.Y;
            var startRotation = LockRotateTransform.Angle;
            var startScaleX = LockScaleTransform.ScaleX;
            var startScaleY = LockScaleTransform.ScaleY;

            await AnimateAsync(
                durationMs: 120,
                update: progress =>
                {
                    var eased = EaseOutCubic(progress);
                    LockTranslateTransform.X = Lerp(startX, 0, eased);
                    LockTranslateTransform.Y = Lerp(startY, 0, eased);
                    LockRotateTransform.Angle = Lerp(startRotation, 0, eased);
                    LockScaleTransform.ScaleX = Lerp(startScaleX, 1, eased);
                    LockScaleTransform.ScaleY = Lerp(startScaleY, 1, eased);
                },
                ct);

            await Task.Delay(220, ct);

            await AnimateAsync(
                durationMs: 160,
                update: progress =>
                {
                    var eased = SmoothStep(progress);
                    LockIndicator.Opacity = Lerp(1, 0, eased);
                    LockScaleTransform.ScaleX = Lerp(1, 0.92, eased);
                    LockScaleTransform.ScaleY = Lerp(1, 0.92, eased);
                },
                ct);

            if (ReferenceEquals(_lockAnimationCts, cts))
                LockIndicator.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_lockAnimationCts, cts))
            {
                ResetLockIndicator();
                LockIndicator.IsVisible = false;
                _lockAnimationCts = null;
            }

            cts.Dispose();
        }
    }

    private static async Task AnimateAsync(
        int durationMs,
        Action<double> update,
        CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var progress = Math.Clamp(
                sw.Elapsed.TotalMilliseconds / durationMs,
                0, 1);

            update(progress);

            if (progress >= 1)
                break;

            await Task.Delay(16, cancellationToken);
        }
    }

    private void ResetLockIndicator()
    {
        LockTranslateTransform.X = 0;
        LockTranslateTransform.Y = 0;
        LockRotateTransform.Angle = 0;
        LockScaleTransform.ScaleX = 1;
        LockScaleTransform.ScaleY = 1;
        LockIndicator.Opacity = 0;
    }

    private static double Lerp(double start, double end, double progress)
        => start + ((end - start) * progress);

    private static double EaseOutCubic(double progress)
        => 1 - Math.Pow(1 - progress, 3);

    private static double EaseOutBack(double progress)
    {
        const double overshoot = 1.70158;
        const double multiplier = overshoot + 1;
        return 1 + (multiplier * Math.Pow(progress - 1, 3)) + (overshoot * Math.Pow(progress - 1, 2));
    }

    private static double SmoothStep(double progress)
        => progress * progress * (3 - (2 * progress));
}
