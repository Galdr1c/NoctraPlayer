using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using IPTVPlayer.ViewModels;
using IPTVPlayer.WinUI.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IPTVPlayer.WinUI.Pages;

public sealed partial class PlayerPage : Page, INotifyPropertyChanged
{
    private readonly PlayerViewModel _viewModel;
    private readonly FFmpegPlayerService _playerService;
    private bool _isProgressSliderDragging;
    
    private Visibility _subtitleVisibility = Visibility.Collapsed;
    public Visibility SubtitleVisibility 
    { 
        get => _subtitleVisibility; 
        set 
        {
            if (_subtitleVisibility != value)
            {
                _subtitleVisibility = value;
                OnPropertyChanged();
            }
        }
    }

    public PlayerPage()
    {
        this.InitializeComponent();
        
        _viewModel = App.Instance.Services.GetRequiredService<PlayerViewModel>();
        _playerService = App.Instance.Services.GetRequiredService<FFmpegPlayerService>();
        
        VideoPlayer.SetPlayerService(_playerService);
        Statistics.SetPlayerService(_playerService);
        
        DataContext = _viewModel;
        
        _playerService.PlayingChanged += OnPlayingChanged;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.SubtitleDecoded += OnSubtitleDecoded;
        
        // Keyboard shortcuts
        this.KeyDown += PlayerPage_KeyDown;
    }

    private void PlayerPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                PlayPauseButton_Click(null, null);
                break;
            case Windows.System.VirtualKey.Left:
                SkipBackward_Click(null, null);
                break;
            case Windows.System.VirtualKey.Right:
                SkipForward_Click(null, null);
                break;
            case Windows.System.VirtualKey.F:
                FullscreenButton_Click(null, null);
                break;
            case Windows.System.VirtualKey.M:
                MuteButton_Click(null, null);
                break;
            case Windows.System.VirtualKey.Up:
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
                break;
            case Windows.System.VirtualKey.Down:
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
                break;
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_playerService.IsPlaying)
        {
            _playerService.Pause();
        }
        else if (_viewModel.CurrentChannel != null)
        {
            _ = _playerService.PlayAsync(_viewModel.CurrentChannel.StreamUrl);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _playerService.Stop();
    }

    private void SkipBackward_Click(object sender, RoutedEventArgs e)
    {
        if (_playerService.Duration > 0)
        {
            _playerService.Position = Math.Max(0, _playerService.Position - 10);
        }
    }

    private void SkipForward_Click(object sender, RoutedEventArgs e)
    {
        if (_playerService.Duration > 0)
        {
            _playerService.Position = Math.Min(_playerService.Duration, _playerService.Position + 10);
        }
    }

    private void ProgressSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isProgressSliderDragging = true;
    }

    private void ProgressSlider_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isProgressSliderDragging = false;
        _playerService.Position = ProgressSlider.Value;
    }

    private void ProgressSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isProgressSliderDragging)
        {
            CurrentTimeText.Text = FormatTime(e.NewValue);
        }
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _playerService.Volume = (int)e.NewValue;
        UpdateVolumeIcon();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        _playerService.IsMuted = !_playerService.IsMuted;
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        if (_playerService.IsMuted || _playerService.Volume == 0)
            VolumeIcon.Symbol = Symbol.Mute;
        else
            VolumeIcon.Symbol = Symbol.Volume;
    }

    private void AudioTrackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AudioTrackComboBox.SelectedItem is ValueTuple<int, string?> track)
        {
            _playerService.SetAudioTrack(track.Item1);
        }
    }

    private void SubtitleTrackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SubtitleTrackComboBox.SelectedItem is ValueTuple<int, string?> track)
        {
            _playerService.SetSubtitleTrack(track.Item1);
            // Updating visibility is handled by property binding, but we set it here logic-wise
            SubtitleVisibility = track.Item1 >= 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void StatsToggle_Click(object sender, RoutedEventArgs e)
    {
        Statistics.Visibility = StatsToggle.IsChecked == true 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        var appWindow = App.Instance.MainWindow;
        // Implement fullscreen toggle
    }

    private void OnPlayingChanged(object? sender, bool isPlaying)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var icon = PlayPauseButton.Content as SymbolIcon;
            if (icon != null)
            {
                icon.Symbol = isPlaying ? Symbol.Pause : Symbol.Play;
            }
            
            // Update track lists
            if (isPlaying)
            {
                AudioTrackComboBox.ItemsSource = _playerService.AudioTracks;
                SubtitleTrackComboBox.ItemsSource = _playerService.SubtitleTracks;
            }
        });
    }

    private void OnPositionChanged(object? sender, double position)
    {
        if (_isProgressSliderDragging) return;
        
        DispatcherQueue.TryEnqueue(() =>
        {
            ProgressSlider.Maximum = _playerService.Duration;
            ProgressSlider.Value = position;
            
            CurrentTimeText.Text = FormatTime(position);
            DurationText.Text = FormatTime(_playerService.Duration);
        });
    }
    
    private void OnSubtitleDecoded(object? sender, string text)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SubtitleText.Text = text;
        });
    }

    private string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.Hours > 0 
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
