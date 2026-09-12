using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctra.Avalonia.Localization;
using Noctra.Avalonia.ViewModels;
using Noctra.Core.Services;
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
    public static readonly StyledProperty<DesktopEpgGuidePresentation> DesktopEpgProperty =
        AvaloniaProperty.Register<VideoOverlayView, DesktopEpgGuidePresentation>(
            nameof(DesktopEpg),
            DesktopEpgGuidePresentation.CreateWindow(DateTime.Now));

    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor VisibleCursor = new(StandardCursorType.Arrow);

    private readonly DispatcherTimer _volumeToastTimer;
    private readonly DispatcherTimer _seekToastTimer;
    private readonly DispatcherTimer _downloadToastTimer;
    private PlayerViewModel? _playerViewModel;
    private bool _isLocalizationSubscribed;
    private DateTime _lastPointerInteractionUtc = DateTime.MinValue;
    private static readonly TimeSpan PointerInteractionThrottle = TimeSpan.FromMilliseconds(100);

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

    public DesktopEpgGuidePresentation DesktopEpg
    {
        get => GetValue(DesktopEpgProperty);
        private set => SetValue(DesktopEpgProperty, value);
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
        if (!_isLocalizationSubscribed)
        {
            LocalizationSource.Instance.PropertyChanged += LocalizationSource_PropertyChanged;
            _isLocalizationSubscribed = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_isLocalizationSubscribed)
        {
            LocalizationSource.Instance.PropertyChanged -= LocalizationSource_PropertyChanged;
            _isLocalizationSubscribed = false;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void LocalizationSource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_playerViewModel?.IsEpgPanelOpen == true)
        {
            UpdateDesktopEpgDateLabel();
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
            _ = OpenDesktopEpgAsync();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.EpgGuideState)
                 && _playerViewModel?.IsEpgPanelOpen == true
                 && !_playerViewModel.IsEpgLoading)
        {
            RebuildDesktopEpg();
            QueueFocusCurrentEpgRow();
        }
    }

    private async Task OpenDesktopEpgAsync()
    {
        if (_playerViewModel == null)
            return;

        var window = DesktopEpgGuidePresentation.CreateWindow(DateTime.Now);
        DesktopEpg = window;
        BuildEpgTimeHeader();

        await _playerViewModel.LoadEpgPanelAsync(window.WindowStart, window.WindowEnd);
        if (!_playerViewModel.IsEpgPanelOpen)
            return;

        RebuildDesktopEpg();
        QueueFocusCurrentEpgRow();
    }

    private void RebuildDesktopEpg()
    {
        if (_playerViewModel == null)
            return;

        DesktopEpg = DesktopEpgGuidePresentation.Build(
            _playerViewModel.EpgRows,
            _playerViewModel.CurrentChannel,
            DateTime.Now);
        BuildEpgTimeHeader();
    }

    private void BuildEpgTimeHeader()
    {
        var canvas = this.FindControl<Canvas>("EpgTimeHeaderCanvas");
        if (canvas == null)
            return;

        canvas.Children.Clear();

        var lineBrush = new SolidColorBrush(Color.Parse("#33FFFFFF"));
        var halfLineBrush = new SolidColorBrush(Color.Parse("#1AFFFFFF"));
        var nowBrush = new SolidColorBrush(Color.Parse("#CC7B2FBE"));
        var accentBrush = new SolidColorBrush(Color.Parse("#7B2FBE"));
        var totalMinutes = (int)(DesktopEpg.WindowEnd - DesktopEpg.WindowStart).TotalMinutes;

        UpdateDesktopEpgDateLabel();

        for (var minute = 0; minute <= totalMinutes; minute += 30)
        {
            if (minute > 0 && minute < totalMinutes)
            {
                var tickTime = DesktopEpg.WindowStart.AddMinutes(minute);
                var line = new Border
                {
                    Width = 1,
                    Height = 40,
                    Background = tickTime.Minute == 0 ? lineBrush : halfLineBrush
                };
                Canvas.SetLeft(line, minute * DesktopEpgGuidePresentation.PixelsPerMinute);
                canvas.Children.Add(line);
            }

            var labelTime = DesktopEpg.WindowStart.AddMinutes(minute);
            if (labelTime.Minute != 0)
                continue;

            var label = new TextBlock
            {
                Text = labelTime.ToString("HH:mm"),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#80FFFFFF"))
            };
            Canvas.SetLeft(label, minute * DesktopEpgGuidePresentation.PixelsPerMinute + 4);
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
        Canvas.SetLeft(nowLine, DesktopEpg.NowLineLeft);
        canvas.Children.Add(nowLine);

        var nowLabel = new TextBlock
        {
            FontSize = 7,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };
        nowLabel.Bind(TextBlock.TextProperty, new Binding("[Player.Epg.Now]")
        {
            Source = LocalizationSource.Instance
        });

        var nowBadge = new Border
        {
            Width = 26,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            Background = accentBrush,
            ZIndex = 11,
            Child = nowLabel
        };
        Canvas.SetLeft(nowBadge, DesktopEpg.NowLineLeft - 13);
        Canvas.SetTop(nowBadge, 5);
        canvas.Children.Add(nowBadge);
    }

    private void UpdateDesktopEpgDateLabel()
    {
        var dateLabel = this.FindControl<TextBlock>("EpgDateLabel");
        if (dateLabel is null)
        {
            return;
        }

        dateLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationSource.Instance["Player.Epg.DateHeaderFormat"],
            LocalizationSource.Instance["Player.Epg.Today"],
            DesktopEpg.DisplayDate.ToString(
                LocalizationSource.Instance["Player.Epg.DateValueFormat"],
                CultureInfo.CurrentCulture));
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

        var targetX = Math.Max(0, DesktopEpg.NowLineLeft - timelineScroll.Viewport.Width / 2);
        var targetY = timelineScroll.Offset.Y;

        if (DesktopEpg.CurrentRowIndex >= 0)
        {
            targetY = Math.Max(
                0,
                DesktopEpg.CurrentRowIndex * DesktopEpgGuidePresentation.RowHeight
                - timelineScroll.Viewport.Height / 2
                + DesktopEpgGuidePresentation.RowHeight / 2);
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
        var now = DateTime.UtcNow;
        if (now - _lastPointerInteractionUtc < PointerInteractionThrottle)
        {
            return;
        }

        _lastPointerInteractionUtc = now;
        _playerViewModel?.UserInteractionCommand.Execute(null);
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
        // Oynatıcı üzerindeki tekerlek kaydırmasının paylaşılan kontrollere
        // (ör. zaman çizelgesi) ulaşmasını engeller.
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

        DesktopEpgPanelRow? row = null;
        if (sender is Control control)
        {
            row = control.DataContext as DesktopEpgPanelRow
                  ?? control.GetVisualAncestors()
                      .OfType<Control>()
                      .Select(c => c.DataContext)
                      .OfType<DesktopEpgPanelRow>()
                      .FirstOrDefault();
        }

        if (row != null)
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
