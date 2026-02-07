using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using IPTVPlayer.WinUI.Services;

namespace IPTVPlayer.WinUI.Controls;

public sealed partial class FFmpegVideoPlayer : UserControl
{
    private FFmpegPlayerService? _playerService;

    public FFmpegVideoPlayer()
    {
        this.InitializeComponent();
    }

    public void SetPlayerService(FFmpegPlayerService playerService)
    {
        if (_playerService != null)
        {
            _playerService.FrameReady -= OnFrameReady;
            _playerService.PlayingChanged -= OnPlayingChanged;
            _playerService.ErrorOccurred -= OnErrorOccurred;
        }

        _playerService = playerService;
        
        if (_playerService != null)
        {
            _playerService.FrameReady += OnFrameReady;
            _playerService.PlayingChanged += OnPlayingChanged;
            _playerService.ErrorOccurred += OnErrorOccurred;
        }
    }

    private void OnFrameReady(object? sender, WriteableBitmap bitmap)
    {
        VideoImage.Source = bitmap;
        LoadingRing.IsActive = false;
        ErrorText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void OnPlayingChanged(object? sender, bool isPlaying)
    {
        if (isPlaying)
        {
            LoadingRing.IsActive = false;
            ErrorText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        else
        {
            // Show loading if intended? Or just stop.
        }
    }

    private void OnErrorOccurred(object? sender, string message)
    {
        LoadingRing.IsActive = false;
        ErrorText.Text = message;
        ErrorText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }
}
