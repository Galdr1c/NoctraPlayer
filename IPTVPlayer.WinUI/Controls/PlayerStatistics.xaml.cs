using Microsoft.UI.Xaml.Controls;
using IPTVPlayer.WinUI.Services;

namespace IPTVPlayer.WinUI.Controls;

public sealed partial class PlayerStatistics : UserControl
{
    private FFmpegPlayerService? _playerService;

    public PlayerStatistics()
    {
        this.InitializeComponent();
    }

    public void SetPlayerService(FFmpegPlayerService playerService)
    {
        if (_playerService != null)
        {
            _playerService.StatisticsUpdated -= OnStatisticsUpdated;
        }

        _playerService = playerService;
        
        if (_playerService != null)
        {
            _playerService.StatisticsUpdated += OnStatisticsUpdated;
        }
    }

    private void OnStatisticsUpdated(double fps, double dropped, int buffer)
    {
        FpsText.DispatcherQueue.TryEnqueue(() =>
        {
            FpsText.Text = $"FPS: {fps:F1}";
            DroppedText.Text = $"Dropped: {dropped:F0}";
            BufferText.Text = $"Buffer: {buffer} frames";
        });
    }

    public void UpdateResolution(int width, int height)
    {
        ResolutionText.DispatcherQueue.TryEnqueue(() =>
        {
            ResolutionText.Text = $"Resolution: {width}x{height}";
        });
    }

    public void UpdateBitrate(long bitrate)
    {
        BitrateText.DispatcherQueue.TryEnqueue(() =>
        {
            BitrateText.Text = $"Bitrate: {bitrate / 1000} kbps";
        });
    }
}
