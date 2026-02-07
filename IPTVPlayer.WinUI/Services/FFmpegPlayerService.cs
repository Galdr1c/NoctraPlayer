using global::Microsoft.UI.Xaml.Media.Imaging;
using global::IPTVPlayer.Services.Interfaces;
using global::System;
using global::System.Collections.Generic;
using global::System.IO;
using global::System.Threading;
using global::System.Threading.Tasks;
using global::System.Runtime.InteropServices;
using global::System.Runtime.InteropServices.WindowsRuntime;

namespace IPTVPlayer.WinUI.Services;

public class FFmpegPlayerService : IVideoPlayerService, IDisposable
{
    private CancellationTokenSource? _playbackCts;
    private Task? _playbackTask;
    private readonly IDispatcherService _dispatcherService;
    private FFmpegWorker? _worker;
    
    public event EventHandler<bool>? PlayingChanged;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<WriteableBitmap>? FrameReady;
    public event Action<double, double, int>? StatisticsUpdated;
    public event EventHandler<string>? SubtitleDecoded;
    
    public bool IsPlaying { get; private set; }
    public double Position { get; set; }
    public double Duration { get; private set; }
    public int Volume { get; set; } = 100;
    public bool IsMuted { get; set; }
    public IReadOnlyList<(int Id, string? Name)> AudioTracks { get; } = new List<(int Id, string? Name)>();
    public IReadOnlyList<(int Id, string? Name)> SubtitleTracks { get; } = new List<(int Id, string? Name)>();

    public FFmpegPlayerService(IDispatcherService dispatcherService)
    {
        _dispatcherService = dispatcherService;
        var path = Path.Combine(AppContext.BaseDirectory, "FFmpeg");
        unsafe { global::FFmpeg.AutoGen.ffmpeg.RootPath = Directory.Exists(path) ? path : AppContext.BaseDirectory; }
    }

    public async Task PlayAsync(string url)
    {
        try
        {
            await StopInternalAsync();
            _playbackCts = new CancellationTokenSource();
            var token = _playbackCts.Token;
            _playbackTask = Task.Run(() => PlaybackLoop(url, token), token);
            IsPlaying = true;
            _dispatcherService.Invoke(() => PlayingChanged?.Invoke(this, true));
        }
        catch (Exception ex)
        {
            _dispatcherService.Invoke(() => ErrorOccurred?.Invoke(this, ex.Message));
        }
    }

    private void PlaybackLoop(string url, CancellationToken token)
    {
        _worker = new FFmpegWorker();
        try
        {
            _worker.Open(url);
            Duration = _worker.Duration;
            int width = _worker.Width;
            int height = _worker.Height;

            var startTime = DateTime.Now;

            while (!token.IsCancellationRequested)
            {
                if (_worker.ReadFrame(out IntPtr dataPtr, out int stride, out double pts))
                {
                    _dispatcherService.Invoke(() =>
                    {
                        var wBitmap = new WriteableBitmap(width, height);
                        using (var stream = wBitmap.PixelBuffer.AsStream())
                        {
                            int pSize = height * stride;
                            byte[] b = new byte[pSize];
                            Marshal.Copy(dataPtr, b, 0, pSize);
                            stream.Write(b, 0, pSize);
                        }
                        wBitmap.Invalidate();
                        FrameReady?.Invoke(this, wBitmap);
                        Position = pts;
                        PositionChanged?.Invoke(this, Position);
                    });

                    // Simple sync: wait until next frame time or roughly 1 tick
                    Thread.Sleep(1); 
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _dispatcherService.Invoke(() => ErrorOccurred?.Invoke(this, ex.Message));
        }
        finally
        {
            _worker.Dispose();
            _worker = null;
            IsPlaying = false;
            _dispatcherService.Invoke(() => PlayingChanged?.Invoke(this, false));
        }
    }

    public void Pause() { _playbackCts?.Cancel(); IsPlaying = false; }
    public void Stop() { _ = StopInternalAsync(); }
    
    private async Task StopInternalAsync()
    {
        _playbackCts?.Cancel();
        if (_playbackTask != null)
        {
            try { await _playbackTask; } catch { }
        }
        IsPlaying = false;
    }

    public void SetAudioTrack(int id) { }
    public void SetSubtitleTrack(int id) { }
    public void Dispose() { Stop(); }
}
