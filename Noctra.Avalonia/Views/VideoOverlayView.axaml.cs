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

    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor VisibleCursor = new(StandardCursorType.Arrow);

    private readonly DispatcherTimer _volumeToastTimer;
    private PlayerViewModel? _playerViewModel;
    private bool _isTimelinePointerDown;

    public bool IsVolumeToastVisible
    {
        get => GetValue(IsVolumeToastVisibleProperty);
        set => SetValue(IsVolumeToastVisibleProperty, value);
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
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_playerViewModel != null)
        {
            _playerViewModel.PropertyChanged -= PlayerViewModel_PropertyChanged;
        }

        _playerViewModel = DataContext as PlayerViewModel;
        if (_playerViewModel != null)
        {
            _playerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;
            UpdateOverlayCursor(_playerViewModel.IsVisible);
        }
        else
        {
            Cursor = VisibleCursor;
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
                _playerViewModel.Volume = Math.Min(100, _playerViewModel.Volume + 5);
                _playerViewModel.UserInteractionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                _playerViewModel.Volume = Math.Max(0, _playerViewModel.Volume - 5);
                _playerViewModel.UserInteractionCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void PlayerViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.Volume) or nameof(PlayerViewModel.IsMuted))
        {
            ShowVolumeToast();
        }

        if (e.PropertyName == nameof(PlayerViewModel.IsVisible))
        {
            UpdateOverlayCursor(_playerViewModel?.IsVisible == true);
        }
    }

    private void ShowVolumeToast()
    {
        IsVolumeToastVisible = true;
        _volumeToastTimer.Stop();
        _volumeToastTimer.Start();
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

    private void TimelineSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isTimelinePointerDown = true;
    }

    private void TimelineSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isTimelinePointerDown || _playerViewModel == null)
        {
            return;
        }

        _isTimelinePointerDown = false;

        if (_playerViewModel.IsLiveContent || sender is not Slider slider)
        {
            return;
        }

        _playerViewModel.SeekCommand.Execute(slider.Value);
        _playerViewModel.UserInteractionCommand.Execute(null);
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
}
