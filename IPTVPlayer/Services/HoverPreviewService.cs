using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace IPTVPlayer.Services
{
    public class HoverPreviewService : IDisposable
    {
        private readonly LibVLC _libVlc;
        private MediaPlayer? _mediaPlayer;
        private CancellationTokenSource? _cts;
        private Border? _activePreviewContainer;
        private VideoView? _activeVideoView;

        public HoverPreviewService()
        {
            _libVlc = new LibVLC("--quiet");
        }

        public async Task StartPreviewAsync(string streamUrl, Border previewContainer, VideoView videoView)
        {
            // Cancel any pending preview
            StopPreview();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                // 1.5s Delay
                await Task.Delay(1500, token);

                if (token.IsCancellationRequested) return;

                // Prepare Media Player
                _activePreviewContainer = previewContainer;
                _activeVideoView = videoView;

                _mediaPlayer = new MediaPlayer(_libVlc);
                _mediaPlayer.Mute = true; // Preview should be silent
                
                _activeVideoView.MediaPlayer = _mediaPlayer;
                
                var media = new Media(_libVlc, new Uri(streamUrl));
                _mediaPlayer.Play(media);

                // Show container
                _activePreviewContainer.Visibility = Visibility.Visible;
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preview Error: {ex.Message}");
            }
        }

        public void StopPreview()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }

            if (_activeVideoView != null)
            {
                _activeVideoView.MediaPlayer = null;
                _activeVideoView = null;
            }

            if (_activePreviewContainer != null)
            {
                _activePreviewContainer.Visibility = Visibility.Collapsed;
                _activePreviewContainer = null;
            }
        }

        public void Dispose()
        {
            StopPreview();
            _libVlc.Dispose();
        }
    }
}
