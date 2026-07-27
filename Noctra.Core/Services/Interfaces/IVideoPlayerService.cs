using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

public interface IVideoPlayerService : IDisposable
{
    string? CurrentUrl { get; }
    Task PlayAsync(string url, double startTimeSeconds = 0);
    Task HardSeekAsync(double seconds);
    Task ReinitializeAsync();
    void UpdateMediaMetadata(Noctra.Models.PlaybackMediaMetadata metadata) { }
    void Pause();
    void Resume();
    void Stop();

    Task EndSessionAsync(CancellationToken cancellationToken = default);

    event EventHandler<int>? VolumeChanged;
    int Volume { get; set; }
    bool IsMuted { get; set; }
    bool IsPlaying { get; }
    Noctra.Models.PlaybackState State { get; }
    bool HasLoadedMedia { get; }
    long CurrentTimeMilliseconds { get; }
    double Position { get; set; }
    float PlaybackRate { get; set; }
    double Duration { get; }

    IReadOnlyList<(int Id, string? Name)> AudioTracks { get; }
    IReadOnlyList<(int Id, string? Name)> SubtitleTracks { get; }
    void SetAudioTrack(int trackId);
    void SetSubtitleTrack(int trackId);

    event EventHandler<bool>? PlayingChanged;
    event EventHandler<double>? PositionChanged;
    event EventHandler? PlayerReady;
    event EventHandler? PlaybackEnded;
    event EventHandler<float>? BufferingChanged;
    event EventHandler<string>? ErrorOccurred;
    event EventHandler<string?>? SubtitleTextChanged;

    void SeekToTime(long milliseconds);
    void PlayLoadedMedia();
    void SetVideoLayout(string? aspectRatio, string? cropGeometry);

    Noctra.Models.StreamQualityInfo? StreamQuality { get; }
    event EventHandler<Noctra.Models.StreamQualityInfo>? QualityDetected;
}
