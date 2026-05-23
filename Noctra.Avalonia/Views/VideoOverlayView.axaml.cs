using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class VideoOverlayView : UserControl
{
    public static readonly StyledProperty<bool> IsVolumeToastVisibleProperty =
        AvaloniaProperty.Register<VideoOverlayView, bool>(nameof(IsVolumeToastVisible));
    public static readonly StyledProperty<bool> IsSeekToastVisibleProperty =
        AvaloniaProperty.Register<VideoOverlayView, bool>(nameof(IsSeekToastVisible));
    public static readonly StyledProperty<bool> IsDownloadToastVisibleProperty =
        AvaloniaProperty.Register<VideoOverlayView, bool>(nameof(IsDownloadToastVisible));
    public static readonly StyledProperty<string> SeekToastTextProperty =
        AvaloniaProperty.Register<VideoOverlayView, string>(nameof(SeekToastText), "+0:10");

    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor VisibleCursor = new(StandardCursorType.Arrow);

    private readonly DispatcherTimer _volumeToastTimer;
    private readonly DispatcherTimer _seekToastTimer;
    private readonly DispatcherTimer _downloadToastTimer;
    private PlayerViewModel? _playerViewModel;
    private bool _isTimelinePointerDown;
    private bool _isCommittingSeek;

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

    public string SeekToastText
    {
        get => GetValue(SeekToastTextProperty);
        set => SetValue(SeekToastTextProperty, value);
    }

    public VideoOverlayView()
    {
        InitializeComponent();

        _volumeToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        _volumeToastTimer.Tick += (_, _) =>
        {
            IsVolumeToastVisible = false;
            _volumeToastTimer.Stop();
        };

        _seekToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        _seekToastTimer.Tick += (_, _) =>
        {
            IsSeekToastVisible = false;
            _seekToastTimer.Stop();
        };

        _downloadToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(3000)
        };
        _downloadToastTimer.Tick += (_, _) =>
        {
            IsDownloadToastVisible = false;
            _downloadToastTimer.Stop();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Subscribe to slider events explicitly to handle bubbled/tunnelled events correctly
        var slider = this.FindControl<Slider>("TimelineSlider");
        if (slider != null)
        {
            // Capture the start of interaction eagerly (Tunnel) or even if handled (Bubble)
            slider.AddHandler(PointerPressedEvent, TimelineSlider_PointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            slider.AddHandler(PointerReleasedEvent, TimelineSlider_PointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            slider.AddHandler(PointerCaptureLostEvent, TimelineSlider_PointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
            
            // Block scrolling from timeline to prevent unintended rapid seeking
            slider.AddHandler(InputElement.PointerWheelChangedEvent, Slider_PointerWheelChanged_Tunnel, RoutingStrategies.Tunnel);
        }

        var volumeSlider = this.FindControl<Slider>("VolumeSlider");
        if (volumeSlider != null)
        {
            // Block scrolling from volume slider to prevent volume toast spam
            volumeSlider.AddHandler(InputElement.PointerWheelChangedEvent, Slider_PointerWheelChanged_Tunnel, RoutingStrategies.Tunnel);
        }
    }

    private void Slider_PointerWheelChanged_Tunnel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
    }

    private void TimelineSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _playerViewModel?.LogDebug("UI Action: TimelineSlider PointerPressed");
        _isTimelinePointerDown = true;
        _playerViewModel?.StartSeekingCommand.Execute(null);
    }

    private void TimelineSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CommitSeek(sender);
    }

    private void TimelineSlider_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CommitSeek(sender);
    }

    private void CommitSeek(object? sender)
    {
        if (!_isTimelinePointerDown || _playerViewModel == null)
        {
            return;
        }

        if (_isCommittingSeek) return; // double-fire koruması

        _playerViewModel?.LogDebug("UI Action: CommitSeek triggered");
        _isTimelinePointerDown = false;
        _isCommittingSeek = true;

        try
        {
            // Robust check: ensure ViewModel is not null before accessing its commands
            if (_playerViewModel == null || _playerViewModel.IsLiveContent || sender is not Slider slider)
            {
                return;
            }

            _playerViewModel.SeekCommand.Execute(slider.Value);
            _playerViewModel.UserInteractionCommand.Execute(null);
        }
        finally
        {
            _isCommittingSeek = false;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_playerViewModel != null)
        {
            _playerViewModel.PropertyChanged -= PlayerViewModel_PropertyChanged;
            _playerViewModel.SkipOverlayRequested -= PlayerViewModel_SkipOverlayRequested;
            _playerViewModel.PropertyChanged -= PlayerViewModel_EpgPropertyChanged;
        }

        _playerViewModel = DataContext as PlayerViewModel;
        if (_playerViewModel != null)
        {
            _isInitialVolumeSet = false;
            _playerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;
            _playerViewModel.SkipOverlayRequested += PlayerViewModel_SkipOverlayRequested;
            _playerViewModel.PropertyChanged += PlayerViewModel_EpgPropertyChanged;
            UpdateOverlayCursor(_playerViewModel.IsVisible);
        }
        else
        {
            Cursor = VisibleCursor;
            IsSeekToastVisible = false;
        }
    }

    /// <summary>
    /// EPG paneli açıldığında saat başlıklarını ve zaman penceresini günceller.
    /// </summary>
    private void PlayerViewModel_EpgPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsEpgPanelOpen)
            && _playerViewModel?.IsEpgPanelOpen == true)
        {
            InitializeEpgTimeHeader();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.EpgFocusRowIndex)
                 && _playerViewModel?.IsEpgPanelOpen == true)
        {
            QueueFocusCurrentEpgRow();
        }
    }

    private void InitializeEpgTimeHeader()
    {
        // Saat etiketlerini hesapla (pencere: now-2h → now+4h)
        var now = DateTime.Now;
        BuildEpgTimeHeader(now);
        var labels = new[]
        {
            this.FindControl<TextBlock>("EpgH_Minus2"),
            this.FindControl<TextBlock>("EpgH_Minus1"),
            this.FindControl<TextBlock>("EpgH_Now"),
            this.FindControl<TextBlock>("EpgH_Plus1"),
            this.FindControl<TextBlock>("EpgH_Plus2"),
            this.FindControl<TextBlock>("EpgH_Plus3"),
        };

        var offsets = new[] { -2, -1, 0, 1, 2, 3 };
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] == null) continue;
            var t = now.AddHours(offsets[i]);
            labels[i]!.Text = t.ToString("HH:mm");
        }

        // Zaman penceresi etiketini güncelle
        var windowLabel = this.FindControl<TextBlock>("EpgTimeWindowLabel");
        if (windowLabel != null)
        {
            var start = now.AddHours(-PlayerViewModel.EpgPastHours).ToString("HH:mm");
            var end   = now.AddHours( PlayerViewModel.EpgFutureHours).ToString("HH:mm");
            windowLabel.Text = $"{start} – {end}";
        }

        QueueFocusCurrentEpgRow();
    }

    private void BuildEpgTimeHeader(DateTime now)
    {
        var canvas = this.FindControl<Canvas>("EpgTimeHeaderCanvas");
        if (canvas == null)
            return;

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
                Height = 40,
                Background = minute % 60 == 0 ? lineBrush : halfLineBrush
            };
            Canvas.SetLeft(line, minute * PlayerViewModel.EpgPxPerMinute);
            canvas.Children.Add(line);
        }

        for (var hour = -(int)PlayerViewModel.EpgPastHours; hour <= (int)PlayerViewModel.EpgFutureHours; hour++)
        {
            if (hour == 0)
                continue;

            var label = new TextBlock
            {
                Text = now.AddHours(hour).ToString("HH:mm"),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#80FFFFFF"))
            };
            Canvas.SetLeft(label, (hour + PlayerViewModel.EpgPastHours) * 60 * PlayerViewModel.EpgPxPerMinute + 4);
            Canvas.SetTop(label, 16);
            canvas.Children.Add(label);
        }

        var nowLine = new Border
        {
            Width = 1.5,
            Height = 40,
            Background = nowBrush,
            ZIndex = 10
        };
        Canvas.SetLeft(nowLine, PlayerViewModel.EpgNowPixelPos);
        canvas.Children.Add(nowLine);

        var nowBadge = new Border
        {
            Width = 26,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            Background = accentBrush,
            ZIndex = 11,
            Child = new TextBlock
            {
                                Text = "SIMDI",
                FontSize = 7,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(nowBadge, PlayerViewModel.EpgNowPixelPos - 13);
        Canvas.SetTop(nowBadge, 5);
        canvas.Children.Add(nowBadge);
    }

    private void QueueFocusCurrentEpgRow()
    {
        Dispatcher.UIThread.Post(
            FocusCurrentEpgRow,
            global::Avalonia.Threading.DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(
            FocusCurrentEpgRow,
            global::Avalonia.Threading.DispatcherPriority.Background);
    }

    private void FocusCurrentEpgRow()
    {
        var timelineScroll = this.FindControl<ScrollViewer>("EpgTimelineScroll");
        if (timelineScroll == null)
            return;

        var targetX = Math.Max(0, PlayerViewModel.EpgNowPixelPos - timelineScroll.Viewport.Width / 2);
        var targetY = timelineScroll.Offset.Y;

        if (_playerViewModel?.EpgFocusRowIndex >= 0)
        {
            const double rowHeight = 68;
            targetY = Math.Max(0, _playerViewModel.EpgFocusRowIndex * rowHeight - timelineScroll.Viewport.Height / 2 + rowHeight / 2);
        }

        timelineScroll.Offset = new global::Avalonia.Vector(targetX, targetY);

        var timeHeader = this.FindControl<ScrollViewer>("EpgTimeHeaderScroll");
        if (timeHeader != null)
            timeHeader.Offset = new global::Avalonia.Vector(targetX, 0);

        var namesScroll = this.FindControl<ScrollViewer>("EpgNamesScroll");
        if (namesScroll != null)
            namesScroll.Offset = new global::Avalonia.Vector(0, targetY);
    }

    private void OverlayRoot_PointerMoved(object? sender, PointerEventArgs e)
    {
        _playerViewModel?.UserInteractionCommand.Execute(null);
    }

    private void TimelineSlider_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (_playerViewModel == null || _playerViewModel.IsLiveContent || _playerViewModel.Duration <= 0)
            return;

        HoverTimePopup.IsOpen = true;
    }

    private void TimelineSlider_PointerExited(object? sender, PointerEventArgs e)
    {
        HoverTimePopup.IsOpen = false;
    }

    private void TimelineSlider_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_playerViewModel == null || _playerViewModel.IsLiveContent || _playerViewModel.Duration <= 0 || !HoverTimePopup.IsOpen)
            return;

        var slider = TimelineSlider;
        var pointerPos = e.GetPosition(slider);
        
        // Accurate calculation using Track if possible
        // Avalonia Slider uses a Track inside its template to map values.
        // The Track might have margins (e.g. to fit the Thumb).
        var track = slider.GetVisualDescendants().OfType<Track>().FirstOrDefault();
        double hoverTimeSeconds;

        if (track != null && track.Bounds.Width > 0)
        {
            var trackPos = e.GetPosition(track);
            hoverTimeSeconds = track.ValueFromPoint(trackPos);
        }
        else
        {
            var width = slider.Bounds.Width;
            if (width <= 0) return;
            var percent = Math.Clamp(pointerPos.X / width, 0, 1);
            hoverTimeSeconds = percent * _playerViewModel.Duration;
        }
        
        // Format time (00:00 or 0:00:00)
        var timeSpan = TimeSpan.FromSeconds(hoverTimeSeconds);
        HoverTimeText.Text = timeSpan.TotalHours >= 1 
            ? timeSpan.ToString(@"h\:mm\:ss") 
            : timeSpan.ToString(@"m\:ss");

        // Position popup centered above pointer
        var center = slider.Bounds.Width / 2;
        HoverTimePopup.HorizontalOffset = pointerPos.X - center;
    }

    private void OverlayRoot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_playerViewModel == null)
        {
            return;
        }

        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _playerViewModel.ToggleFullScreenCommand.Execute(null);
            return;
        }

        _playerViewModel.UserInteractionCommand.Execute(null);
    }

    private void OverlayRoot_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Block mouse/trackpad scrolling from reaching the Volume slider natively.
        // The default Slider control captures scroll events and changes volume rapidly,
        // causing severe toast notification spam (e.g. 50 times a second).
        e.Handled = true;
    }

    private void OverlayRoot_KeyDown(object? sender, KeyEventArgs e)
    {
        // MainWindow_KeyDown (Tunnel) zaten işlediyse tekrar işleme
        if (e.Handled || _playerViewModel == null)
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
                else
                {
                    _playerViewModel.PlayPreviousLiveChannelCommand.Execute(null);
                    e.Handled = true;
                }
                break;
            case Key.Right:
                if (!_playerViewModel.IsLiveContent)
                {
                    _playerViewModel.SkipForwardCommand.Execute(10);
                    e.Handled = true;
                }
                else
                {
                    _playerViewModel.PlayNextLiveChannelCommand.Execute(null);
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
    private bool _isInitialVolumeSet;
    private DateTime _lastVolumeToastShownUtc = DateTime.MinValue;
    private static readonly TimeSpan VolumeToastThrottleInterval = TimeSpan.FromMilliseconds(400);

    private void ShowVolumeToast()
    {
        var now = DateTime.UtcNow;
        if (now - _lastVolumeToastShownUtc < VolumeToastThrottleInterval)
        {
            return;
        }
        _lastVolumeToastShownUtc = now;

        _playerViewModel?.LogDebug("UI State: Volume Toast visible");
        IsVolumeToastVisible = true;
        _volumeToastTimer.Stop();
        _volumeToastTimer.Start();
    }

    private void PlayerViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.Volume) or nameof(PlayerViewModel.IsMuted))
        {
            if (!_isInitialVolumeSet)
            {
                _isInitialVolumeSet = true;
                return;
            }
            ShowVolumeToast();
        }

        if (e.PropertyName == nameof(PlayerViewModel.IsVisible))
        {
            UpdateOverlayCursor(_playerViewModel?.IsVisible == true);
        }

        if (e.PropertyName == nameof(PlayerViewModel.DownloadStatusMessage))
        {
            if (!string.IsNullOrEmpty(_playerViewModel?.DownloadStatusMessage))
            {
                ShowDownloadToast();
            }
        }
    }



    private void PlayerViewModel_SkipOverlayRequested(object? sender, PlayerViewModel.SkipOverlayEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SeekToastText = FormatSkipToast(e.Seconds);
            ShowSeekToast();
        });
    }

    private void ShowSeekToast()
    {
        _playerViewModel?.LogDebug($"UI State: Seek Toast visible ({SeekToastText})");
        IsSeekToastVisible = true;
        _seekToastTimer.Stop();
        _seekToastTimer.Start();
    }

    private void ShowDownloadToast()
    {
        _playerViewModel?.LogDebug($"UI State: Download Toast visible ({_playerViewModel?.DownloadStatusMessage})");
        IsDownloadToastVisible = true;
        _downloadToastTimer.Stop();
        _downloadToastTimer.Start();
    }

    private void AudioTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (_playerViewModel == null || sender is not Button button)
        {
            return;
        }

        if (TryGetIntFromTag(button.Tag, out var id))
        {
            _playerViewModel.SetAudioTrackCommand.Execute(id);
        }
    }

    private void SubtitleTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (_playerViewModel == null || sender is not Button button)
        {
            return;
        }

        if (TryGetIntFromTag(button.Tag, out var id))
        {
            _playerViewModel.SetSubtitleTrackCommand.Execute(id);
        }
    }

    private static bool TryGetIntFromTag(object? tag, out int value)
    {
        switch (tag)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = (int)longValue;
                return true;
            case string str when int.TryParse(str, out var parsed):
                value = parsed;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static string FormatSkipToast(double seconds)
    {
        var sign = seconds >= 0 ? "+" : "-";
        var absDuration = TimeSpan.FromSeconds(Math.Abs(seconds));
        var formatted = absDuration.TotalHours >= 1
            ? absDuration.ToString(@"h\:mm\:ss")
            : absDuration.ToString(@"m\:ss");
        return $"{sign}{formatted}";
    }

    private void UpdateOverlayCursor(bool isOverlayVisible)
    {
        var cursor = isOverlayVisible ? VisibleCursor : HiddenCursor;
        Cursor = cursor;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            topLevel.Cursor = cursor;
        }
    }

    private void PlayPreviousLiveChannel_Click(object? sender, RoutedEventArgs e)
    {
        if (_playerViewModel != null && _playerViewModel.IsLiveContent)
        {
            _playerViewModel.PlayPreviousLiveChannelCommand.Execute(null);
            _playerViewModel.UserInteractionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PlayNextLiveChannel_Click(object? sender, RoutedEventArgs e)
    {
        if (_playerViewModel != null && _playerViewModel.IsLiveContent)
        {
            _playerViewModel.PlayNextLiveChannelCommand.Execute(null);
            _playerViewModel.UserInteractionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void EpisodeCardBtn_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Stop pointer pressed from bubbling up to the Expander,
        // which prevents the Expander header from incorrectly toggling
        // when an episode card is clicked.
        e.Handled = true;
    }

    // ── EPG Panel ──────────────────────────────────────────────────────────

    /// <summary>
    /// EPG timeline kanal satırına tıklandığında çağrılır.
    /// DataContext içindeki EpgPanelRow.Channel bilgisine göre MainViewModel'ı tetikler.
    /// </summary>
    private void EpgChannelRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
            return;

        if (sender is Control { DataContext: Noctra.ViewModels.EpgPanelRow row })
        {
            _playerViewModel?.ToggleEpgPanelCommand.Execute(null); // paneli kapat
            // MainWindow handler'ına yönlendir
            RaiseEvent(new EpgChannelSelectedRoutedEventArgs(EpgChannelSelectedEvent, row.Channel));
            e.Handled = true;
        }
    }

    /// <summary>
    /// EPG horizontal scroll (timeline) değişince time-header scroll'unu senkronize eder.
    /// </summary>
    private void EpgTimelineScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var timeHeader = this.FindControl<ScrollViewer>("EpgTimeHeaderScroll");
        var namesScroll = this.FindControl<ScrollViewer>("EpgNamesScroll");
        var timelineScroll = this.FindControl<ScrollViewer>("EpgTimelineScroll");

        if (timelineScroll == null) return;

        if (timeHeader != null)
            timeHeader.Offset = new global::Avalonia.Vector(timelineScroll.Offset.X, 0);

        if (namesScroll != null)
            namesScroll.Offset = new global::Avalonia.Vector(0, timelineScroll.Offset.Y);
    }
}

// ── EPG Routed Event ───────────────────────────────────────────────────────

public class EpgChannelSelectedRoutedEventArgs : global::Avalonia.Interactivity.RoutedEventArgs
{
    public Noctra.Models.Channel Channel { get; }
    public EpgChannelSelectedRoutedEventArgs(
        global::Avalonia.Interactivity.RoutedEvent @event,
        Noctra.Models.Channel channel)
        : base(@event)
    {
        Channel = channel;
    }
}

public partial class VideoOverlayView
{
    public static readonly global::Avalonia.Interactivity.RoutedEvent<EpgChannelSelectedRoutedEventArgs> EpgChannelSelectedEvent =
        global::Avalonia.Interactivity.RoutedEvent.Register<VideoOverlayView, EpgChannelSelectedRoutedEventArgs>(
            "EpgChannelSelected", global::Avalonia.Interactivity.RoutingStrategies.Bubble);

    public event EventHandler<EpgChannelSelectedRoutedEventArgs>? EpgChannelSelected
    {
        add    => AddHandler(EpgChannelSelectedEvent, value);
        remove => RemoveHandler(EpgChannelSelectedEvent, value);
    }
}
