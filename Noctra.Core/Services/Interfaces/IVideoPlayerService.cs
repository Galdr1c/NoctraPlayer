using LibVLCSharp.Shared;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Video player servis interface'i
/// </summary>
public interface IVideoPlayerService : IDisposable
{
    /// <summary>
    /// Şuan oynatılan URL
    /// </summary>
    string? CurrentUrl { get; }

    /// <summary>
    /// Stream oynatmayı başlatır
    /// </summary>
    /// <param name="url">Stream URL</param>
    Task PlayAsync(string url);
    
    /// <summary>
    /// Oynatmayı duraklatır
    /// </summary>
    void Pause();

    /// <summary>
    /// Duraklatılan oynatmayı devam ettirir
    /// </summary>
    void Resume();
    
    /// <summary>
    /// Oynatmayı durdurur
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Ses seviyesi değiştiğinde (0-100)
    /// </summary>
    event EventHandler<int>? VolumeChanged;

    /// <summary>
    /// Ses seviyesini ayarlar (0-100)
    /// </summary>
    int Volume { get; set; }
    
    /// <summary>
    /// Sessiz modu
    /// </summary>
    bool IsMuted { get; set; }
    
    /// <summary>
    /// Oynatma durumu
    /// </summary>
    bool IsPlaying { get; }
    
    /// <summary>
    /// VOD için pozisyon (saniye)
    /// </summary>
    double Position { get; set; }
    
    /// <summary>
    /// Oynatma hızı (0.5 - 2.0)
    /// </summary>
    float PlaybackRate { get; set; }
    
    /// <summary>
    /// VOD için toplam süre (saniye)
    /// </summary>
    double Duration { get; }
    
    /// <summary>
    /// Mevcut ses track'lerini getirir
    /// </summary>
    IReadOnlyList<(int Id, string? Name)> AudioTracks { get; }
    
    /// <summary>
    /// Mevcut altyazı track'lerini getirir
    /// </summary>
    IReadOnlyList<(int Id, string? Name)> SubtitleTracks { get; }
    
    /// <summary>
    /// Ses track'ini seçer
    /// </summary>
    void SetAudioTrack(int trackId);
    
    /// <summary>
    /// Altyazı track'ini seçer
    /// </summary>
    void SetSubtitleTrack(int trackId);
    
    /// <summary>
    /// Oynatma durumu değiştiğinde
    /// </summary>
    event EventHandler<bool>? PlayingChanged;
    
    /// <summary>
    /// Pozisyon değiştiğinde
    /// </summary>
    event EventHandler<double>? PositionChanged;

    /// <summary>
    /// Medya oynatıcı asenkron olarak başlatıldığında (hazır olduğunda) tetiklenir
    /// </summary>
    event EventHandler<LibVLCSharp.Shared.MediaPlayer?>? MediaPlayerReady;

    /// <summary>
    /// Medya doğal olarak sona ulaştığında
    /// </summary>
    event EventHandler? PlaybackEnded;

    
    /// <summary>
    /// LibVLC MediaPlayer nesnesini döner (Sadece UI/VideoView bağlama için)
    /// </summary>
    LibVLCSharp.Shared.MediaPlayer? GetMediaPlayer();

    /// <summary>
    /// Buffering durumu değiştiğinde (0-100)
    /// </summary>
    event EventHandler<float>? BufferingChanged;

    /// <summary>
    /// Hata oluştuğunda
    /// </summary>
    event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Tespit edilen stream kalite bilgisi
    /// </summary>
    Noctra.Models.StreamQualityInfo? StreamQuality { get; }

    /// <summary>
    /// Stream kalitesi tespit edildiğinde
    /// </summary>
    event EventHandler<Noctra.Models.StreamQualityInfo>? QualityDetected;
}


