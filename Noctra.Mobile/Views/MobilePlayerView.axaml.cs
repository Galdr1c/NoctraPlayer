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

    // ── Kilit göstergesi uzun basma ────────────────────────────────────────
    private const int LockLongPressMs = 500;
    private CancellationTokenSource? _lockPressCts;

    // ── Swipe (kaydırma) jest durumu ───────────────────────────────────────
    // Sağ yarı dikey = ses, sol yarı dikey = parlaklık, yatay = ileri/geri sarma.
    private bool _isSwiping;
    private bool _swipeIsLeftZone;
    private double _swipeStartY;
    private int _swipeStartVolume;
    private double _swipeStartBrightness;

    private readonly Dictionary<long, Point> _activePointers = new();
    private bool _isPinchZooming;
    private double _pinchStartDistance;
    private double _pinchStartZoom = 1.0;
    private Point _pinchStartCenter;
    private double _currentZoom = 1.0;
    private double _currentPanX;
    private double _currentPanY;

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
        }

        _boundVm = DataContext as PlayerViewModel;
        _lastObservedVolume = _boundVm?.Volume;
        _lastObservedIsMuted = _boundVm?.IsMuted;

        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged += OnPlayerPropertyChanged;
            _boundVm.SkipOverlayRequested += OnSkipOverlayRequested;
            TryShowGestureHintsOnceAsync();
            QueueVideoSurfaceLayoutUpdate();
        }
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
            LockIndicator.Opacity = _boundVm?.IsLockIndicatorVisible == true ? 1 : 0;
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
        if (VideoSurfaceSlot is null)
        {
            return;
        }

        SyncNativeSurfaceTo(VideoSurfaceSlot);
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

    private void SyncNativeSurfaceTo(Control slot)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var topLeft = slot.TranslatePoint(new Point(0, 0), topLevel);
        if (topLeft is null)
        {
            return;
        }

        var scaling = topLevel.RenderScaling;
        var px = (int)Math.Round(topLeft.Value.X * scaling);
        var py = (int)Math.Round(topLeft.Value.Y * scaling);
        var pw = (int)Math.Round(slot.Bounds.Width * scaling);
        var ph = (int)Math.Round(slot.Bounds.Height * scaling);
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
        // Sürükleme (swipe) sırasında tetiklenen sahte tap'leri yoksay.
        if (_isSwiping)
        {
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
        if (_activePointers.Count >= 2)
        {
            BeginPinchZoom();
            e.Handled = true;
            return;
        }

        _isSwiping = true;
        _swipeStartY = point.Position.Y;
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

        if (_isPinchZooming && _activePointers.Count >= 2)
        {
            HandlePinchZoom();
            e.Handled = true;
            return;
        }

        if (!_isSwiping || DataContext is not PlayerViewModel vm)
            return;

        var dy = point.Position.Y - _swipeStartY;

        var height = Bounds.Height > 1 ? Bounds.Height : 1;
        var fraction = -dy / height;

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

        if (_isPinchZooming)
        {
            if (_activePointers.Count < 2)
            {
                _isPinchZooming = false;
                _isSwiping = false;
            }

            e.Handled = true;
            return;
        }

        _isSwiping = false;
    }

    private void BeginPinchZoom()
    {
        var (first, second) = GetFirstTwoPointers();
        _isPinchZooming = true;
        _isSwiping = false;

        _pinchStartDistance = Distance(first, second);
        _pinchStartCenter = Midpoint(first, second);
        _pinchStartZoom = _currentZoom;
    }

    private void HandlePinchZoom()
    {
        var (first, second) = GetFirstTwoPointers();
        var distance = Distance(first, second);
        if (_pinchStartDistance <= 1 || distance <= 1)
        {
            return;
        }

        var center = Midpoint(first, second);
        _currentZoom = Math.Clamp(_pinchStartZoom * (distance / _pinchStartDistance), 1.0, 3.0);

        if (_currentZoom <= 1.001)
        {
            ResetInteractionTransform();
            return;
        }

        _currentPanX += center.X - _pinchStartCenter.X;
        _currentPanY += center.Y - _pinchStartCenter.Y;
        _pinchStartCenter = center;

        GetVideoSurfaceService()?.SetInteractionTransform(
            (float)_currentZoom,
            (float)_currentPanX,
            (float)_currentPanY);
    }

    private void ResetInteractionTransform()
    {
        _currentZoom = 1.0;
        _currentPanX = 0.0;
        _currentPanY = 0.0;
        GetVideoSurfaceService()?.ResetInteractionTransform();
    }

    private (Point First, Point Second) GetFirstTwoPointers()
    {
        using var enumerator = _activePointers.Values.GetEnumerator();
        enumerator.MoveNext();
        var first = enumerator.Current;
        enumerator.MoveNext();
        var second = enumerator.Current;
        return (first, second);
    }

    private static double Distance(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Point Midpoint(Point first, Point second)
        => new((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0);

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

    // ── Kilit göstergesi uzun basma (500 ms) ─────────────────────────────────

    private void OnLockIndicatorPressed(object? sender, PointerPressedEventArgs e)
    {
        _lockPressCts?.Cancel();
        _lockPressCts = new CancellationTokenSource();
        var cts = _lockPressCts;

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(LockLongPressMs)
        };
        timer.Tick += (s, args) =>
        {
            timer.Stop();
            if (!cts.IsCancellationRequested && DataContext is PlayerViewModel vm)
                vm.Unlock();
        };
        timer.Start();
    }

    private void OnLockIndicatorReleased(object? sender, PointerReleasedEventArgs e)
    {
        _lockPressCts?.Cancel();
    }
}
