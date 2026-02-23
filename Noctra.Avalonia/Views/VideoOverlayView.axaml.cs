using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class VideoOverlayView : UserControl
{
    public static readonly StyledProperty<bool> IsVolumeToastVisibleProperty =
        AvaloniaProperty.Register<VideoOverlayView, bool>(nameof(IsVolumeToastVisible));
    public static readonly StyledProperty<bool> IsSeekToastVisibleProperty =
        AvaloniaProperty.Register<VideoOverlayView, bool>(nameof(IsSeekToastVisible));
    public static readonly StyledProperty<string> SeekToastTextProperty =
        AvaloniaProperty.Register<VideoOverlayView, string>(nameof(SeekToastText), "+0:10");

    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor VisibleCursor = new(StandardCursorType.Arrow);

    private readonly DispatcherTimer _volumeToastTimer;
    private readonly DispatcherTimer _seekToastTimer;
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
            if (_playerViewModel.IsLiveContent || sender is not Slider slider)
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
        }

        _playerViewModel = DataContext as PlayerViewModel;
        if (_playerViewModel != null)
        {
            _isInitialVolumeSet = false;
            _playerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;
            _playerViewModel.SkipOverlayRequested += PlayerViewModel_SkipOverlayRequested;
            UpdateOverlayCursor(_playerViewModel.IsVisible);
        }
        else
        {
            Cursor = VisibleCursor;
            IsSeekToastVisible = false;
        }
    }

    private void OverlayRoot_PointerMoved(object? sender, PointerEventArgs e)
    {
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
        // Block mouse/trackpad scrolling from reaching the Volume slider natively.
        // The default Slider control captures scroll events and changes volume rapidly,
        // causing severe toast notification spam (e.g. 50 times a second).
        e.Handled = true;
    }

    private void OverlayRoot_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_playerViewModel == null)
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
    }

    private void ShowVolumeToast()
    {
        _playerViewModel?.LogDebug("UI State: Volume Toast visible");
        IsVolumeToastVisible = true;
        _volumeToastTimer.Stop();
        _volumeToastTimer.Start();
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
}
