using LibVLCSharp.Shared;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// LibVLCSharp tabanlı video player servisi
/// </summary>
public class VideoPlayerService : IVideoPlayerService
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private readonly IDispatcherService _dispatcherService;
    private readonly ISettingsService _settingsService;
    private bool _disposed;
    private int _currentVolume = 100;
    private string _lastUserAgent;
    private int _lastSubtitleFontSize;
    private int _lastSubtitleBackgroundOpacity;
    private int _lastSubtitleMargin;
    private bool _lastHardwareAcceleration;
    private BufferSize _lastVideoBufferSize;
    
    private int _retryCount = 0;
    private const int MaxRetries = 3;
    private readonly object _qualitySync = new();
    private CancellationTokenSource? _qualityMonitorCts;
    private CancellationTokenSource? _playCts;
    private long _playGeneration;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;
    private CancellationTokenSource? _volumeSaveCts;
    private CancellationTokenSource? _reinitCts;
    
    // Altyazı ve Ses seçimi durumu reinit sonrası kaybolmasın diye
    private int? _restoredAudioTrack;
    private int? _restoredSpu;

    private void LogDebug(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(@"d:\IPTVPlayer\vlc_debug_log.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [VPS] {msg}\n");
        }
        catch { }
        
        System.Diagnostics.Debug.WriteLine($"[VideoPlayerService] {msg}");
    }


    public event EventHandler<MediaPlayer?>? MediaPlayerReady;
    public event EventHandler<bool>? PlayingChanged;

    public event EventHandler<double>? PositionChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<StreamQualityInfo>? QualityDetected;
    public event EventHandler<int>? VolumeChanged;

    public string? CurrentUrl { get; private set; }
    public StreamQualityInfo? StreamQuality { get; private set; }


    private int GetNetworkCaching() => _settingsService.Settings.VideoBufferSize switch
    {
        BufferSize.Small => 2000,
        BufferSize.Large => 10000,
        _ => 5000  // Normal
    };

    private int GetLiveCaching() => GetNetworkCaching() - 1000;
    private int GetFileCaching() => 1500;

    public VideoPlayerService(IDispatcherService dispatcherService, ISettingsService settingsService)
    {
        _dispatcherService = dispatcherService;
        _settingsService = settingsService;
        
        // Initialize volume from settings
        _currentVolume = _settingsService.Settings.DefaultVolume;
        _lastUserAgent = _settingsService.Settings.UserAgent;
        _lastSubtitleFontSize = _settingsService.Settings.SubtitleFontSize;
        _lastSubtitleBackgroundOpacity = _settingsService.Settings.SubtitleBackgroundOpacity;
        _lastSubtitleMargin = _settingsService.Settings.SubtitleMargin;
        _lastHardwareAcceleration = _settingsService.Settings.HardwareAcceleration;
        _lastVideoBufferSize = _settingsService.Settings.VideoBufferSize;

        _settingsService.SettingsChanged += OnSettingsChanged;

        // Start initialization in the background so we don't block the UI thread
        _ = InitializeAsync();
    }

    private void OnSettingsChanged()
    {
        var settings = _settingsService.Settings;
        bool shouldReinit = false;

        if (_lastUserAgent != settings.UserAgent)
        {
            _lastUserAgent = settings.UserAgent;
            shouldReinit = true;
        }

        if (_lastSubtitleFontSize != settings.SubtitleFontSize)
        {
            _lastSubtitleFontSize = settings.SubtitleFontSize;
            shouldReinit = true;
        }

        if (_lastSubtitleBackgroundOpacity != settings.SubtitleBackgroundOpacity)
        {
            _lastSubtitleBackgroundOpacity = settings.SubtitleBackgroundOpacity;
            shouldReinit = true;
        }

        if (_lastSubtitleMargin != settings.SubtitleMargin)
        {
            _lastSubtitleMargin = settings.SubtitleMargin;
            shouldReinit = true;
        }
        
        if (_lastHardwareAcceleration != settings.HardwareAcceleration)
        {
            _lastHardwareAcceleration = settings.HardwareAcceleration;
            shouldReinit = true;
        }

        if (_lastVideoBufferSize != settings.VideoBufferSize)
        {
            _lastVideoBufferSize = settings.VideoBufferSize;
            shouldReinit = true;
        }

        if (shouldReinit)
        {
            var cts = new CancellationTokenSource();
            _reinitCts?.Cancel();
            _reinitCts?.Dispose();
            _reinitCts = cts;

            var token = cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    // Debounce süresi: Çoklu ayar değişikliğinin sonlanmasını bekle
                    await Task.Delay(500, token);
                    if (!token.IsCancellationRequested)
                    {
                        await ReinitializeAsync();
                    }
                }
                catch (TaskCanceledException) { }
            }, token);
        }
    }

    private async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            await Task.Run(() => 
            {
                LibVLCSharp.Shared.Core.Initialize();
                
                var ua = string.IsNullOrWhiteSpace(_lastUserAgent) ? "VLC/3.0.4" : _lastUserAgent;
                var netCaching = GetNetworkCaching();
                var liveCaching = GetLiveCaching();
                var fileCaching = GetFileCaching();

                var optionsList = new List<string>
                {
                    // avcodec-fast: daha az kalite ama takılma yok
                    "--avcodec-fast",
                    // Direct rendering — CPU→GPU kopyalama yükünü azaltır
                    "--avcodec-dr",

                    $"--network-caching={netCaching}",
                    $"--live-caching={liveCaching}",
                    $"--file-caching={fileCaching}",
                    
                    // Canlı TV için clock düzeltmesi
                    "--clock-synchro=0",
                    "--clock-jitter=500",

                    "--rtsp-tcp",
                    // "--drop-late-frames", // Kaldırıldı (MKV için sorunlu)
                    // "--skip-frames",      // Kaldırıldı
                    "--ts-seek-percent",
                    "--http-reconnect",
                    $"--http-user-agent={ua}",
                    "--verbose=0",
                    "--quiet",
                    
                    //Altyaz ayarlarını buraya ekle
                    $"--freetype-fontsize={_lastSubtitleFontSize}", // Altyazı boyutu
                    $"--freetype-background-opacity={_lastSubtitleBackgroundOpacity}", // Arkaplan şeffaflığı
                    "--freetype-background-color=0x000000",         // Arkaplan rengi siyah
                    $"--sub-margin={_lastSubtitleMargin}",          // Alttan yukarı doğru marjin
                };

                if (_lastHardwareAcceleration)
                {
                    // dxva2 (eski) → d3d11va (modern, H.265/HEVC destekli)
                    optionsList.Add("--avcodec-hw=d3d11va");
                    optionsList.Add("--vout=direct3d11");
                }
                else
                {
                    optionsList.Add("--avcodec-hw=none");
                    optionsList.Add("--vout=any");
                }
                
                _libVLC = new LibVLC(optionsList.ToArray());
                _mediaPlayer = new MediaPlayer(_libVLC);
            });

            SetupEventHandlers();
            _isInitialized = true;
            
            await _dispatcherService.InvokeAsync(() =>
            {
                MediaPlayerReady?.Invoke(this, _mediaPlayer);
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            LogDebug($"VLC Init failed: {ex.Message} - {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"[VideoPlayerService] VLC Init failed: {ex.Message}");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task ReinitializeAsync()
    {
        // 1. Durumu kaydet
        var currentUrl = CurrentUrl;
        var wasPlaying = IsPlaying;
        var currentPosition = Position;
        
        // Ses ve altyazı seçimini kaydet
        if (_mediaPlayer != null)
        {
            _restoredAudioTrack = _mediaPlayer.AudioTrack;
            _restoredSpu = _mediaPlayer.Spu;
        }
        
        // 2. Oynatmayı durdur
        Stop();

        // 3. UI üzerindeki MediaPlayer referansını kaldır (crash önlemek için çok kritik)
        await _dispatcherService.InvokeAsync(() =>
        {
            MediaPlayerReady?.Invoke(this, null);
            return Task.CompletedTask;
        });
        
        // 4. Mevcut nesneleri arka planda temizle
        await Task.Run(() =>
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }

            if (_libVLC != null)
            {
                _libVLC.Dispose();
                _libVLC = null;
            }
        });

        _isInitialized = false;

        // 5. Yeni ayarlarla tekrar başlat
        await InitializeAsync();

        // 6. Eğer bir şey çalıyorsa kaldığı yerden devam ettir
        if (!string.IsNullOrEmpty(currentUrl))
        {
            // Position saniye cinsindendir
            await PlayAsync(currentUrl, currentPosition);
            if (!wasPlaying)
            {
                Pause();
            }
        }
    }

    private void SetupEventHandlers()
    {
        if (_mediaPlayer == null) return;

        _mediaPlayer.Opening += (s, e) =>
        {
            LogDebug("Event: Opening");
            // Set volume as early as possible
            if (_mediaPlayer != null) _mediaPlayer.Volume = _currentVolume;
        };

        _mediaPlayer.Playing += (s, e) =>
        {
            LogDebug("Event: Playing");

            // Kaydedilmiş ses veya altyazı track seçimleri varsa geri yükle
            if (_restoredAudioTrack.HasValue || _restoredSpu.HasValue)
            {
                var audioToRestore = _restoredAudioTrack;
                var spuToRestore = _restoredSpu;
                _restoredAudioTrack = null;
                _restoredSpu = null;

                _ = Task.Run(async () =>
                {
                    // Tracklerin VLC tarafından tam yüklenmesi için ufak bir gecikme
                    await Task.Delay(500); 
                    if (_mediaPlayer == null || !_mediaPlayer.IsPlaying) return;

                    if (audioToRestore.HasValue && audioToRestore.Value >= 0)
                    {
                        LogDebug($"Restoring AudioTrack: {audioToRestore.Value}");
                        _mediaPlayer.SetAudioTrack(audioToRestore.Value);
                    }
                    if (spuToRestore.HasValue)
                    {
                        LogDebug($"Restoring SPU: {spuToRestore.Value}");
                        SetSubtitleTrack(spuToRestore.Value);
                    }
                });
            }
            
            // Aggressive enforcement kaldırıldı.
            // Opening event'i zaten volume'u doğru ayarlıyor; burada tekrar yazmak
            // hem gereksiz hem de VolumeChanged event spam'ine yol açıyor.
            _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, true));
            StartQualityMonitoring();
        };
        _mediaPlayer.Paused += (s, e) =>
        {
            LogDebug("Event: Paused");
            StopQualityMonitoring();
            _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, false));
        };
        _mediaPlayer.Stopped += (s, e) =>
        {
            LogDebug("Event: Stopped");
            StopQualityMonitoring();
            _dispatcherService.BeginInvoke(() => PlayingChanged?.Invoke(this, false));
        };
        _mediaPlayer.EndReached += (s, e) =>
        {
            LogDebug("Event: EndReached");
            StopQualityMonitoring();
            _dispatcherService.BeginInvoke(() =>
            {
                PlayingChanged?.Invoke(this, false);
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            });
        };
        
        _mediaPlayer.PositionChanged += (s, e) => 
        {
            var currentDuration = Duration;
            if (currentDuration > 0)
            {
                _dispatcherService.BeginInvoke(() => PositionChanged?.Invoke(this, e.Position * currentDuration));
            }
        };
        
        _mediaPlayer.EncounteredError += (s, e) => 
        {
            LogDebug("Event: EncounteredError");
            _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, "Video oynatma hatası oluştu"));
        };
            
        _mediaPlayer.Buffering += (sender, e) =>
        {
            var progress = (int)e.Cache;
            _dispatcherService.BeginInvoke(() => BufferingChanged?.Invoke(this, e.Cache));
        };
    }

    public event EventHandler<float>? BufferingChanged;

    public MediaPlayer? GetMediaPlayer() => _mediaPlayer;

    public async Task PlayAsync(string url, double startTimeSeconds = 0)
    {
        LogDebug($"PlayAsync Called -> URL: {url}, StartTime: {startTimeSeconds}s");
        CurrentUrl = url;
        

        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        if (_mediaPlayer == null)
        {
            LogDebug("ERROR: _mediaPlayer is null!");
            return;
        }
        
        // Explicitly terminate the connection and wait before reopening.
        // Xtream Codes servers will ban or drop streams if 2 connections overlap.
        if (_mediaPlayer.State == VLCState.Playing || _mediaPlayer.State == VLCState.Buffering || _mediaPlayer.State == VLCState.Opening || _mediaPlayer.State == VLCState.Paused)
        {
            LogDebug("Stopping active player stream...");
            _mediaPlayer.Stop();
            await Task.Delay(1200); // Allow TCP FIN to reach server
        }
        
        Interlocked.Exchange(ref _retryCount, 0);
        var generation = Interlocked.Increment(ref _playGeneration);
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = new CancellationTokenSource();
        var playToken = _playCts.Token;
        StopQualityMonitoring();
        lock (_qualitySync)
        {
            StreamQuality = null;
        }
        await PlayWithRetryAsync(url, playToken, generation, startTimeSeconds);
    }

    public async Task HardSeekAsync(double seconds)
    {
        var url = CurrentUrl;
        if (string.IsNullOrEmpty(url)) return;

        LogDebug($"HardSeekAsync Called -> URL: {url}, StartTime: {seconds}s");

        if (!_isInitialized) await InitializeAsync();
        if (_mediaPlayer == null) return;

        // Explicitly terminate the connection and wait before reopening.
        // Extremely important for HardSeek because we inject a new Media into the running player.
        if (_mediaPlayer.State == VLCState.Playing || _mediaPlayer.State == VLCState.Buffering || _mediaPlayer.State == VLCState.Opening || _mediaPlayer.State == VLCState.Paused)
        {
            LogDebug("Stopping active player stream for HardSeek...");
            _mediaPlayer.Stop();
            await Task.Delay(500); // Seek için 500ms yeterli (kanal değişimi 1200ms kullanır)
        }

        Interlocked.Exchange(ref _retryCount, 0); // İstenirse retry devrede kalabilir
        var generation = Interlocked.Increment(ref _playGeneration);
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = new CancellationTokenSource();
        var playToken = _playCts.Token;
        
        StopQualityMonitoring();
        lock (_qualitySync)
        {
            StreamQuality = null;
        }
        await PlayWithRetryAsync(url, playToken, generation, seconds);
    }

    private async Task PlayWithRetryAsync(string url, CancellationToken cancellationToken, long generation, double startSeconds)
    {
        while (Interlocked.CompareExchange(ref _retryCount, 0, 0) <= MaxRetries)
        {
            try
            {
                if (_libVLC == null) return;

                Media media;

                // Gelen URL'nin bir internet yayını mı yoksa yerel dosya mı olduğunu anla
                bool isNetworkStream = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                                    url.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase) ||
                                    url.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase);

                if (isNetworkStream)
                {
                    // 🌐 İNTERNET YAYINI (IPTV / VOD) - Akıllı profiller
                    media = new Media(_libVLC, url, FromType.FromLocation);

                    var currentUa = string.IsNullOrWhiteSpace(_lastUserAgent) ? "VLC/3.0.4" : _lastUserAgent;
                    media.AddOption($":http-user-agent={currentUa}");
                    media.AddOption(":http-reconnect=true");

                    var netCaching = GetNetworkCaching();
                    var liveCaching = GetLiveCaching();

                    var streamProfile = DetectStreamProfile(url);
                    switch (streamProfile)
                    {
                        case StreamProfile.LiveTs:
                            media.AddOption($":network-caching={liveCaching}");
                            media.AddOption(":clock-synchro=0");
                            media.AddOption(":clock-jitter=500");
                            media.AddOption(":ts-seek-percent");
                            // :drop-late-frames kaldırıldı (iyi bağlantıda görüntü bozukluğu yapabiliyor)
                            break;

                        case StreamProfile.VodMkv:
                            media.AddOption($":network-caching={netCaching * 2}");
                            media.AddOption($":live-caching={netCaching * 2}");
                            
                            if (_lastHardwareAcceleration)
                            {
                                media.AddOption(":avcodec-hw=d3d11va");
                            }
                            else
                            {
                                media.AddOption(":avcodec-hw=none");
                            }
                            
                            media.AddOption(":no-drop-late-frames");
                            media.AddOption(":no-skip-frames");
                            media.AddOption(":http-forward-cookies");
                            break;

                        case StreamProfile.VodMp4:
                            media.AddOption($":network-caching={netCaching}");
                            media.AddOption(":demux=mp4,avformat");
                            break;

                        case StreamProfile.LiveM3u8:
                            media.AddOption($":network-caching={netCaching + 1000}");
                            media.AddOption(":adaptive-logic=rate"); // highest yerine rate kullanılarak donmalar engellendi
                            break;

                        case StreamProfile.Unknown:
                            // Uzantısız/belirsiz stream — VLC kendi demuxer'ı ile otomatik algılasın
                            media.AddOption($":network-caching={netCaching}");
                            media.AddOption(":no-drop-late-frames");
                            media.AddOption(":no-skip-frames");
                            media.AddOption(":http-continuous");
                            media.AddOption(":http-reconnect");
                            break;

                        default:
                            media.AddOption($":network-caching={netCaching}");
                            media.AddOption(":no-drop-late-frames");
                            media.AddOption(":no-skip-frames");
                            break;
                    }
                }
                else
                {
                    // 💾 YEREL DOSYA (İndirilen İçerik)
                    string localPath = url;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
                    {
                        localPath = fileUri.LocalPath;
                    }

                    media = new Media(_libVLC, localPath, FromType.FromPath);
                    media.AddOption($":file-caching={GetFileCaching()}");
                    media.AddOption(":no-drop-late-frames");
                    media.AddOption(":no-skip-frames");
                }

                if (startSeconds > 0)
                {
                    media.AddOption($":start-time={Math.Floor(startSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                }

                // Dinamik Altyazı Konumu ve Boyutu (Yüzde hesaplamaları)
                if (_lastSubtitleMargin >= 900 || _lastSubtitleFontSize > 0)
                {
                    // Video çözünürlüğünü öğrenebilmek için kısa bir parse yap
                    var tcs = new CancellationTokenSource(2000); // En fazla 2 saniye bekle
                    try
                    {
                        var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, tcs.Token).Token;
                        await media.Parse(MediaParseOptions.ParseNetwork, timeout: 2000);
                        
                        uint videoHeight = 1080; // Default varsayım
                        var tracks = media.Tracks;
                        if (tracks != null)
                        {
                            var vTrack = tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
                            if (vTrack.Data.Video.Height > 0)
                            {
                                videoHeight = vTrack.Data.Video.Height;
                            }
                        }

                        // Marjin Hesaplaması (Yukarı konumu için)
                        if (_lastSubtitleMargin >= 900)
                        {
                            int calculatedMargin = (int)(videoHeight * 0.85);
                            media.AddOption($":sub-margin={calculatedMargin}");
                        }
                        else
                        {
                            media.AddOption($":sub-margin={_lastSubtitleMargin}");
                        }

                        // Font Boyutu Hesaplaması (Netflix standartları)
                        double fontPercentage = 0.045; // Varsayılan: Orta (~%4.5)
                        if (_lastSubtitleFontSize <= 28) fontPercentage = 0.03; // Küçük (~%3)
                        else if (_lastSubtitleFontSize >= 60) fontPercentage = 0.085; // Büyük (~%8.5)

                        int calculatedFontSize = (int)(videoHeight * fontPercentage);
                        // VLC freetype modülüne parametreyi anlık akış bazlı iletiyoruz
                        media.AddOption($":freetype-fontsize={calculatedFontSize}");

                        LogDebug($"Dynamic Subtitles: Height={videoHeight}, FontSize={calculatedFontSize}px");
                    }
                    catch
                    {
                        // Hata veya timeout durumunda eski fallbackleri kullan
                        media.AddOption($":sub-margin={_lastSubtitleMargin}");
                        media.AddOption($":freetype-fontsize={_lastSubtitleFontSize}");
                    }
                }
                else
                {
                    media.AddOption($":sub-margin={_lastSubtitleMargin}");
                    media.AddOption($":freetype-fontsize={_lastSubtitleFontSize}");
                }

                if (_mediaPlayer == null) 
                {
                    media.Dispose();
                    return;
                }

                var oldMedia = _mediaPlayer.Media;
                _mediaPlayer.Media = media;
                oldMedia?.Dispose();
                media.Dispose(); // LibVLC increments ref count, so we must release our local handle

                // Hata event'ini dinle
                bool errorOccurred = false;
                void OnError(object? s, EventArgs e)
                {
                    errorOccurred = true;
                }

                _mediaPlayer.EncounteredError += OnError;
                _mediaPlayer.Play();

                // 5 saniye bekle - başarılı başladı mı? Veya hata verirse hemen kır
                try
                {
                    for (int i = 0; i < 50; i++)
                    {
                        if (errorOccurred) break;
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (TaskCanceledException)
                {
                    _mediaPlayer.EncounteredError -= OnError;
                    return;
                }

                _mediaPlayer.EncounteredError -= OnError;

                if (cancellationToken.IsCancellationRequested || generation != Interlocked.Read(ref _playGeneration))
                {
                    return;
                }

                if (errorOccurred && Interlocked.CompareExchange(ref _retryCount, 0, 0) < MaxRetries)
                {
                    Interlocked.Increment(ref _retryCount);
                    try
                    {
                        await Task.Delay(1500, cancellationToken); // 1.5 saniye bekle
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested || generation != Interlocked.Read(ref _playGeneration))
                    {
                        return;
                    }

                    continue; // Loop tekrar dönecek ve yeniden play deneyecek.
                }
                else if (errorOccurred)
                {
                    _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, "Stream bağlantısı kurulamadı. URL'yi kontrol edin."));
                    return;
                }

                // Hata yoksa döngüden çık
                break;
            }
            catch (UriFormatException)
            {
                _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, "Geçersiz stream URL'si."));
                return;
            }
            catch (Exception ex)
            {
                var message = UserFriendlyErrorMessage.WithPrefix("Oynatma baslatilamadi", ex);
                _dispatcherService.BeginInvoke(() => ErrorOccurred?.Invoke(this, message));
                return;
            }
        }
    }
    public void Pause()
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.SetPause(true);
        }
    }

    public void Resume()
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.SetPause(false);
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _playGeneration);
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = null;
        StopQualityMonitoring();
        if (_mediaPlayer == null)
        {
            return;
        }

        _mediaPlayer.Stop();

        // Clear previous frame so failed loads do not leave stale video content visible.
        var currentMedia = _mediaPlayer.Media;
        _mediaPlayer.Media = null;
        currentMedia?.Dispose();
    }

    public int Volume
    {
        get => _currentVolume;
        set
        {
            var oldVolume = _currentVolume;
            _currentVolume = Math.Clamp(value, 0, 100);
            
            if (_mediaPlayer != null)
                _mediaPlayer.Volume = _currentVolume;

            if (oldVolume != _currentVolume)
            {
                VolumeChanged?.Invoke(this, _currentVolume);
                
                // Debounced Persistence: Update settings immediately but delay disk I/O
                if (_settingsService != null)
                {
                    _settingsService.Settings.DefaultVolume = _currentVolume;
                    _volumeSaveCts?.Cancel();
                    _volumeSaveCts?.Dispose();
                    _volumeSaveCts = new CancellationTokenSource();
                    
                    var token = _volumeSaveCts.Token;
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await Task.Delay(1000, token);
                            if (!token.IsCancellationRequested)
                            {
                                await _settingsService.SaveAsync();
                            }
                        }
                        catch (TaskCanceledException) { }
                        catch (Exception ex)
                        {
                            LogDebug($"Failed to persist volume: {ex.Message}");
                        }
                    }, token);
                }
            }
        }
    }

    public bool IsMuted
    {
        get => _mediaPlayer?.Mute ?? false;
        set
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Mute = value;
        }
    }

    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

    public double Position
    {
        get => (_mediaPlayer?.Position ?? 0) * Duration;
        set
        {
            if (_mediaPlayer != null && Duration > 0 && !double.IsNaN(value) && !double.IsInfinity(value))
                _mediaPlayer.Position = (float)(value / Duration);
        }
    }

    public float PlaybackRate
    {
        get => _mediaPlayer?.Rate ?? 1.0f;
        set
        {
            if (_mediaPlayer != null)
                _mediaPlayer.SetRate(value);
        }
    }

    public double Duration
    {
        get
        {
            var length = _mediaPlayer?.Length ?? 0;
            return length > 0 ? length / 1000.0 : 0;
        }
    }

    public IReadOnlyList<(int Id, string? Name)> AudioTracks
    {
        get
        {
            if (_mediaPlayer == null) return Array.Empty<(int, string?)>();
            
            var tracks = new List<(int, string?)>();
            var description = _mediaPlayer.AudioTrackDescription;
            if (description == null) return tracks;
            
            foreach (var track in description)
            {
                tracks.Add((track.Id, track.Name));
            }
            
            return tracks;
        }
    }

    public IReadOnlyList<(int Id, string? Name)> SubtitleTracks
    {
        get
        {
            if (_mediaPlayer == null) return Array.Empty<(int, string?)>();
            
            var tracks = new List<(int, string?)>();
            var description = _mediaPlayer.SpuDescription;
            if (description == null) return tracks;
            
            foreach (var track in description)
            {
                tracks.Add((track.Id, track.Name));
            }
            
            return tracks;
        }
    }

    public void SetAudioTrack(int trackId)
    {
        if (_mediaPlayer != null)
            _mediaPlayer.SetAudioTrack(trackId);
    }

    public void SetSubtitleTrack(int trackId)
    {
        if (_mediaPlayer == null)
        {
            return;
        }

        if (trackId >= 0)
        {
            _mediaPlayer.SetSpu(trackId);
            return;
        }

        // Phase 28: Improved subtitle disabling
        // LibVLC uses -1 for OFF usually, but some streams respond better to 0 or repeated calls.
        _mediaPlayer.SetSpu(-1);

        // Check if it actually changed, if not, try 0
        if (_mediaPlayer.Spu != -1)
        {
            _mediaPlayer.SetSpu(0);
        }
    }
    private void StartQualityMonitoring()
    {
        StopQualityMonitoring();
        var generation = Interlocked.Read(ref _playGeneration);
        if (_mediaPlayer?.Media == null)
        {
            return;
        }
        _qualityMonitorCts = new CancellationTokenSource();
        _ = MonitorStreamQualityAsync(_qualityMonitorCts.Token, generation);
    }

    private void StopQualityMonitoring()
    {
        if (_qualityMonitorCts == null) return;
        try
        {
            _qualityMonitorCts.Cancel();
            _qualityMonitorCts.Dispose();
        }
        catch
        {
            // no-op
        }
        finally
        {
            _qualityMonitorCts = null;
        }
    }

    private async Task MonitorStreamQualityAsync(CancellationToken cancellationToken, long generation)
    {
        // Adaptive streamlerde ilk kalite yanlış/eksik gelebilir; birkaç kez yeniden ölç.
        var delaysMs = new[] { 1500, 2500, 3000, 5000, 7000 };
        for (var i = 0; i < delaysMs.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested || _mediaPlayer == null || !_mediaPlayer.IsPlaying)
            {
                return;
            }

            if (generation != Interlocked.Read(ref _playGeneration))
            {
                return;
            }

            try
            {
                await Task.Delay(delaysMs[i], cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await DetectStreamQualitySnapshotAsync(cancellationToken, generation);
        }
    }

    private async Task DetectStreamQualitySnapshotAsync(CancellationToken cancellationToken, long generation)
    {
        try
        {
            if (_mediaPlayer?.Media == null || cancellationToken.IsCancellationRequested) return;
            if (generation != Interlocked.Read(ref _playGeneration)) return;

            // Parse the media to get track info
            var media = _mediaPlayer.Media;
            if (media == null) return;
            
            await media.Parse(MediaParseOptions.ParseNetwork, timeout: 5000);
            if (generation != Interlocked.Read(ref _playGeneration)) return;

            var measured = new StreamQualityInfo();

            var tracks = media.Tracks;
            if (tracks == null) return;

            foreach (var track in tracks)
            {
                if (track.TrackType == TrackType.Video)
                {
                    var videoTrack = track.Data.Video;
                    measured.Width = Math.Max(measured.Width, (int)videoTrack.Width);
                    measured.Height = Math.Max(measured.Height, (int)videoTrack.Height);
                    var trackFps = videoTrack.FrameRateNum > 0 && videoTrack.FrameRateDen > 0
                        ? (int)Math.Round((double)videoTrack.FrameRateNum / videoTrack.FrameRateDen, MidpointRounding.AwayFromZero)
                        : 0;
                    measured.Fps = Math.Max(measured.Fps, trackFps);
                    measured.VideoCodec = track.Codec > 0 
                        ? FourCCToString(track.Codec) 
                        : track.Description ?? "";
                    measured.VideoBitrate = Math.Max(measured.VideoBitrate, (int)track.Bitrate);
                }
                else if (track.TrackType == TrackType.Audio)
                {
                    var audioTrack = track.Data.Audio;
                    measured.AudioChannels = Math.Max(measured.AudioChannels, (int)audioTrack.Channels);
                    measured.AudioCodec = track.Codec > 0 
                        ? FourCCToString(track.Codec)
                        : track.Description ?? "";
                    measured.AudioBitrate = Math.Max(measured.AudioBitrate, (int)(track.Bitrate / 1000)); // bps → kbps
                }
            }

            var runtimeFps = _mediaPlayer.Fps;
            if (runtimeFps > 0)
            {
                measured.Fps = Math.Max(measured.Fps, (int)Math.Round(runtimeFps, MidpointRounding.AwayFromZero));
            }

            StreamQualityInfo merged;
            lock (_qualitySync)
            {
                merged = MergeQuality(StreamQuality, measured);
                StreamQuality = merged;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[VideoPlayerService] Quality detected: {merged.Width}x{merged.Height} " +
                $"@{merged.Fps}fps, {merged.VideoCodec}, {merged.VideoBitrate}bps | " +
                $"Audio: {merged.AudioCodec} {merged.AudioChannels}ch {merged.AudioBitrate}kbps");

            _dispatcherService.BeginInvoke(() => QualityDetected?.Invoke(this, merged));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPlayerService] Quality detection error: {ex.Message}");
        }
    }

    private static StreamQualityInfo MergeQuality(StreamQualityInfo? current, StreamQualityInfo measured)
    {
        if (current == null) return measured;

        return new StreamQualityInfo
        {
            Width = Math.Max(current.Width, measured.Width),
            Height = Math.Max(current.Height, measured.Height),
            Fps = Math.Max(current.Fps, measured.Fps),
            VideoBitrate = Math.Max(current.VideoBitrate, measured.VideoBitrate),
            VideoCodec = !string.IsNullOrWhiteSpace(measured.VideoCodec) ? measured.VideoCodec : current.VideoCodec,
            AudioBitrate = Math.Max(current.AudioBitrate, measured.AudioBitrate),
            AudioChannels = Math.Max(current.AudioChannels, measured.AudioChannels),
            AudioCodec = !string.IsNullOrWhiteSpace(measured.AudioCodec) ? measured.AudioCodec : current.AudioCodec
        };
    }

    /// <summary>
    /// FourCC codec code → human-readable string
    /// </summary>
    private static string FourCCToString(uint fourcc)
    {
        if (fourcc == 0) return "";
        var bytes = BitConverter.GetBytes(fourcc);
        var chars = new char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = bytes[i] >= 32 && bytes[i] < 127 ? (char)bytes[i] : '?';
        }
        return new string(chars).TrimEnd('?', '\0').Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        if (_settingsService != null)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }

        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = null;
        
        _volumeSaveCts?.Cancel();
        _volumeSaveCts?.Dispose();
        _volumeSaveCts = null;

        StopQualityMonitoring();
        _mediaPlayer?.Stop();
        _mediaPlayer?.Dispose();
        _libVLC?.Dispose();
        _initLock.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static StreamProfile DetectStreamProfile(string url)
    {
        var lower = url.ToLowerInvariant();
        if (lower.Contains(".m3u8") || lower.Contains("manifest.m3u8")) return StreamProfile.LiveM3u8;
        if (lower.EndsWith(".mkv") || lower.Contains("/mkv/") || lower.Contains("format=mkv")) return StreamProfile.VodMkv;
        if (lower.EndsWith(".mp4") || lower.Contains("/mp4/") || lower.Contains("format=mp4")) return StreamProfile.VodMp4;
        if (lower.EndsWith(".ts") || lower.Contains("/live/") || lower.Contains("stream_type=live")) return StreamProfile.LiveTs;
        if (lower.Contains("/movie/") || lower.Contains("/series/"))
        {
            if (lower.Contains(".mkv")) return StreamProfile.VodMkv;
            return StreamProfile.VodMp4;
        }
        // Uzantısız URL — demuxer zorlaması yapma, VLC kendi algılasın
        return StreamProfile.Unknown;
    }

    private enum StreamProfile
    {
        LiveTs,
        LiveM3u8,
        VodMkv,
        VodMp4,
        Unknown
    }
}



