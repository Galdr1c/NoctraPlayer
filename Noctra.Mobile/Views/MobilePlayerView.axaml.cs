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
    // Sağ yarı dikey = ses, sol yarı dikey = parlaklık, yatay = ileri/geri sarma.
    private const double SwipeThreshold = 14;   // yön kararı için minimum hareket (px)
    private bool _isSwiping;
    private bool _swipeDirectionDecided;
    private bool _swipeIsVertical;
    private bool _swipeIsLeftZone;
    private Point _swipeStart;
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
    private double _horizontalSeekDeltaSeconds;
    private bool _isHorizontalSeekPreviewActive;

    private readonly DispatcherTimer _volumeToastTimer;
    private readonly DispatcherTimer _seekToastTimer;
    private readonly DispatcherTimer _downloadToastTimer;
    private readonly DispatcherTimer _gestureToastTimer;
    private readonly DispatcherTimer _singleTapTimer;
    private bool _isInitialVolumeEvent = true;
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged -= OnPlayerPropertyChanged;
            _boundVm.SkipOverlayRequested -= OnSkipOverlayRequested;
        }

        _boundVm = DataContext as PlayerViewModel;
        _isInitialVolumeEvent = true;

        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged += OnPlayerPropertyChanged;
            _boundVm.SkipOverlayRequested += OnSkipOverlayRequested;
            TryShowGestureHintsOnceAsync();
        }
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.Volume) or nameof(PlayerViewModel.IsMuted))
        {
            ShowVolumeToast();
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

        if (e.PropertyName == nameof(PlayerViewModel.IsEpgPanelOpen))
        {
            if (_boundVm?.IsEpgPanelOpen == true)
            {
                InitializeEpgTimelineHeader();
                UpdateEpgVideoLayout();
            }
            else
            {
                // EPG kapandı -> video tekrar tam ekran.
                _lastSurfaceRect = default;
                GetVideoSurfaceService()?.SetBounds(0, 0, 0, 0);
            }

            return;
        }

        if (e.PropertyName == nameof(PlayerViewModel.EpgFocusRowIndex)
            && _boundVm?.IsEpgPanelOpen == true)
        {
            QueueFocusCurrentEpgRow();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // EPG açıkken (rotasyon/boyut değişiminde) video slotunu native yüzeyle senkron tut.
        if (_boundVm?.IsEpgPanelOpen == true)
        {
            UpdateEpgVideoLayout();
        }
    }

    /// <summary>
    /// EPG split görünümünde üstteki şeffaf VideoSlot'un ekran (piksel) dikdörtgenini
    /// hesaplar ve native video yüzeyini oraya küçültür. Böylece masaüstündeki
    /// "video üstüne yarı saydam panel" yerine mobilde "video üstte küçülür, EPG altta" olur.
    /// </summary>
    private void UpdateEpgVideoLayout()
    {
        if (VideoSlot is null)
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
        if (Math.Abs(VideoSlot.Height - desiredHeight) > 0.5)
        {
            VideoSlot.Height = desiredHeight;
            return; // yükseklik değişti; yeni layout pass UpdateEpgVideoLayout'u tekrar tetikler
        }

        // VideoSlot'un pencereye göre konumunu al, piksel ölçeğine çevir.
        var topLeft = VideoSlot.TranslatePoint(new Point(0, 0), topLevel);
        if (topLeft is null)
        {
            return;
        }

        var scaling = topLevel.RenderScaling;
        var px = (int)Math.Round(topLeft.Value.X * scaling);
        var py = (int)Math.Round(topLeft.Value.Y * scaling);
        var pw = (int)Math.Round(VideoSlot.Bounds.Width * scaling);
        var ph = (int)Math.Round(VideoSlot.Bounds.Height * scaling);
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
        if (_isInitialVolumeEvent)
        {
            _isInitialVolumeEvent = false;
            return;
        }

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

    /// <summary>
    /// Desktop EPG'deki saat başlığı / zaman penceresi mantığını mobile timeline'a uygular.
    /// </summary>
    private void InitializeEpgTimelineHeader()
    {
        var now = DateTime.Now;
        BuildEpgTimeHeader(now);

        var windowLabel = this.FindControl<TextBlock>("EpgTimeWindowLabel");
        if (windowLabel is not null)
        {
            var start = now.AddHours(-PlayerViewModel.EpgPastHours).ToString("HH:mm");
            var end = now.AddHours(PlayerViewModel.EpgFutureHours).ToString("HH:mm");
            windowLabel.Text = $"{start} – {end}";
        }

        QueueFocusCurrentEpgRow();
    }

    private void BuildEpgTimeHeader(DateTime now)
    {
        var canvas = this.FindControl<Canvas>("EpgTimeHeaderCanvas");
        if (canvas is null)
        {
            return;
        }

        canvas.Children.Clear();

        var lineBrush = new SolidColorBrush(Color.Parse("#33FFFFFF"));
        var halfLineBrush = new SolidColorBrush(Color.Parse("#1AFFFFFF"));
        var nowBrush = new SolidColorBrush(Color.Parse("#CC7B2FBE"));
        var accentBrush = new SolidColorBrush(Color.Parse("#7B2FBE"));
        var totalMinutes = (PlayerViewModel.EpgPastHours + PlayerViewModel.EpgFutureHours) * 60;

        for (var minute = 30; minute < totalMinutes; minute += 30)
        {
            var line = new Border
            {
                Width = 1,
                Height = 36,
                Background = minute % 60 == 0 ? lineBrush : halfLineBrush
            };
            Canvas.SetLeft(line, minute * PlayerViewModel.EpgPxPerMinute);
            canvas.Children.Add(line);
        }

        for (var hour = -(int)PlayerViewModel.EpgPastHours; hour <= (int)PlayerViewModel.EpgFutureHours; hour++)
        {
            if (hour == 0)
            {
                continue;
            }

            var label = new TextBlock
            {
                Text = now.AddHours(hour).ToString("HH:mm"),
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#80FFFFFF"))
            };
            Canvas.SetLeft(label, (hour + PlayerViewModel.EpgPastHours) * 60 * PlayerViewModel.EpgPxPerMinute + 4);
            Canvas.SetTop(label, 14);
            canvas.Children.Add(label);
        }

        var nowLine = new Border
        {
            Width = 1.5,
            Height = 36,
            Background = nowBrush,
            ZIndex = 10
        };
        Canvas.SetLeft(nowLine, PlayerViewModel.EpgNowPixelPos);
        canvas.Children.Add(nowLine);

        var nowLabel = new TextBlock
        {
            FontSize = 7,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        nowLabel.Bind(TextBlock.TextProperty, new Binding("[Player.Epg.Now]")
        {
            Source = LocalizationSource.Instance
        });

        var nowBadge = new Border
        {
            Width = 26,
            Height = 17,
            CornerRadius = new CornerRadius(4),
            Background = accentBrush,
            ZIndex = 11,
            Child = nowLabel
        };
        Canvas.SetLeft(nowBadge, PlayerViewModel.EpgNowPixelPos - 13);
        Canvas.SetTop(nowBadge, 5);
        canvas.Children.Add(nowBadge);
    }

    private void QueueFocusCurrentEpgRow()
    {
        Dispatcher.UIThread.Post(FocusCurrentEpgRow, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(FocusCurrentEpgRow, DispatcherPriority.Background);
    }

    private void FocusCurrentEpgRow()
    {
        var timelineScroll = this.FindControl<ScrollViewer>("EpgTimelineScroll");
        if (timelineScroll is null)
        {
            return;
        }

        var targetX = Math.Max(0, PlayerViewModel.EpgNowPixelPos - timelineScroll.Viewport.Width / 2);
        var targetY = timelineScroll.Offset.Y;

        if (_boundVm?.EpgFocusRowIndex >= 0)
        {
            const double rowHeight = 60;
            targetY = Math.Max(0, _boundVm.EpgFocusRowIndex * rowHeight - timelineScroll.Viewport.Height / 2 + rowHeight / 2);
        }

        timelineScroll.Offset = new Vector(targetX, targetY);

        var timeHeader = this.FindControl<ScrollViewer>("EpgTimeHeaderScroll");
        if (timeHeader is not null)
        {
            timeHeader.Offset = new Vector(targetX, 0);
        }

        var namesScroll = this.FindControl<ScrollViewer>("EpgNamesScroll");
        if (namesScroll is not null)
        {
            namesScroll.Offset = new Vector(0, targetY);
        }
    }

    /// <summary>
    /// Timeline scroll değişince üst saat başlığını ve soldaki frozen kanal listesini senkron tutar.
    /// </summary>
    private void EpgTimelineScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer timelineScroll)
        {
            return;
        }

        var timeHeader = this.FindControl<ScrollViewer>("EpgTimeHeaderScroll");
        if (timeHeader is not null)
        {
            timeHeader.Offset = new Vector(timelineScroll.Offset.X, 0);
        }

        var namesScroll = this.FindControl<ScrollViewer>("EpgNamesScroll");
        if (namesScroll is not null)
        {
            namesScroll.Offset = new Vector(0, timelineScroll.Offset.Y);
        }
    }

    /// <summary>
    /// EPG timeline kanal satırına tıklandığında çağrılır.
    /// Seçilen kanalı ChannelSelected event'i ile iletir, EPG panelini kapatır.
    /// </summary>
    private void EpgRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
            return;

        if (sender is Control control && control.DataContext is EpgPanelRow row)
        {
            // EPG panelini kapat
            if (DataContext is PlayerViewModel playerVm)
            {
                playerVm.ToggleEpgPanelCommand.Execute(null);
            }

            // Kanal seçim event'ini fırlat
            ChannelSelected?.Invoke(row.Channel);
            e.Handled = true;
        }
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

        // Kilitliyken jestler devre dışı; kullanıcıya sessiz kalma.
        if (vm.IsLocked)
        {
            ShowGestureToast(TranslateOrDefault("Player.Mobile.Toast.Locked", "Kontroller kilitli"));
            return;
        }

        var point = e.GetCurrentPoint(this);
        _activePointers[point.Pointer.Id] = point.Position;
        if (_activePointers.Count >= 2)
        {
            BeginPinchZoom();
            e.Handled = true;
            return;
        }

        _isSwiping = true;
        _swipeDirectionDecided = false;
        _swipeIsVertical = false;
        _swipeStart = point.Position;
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

        var pos = point.Position;
        var dx = pos.X - _swipeStart.X;
        var dy = pos.Y - _swipeStart.Y;

        if (!_swipeDirectionDecided)
        {
            if (Math.Abs(dx) < SwipeThreshold && Math.Abs(dy) < SwipeThreshold)
                return;
            _swipeIsVertical = Math.Abs(dy) >= Math.Abs(dx);
            _swipeDirectionDecided = true;
        }

        if (!_swipeIsVertical)
        {
            // Yatay sarma önizlemesi: sürükleme sırasında hedef delta'yı canlı göster.
            // Asıl seek parmak kalkınca uygulanır (tek sıçrama), ama kullanıcı ışıklı feedback alır.
            if (!vm.IsLiveContent)
            {
                ShowHorizontalSeekPreview(vm, dx);
            }
            return;
        }

        // Dikey jest başladıysa bekleyen yatay seek önizlemesini temizle.
        HideHorizontalSeekPreview();

        var height = Bounds.Height > 1 ? Bounds.Height : 1;
        var fraction = -dy / height; // yukarı kaydırma = artış

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
                _swipeDirectionDecided = false;
            }

            e.Handled = true;
            return;
        }

        if (!_isSwiping || DataContext is not PlayerViewModel vm)
        {
            _isSwiping = false;
            return;
        }

        var pos = point.Position;
        var dx = pos.X - _swipeStart.X;

        // Yatay kaydırma -> ileri/geri sarma. Canlı yayında seek yok; sessiz no-op yerine açık feedback ver.
        if (_swipeDirectionDecided && !_swipeIsVertical && Math.Abs(dx) >= SwipeThreshold)
        {
            if (vm.IsLiveContent)
            {
                ShowGestureToast(TranslateOrDefault("Player.Mobile.Toast.LiveSeekUnavailable", "Canlı yayında ileri/geri sarma kullanılamaz"));
                e.Handled = true;
            }
            else
            {
                var seconds = (int)Math.Clamp(Math.Abs(dx) / 6.0, 5, 90);
                var param = seconds.ToString(CultureInfo.InvariantCulture);

                if (dx > 0)
                {
                    SeekToastIcon = MaterialIconKind.FastForward10;
                    if (vm.SkipForwardCommand.CanExecute(param))
                        vm.SkipForwardCommand.Execute(param);
                }
                else if (vm.SkipBackwardCommand.CanExecute(param))
                {
                    SeekToastIcon = MaterialIconKind.Rewind10;
                    vm.SkipBackwardCommand.Execute(param);
                }

                e.Handled = true;
            }
        }

        // Sürükleme bitti — önizleme toast'unu temizle.
        _isHorizontalSeekPreviewActive = false;
        _horizontalSeekDeltaSeconds = 0;
        _isSwiping = false;
        _swipeDirectionDecided = false;
    }

    private void BeginPinchZoom()
    {
        var (first, second) = GetFirstTwoPointers();
        _isPinchZooming = true;
        _isSwiping = false;
        _swipeDirectionDecided = false;
        HideHorizontalSeekPreview();

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
    private void ShowHorizontalSeekPreview(PlayerViewModel vm, double dx)
    {
        if (Math.Abs(dx) < SwipeThreshold)
        {
            return;
        }

        var seconds = (int)Math.Clamp(Math.Abs(dx) / 6.0, 5, 90);
        _horizontalSeekDeltaSeconds = dx > 0 ? seconds : -seconds;
        _isHorizontalSeekPreviewActive = true;

        SeekToastIcon = dx > 0 ? MaterialIconKind.FastForward10 : MaterialIconKind.Rewind10;
        SeekToastText = FormatSeekToast(_horizontalSeekDeltaSeconds);

        IsSeekToastVisible = true;
        // Önizleme sürdüğü sürece gizleme sayacı çalışmasın.
        _seekToastTimer.Stop();
    }

    private void HideHorizontalSeekPreview()
    {
        if (!_isHorizontalSeekPreviewActive)
        {
            return;
        }

        _isHorizontalSeekPreviewActive = false;
        _horizontalSeekDeltaSeconds = 0;
        IsSeekToastVisible = false;
    }

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
}
