using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

// ─── Ortak Fake Altyapısı ─────────────────────────────────────────────────────
// Bu dosya, PlayerViewModelControlsTests.cs'deki Fake sınıflarını tekrar tanımlamamak
// için onları doğrudan kullanır. Eğer testler tek bir assembly'de derlenmiyorsa
// bu sınıfları buraya kopyalamanız yeterlidir.
//
// Aşağıdaki "CriticalScenario" namespace'i, mevcut fake'lerin aynı namespace'de
// olduğunu varsayar (her ikisi de Noctra.Tests altında).

namespace Noctra.Tests
{
    // ─── Genişletilmiş Fake'ler ───────────────────────────────────────────────────

    /// <summary>
    /// Playback-ended ve buffering event'lerini dışarıdan tetikleyebilen
    /// genişletilmiş video servisi.
    /// </summary>
    internal sealed class ExtendedFakeVideoService : IVideoPlayerService
    {
        public string? CurrentUrl { get; private set; }
        public StreamQualityInfo? StreamQuality => null;
        public bool IsPlaying { get; private set; }
        public PlaybackState State => IsPlaying ? PlaybackState.Playing : PlaybackState.Stopped;
        public bool HasLoadedMedia => CurrentUrl != null;
        public long CurrentTimeMilliseconds => (long)(Position * 1000);
        public double Position { get; set; }
        public double Duration { get; set; } = 3600;
        public float PlaybackRate { get; set; } = 1f;
        public int Volume { get; set; } = 100;
        public bool IsMuted { get; set; }
        public IReadOnlyList<(int Id, string? Name)> AudioTracks => Array.Empty<(int, string?)>();
        public IReadOnlyList<(int Id, string? Name)> SubtitleTracks => Array.Empty<(int, string?)>();

        public int StopCallCount { get; private set; }
        public int PlayCallCount { get; private set; }

        public event EventHandler<bool>? PlayingChanged;
        public event EventHandler<double>? PositionChanged;
        public event EventHandler? PlaybackEnded;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StreamQualityInfo>? QualityDetected;
        public event EventHandler<int>? VolumeChanged;
        public event EventHandler<float>? BufferingChanged;
        public event EventHandler<string?>? SubtitleTextChanged;
        public event EventHandler? PlayerReady;

        public Task PlayAsync(string url, double startTimeSeconds = 0)
        {
            CurrentUrl = url;
            IsPlaying = true;
            PlayCallCount++;
            PlayingChanged?.Invoke(this, true);
            return Task.CompletedTask;
        }

        public Task ReinitializeAsync() => Task.CompletedTask;

        public Task HardSeekAsync(double seconds) { Position = seconds; return Task.CompletedTask; }
        public void Pause() { IsPlaying = false; PlayingChanged?.Invoke(this, false); }
        public void Resume() { IsPlaying = true; PlayingChanged?.Invoke(this, true); }
        public void Stop() { IsPlaying = false; CurrentUrl = null; StopCallCount++; }
        public Task EndSessionAsync(CancellationToken cancellationToken = default) { Stop(); return Task.CompletedTask; }
        public void SetAudioTrack(int trackId) { }
        public void SetSubtitleTrack(int trackId) { }
        public void SeekToTime(long milliseconds) => Position = milliseconds / 1000.0;
        public void PlayLoadedMedia() => Resume();
        public void SetVideoLayout(string? aspectRatio, string? cropGeometry) { }
        public void Dispose() { }

        // ─── Trigger helpers ───────────────────────────────────────────────────
        public void FirePlaybackEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        public void FirePositionChanged(double pos) => PositionChanged?.Invoke(this, pos);
        public void FirePlayingChanged(bool playing) { IsPlaying = playing; PlayingChanged?.Invoke(this, playing); }
        public void FireError(string msg) => ErrorOccurred?.Invoke(this, msg);
        public void FireBufferingChanged(float progress) => BufferingChanged?.Invoke(this, progress);
    }

    /// <summary>
    /// ILicenseService: premium özelliği kontrol edilebilir.
    /// </summary>
    internal sealed class ScenarioLicenseService : ILicenseService
    {
        public bool IsPremium { get; set; } = true;
        public bool CanUpgradeToPremium => !IsPremium;
        public bool IsEditionLockedPremium => false;
        public SubscriptionTier CurrentTier => IsPremium ? SubscriptionTier.Premium : SubscriptionTier.Free;
        public bool IsFeatureAvailable(string feature) => IsPremium;
        public event Action? SubscriptionChanged;

        public void ActivatePremium() { IsPremium = true; SubscriptionChanged?.Invoke(); }
        public void DeactivatePremium() { IsPremium = false; SubscriptionChanged?.Invoke(); }
        public string GetPriceText() => "0";        public SubscriptionInfo GetCurrentSubscription() => new() { Tier = CurrentTier };
        public bool IsWithinLimit(string limit, int count) => true;
        public int GetLimit(string limit) => int.MaxValue;
        public Task<bool> StartPurchaseFlowAsync(SubscriptionTier targetTier) => Task.FromResult(true);
        public Task RefreshSubscriptionStatusAsync() => Task.CompletedTask;
        public void SetTierForTesting(SubscriptionTier tier) { IsPremium = tier == SubscriptionTier.Premium; SubscriptionChanged?.Invoke(); }
    }

    internal sealed class ScenarioSettingsService : ISettingsService
    {
        public AppSettings Settings { get; } = new AppSettings
        {
            DefaultVolume = 80,
            AutoPlayNext = false
        };
        public event Action? SettingsChanged;
        public Task LoadAsync() => Task.CompletedTask;
        public Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds) => Task.FromResult(0);
        public Task LoadProfileSettingsAsync(int profileId) => Task.CompletedTask;
        public Task<AppSettings?> PeekProfileSettingsAsync(int profileId) => Task.FromResult<AppSettings?>(Settings);
        public Task SaveAsync() => Task.CompletedTask;
        public void ResetToDefaults() { }
    }

    internal sealed class CapturingWatchHistoryService : IWatchHistoryService
    {
        public record Call(int ProfileId, int? ChannelId, int? EpisodeId,
            TimeSpan Position, bool Completed, TimeSpan? Duration);

        public List<Call> Calls { get; } = new();

        public Task TrackWatchAsync(int profileId, int? channelId, int? episodeId,
            TimeSpan position, bool completed = false, TimeSpan? duration = null,
            TimeSpan? incrementDelta = null, bool allowReset = false, CancellationToken ct = default)
        {
            Calls.Add(new Call(profileId, channelId, episodeId, position, completed, duration));
            return Task.CompletedTask;
        }

        public Task<List<WatchHistory>> GetHistoryAsync(int profileId, int skip = 0, int take = 50, CancellationToken ct = default) => Task.FromResult(new List<WatchHistory>());
        public Task DeleteProfileHistoryAsync(int profileId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WatchHistory?> GetLatestForMediaAsync(int profileId, int? channelId, int? episodeId, CancellationToken ct = default) => Task.FromResult<WatchHistory?>(null);
        public Task CleanupOlderThanDaysAsync(int profileId, int days, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveFromHistoryAsync(int profileId, int? channelId, int? episodeId, CancellationToken ct = default) => Task.CompletedTask;
    }

    // ─── Kritik Senaryo Test Bağlamı ─────────────────────────────────────────────

    internal sealed class ScenarioContext
    {
        public ExtendedFakeVideoService VideoService { get; } = new();
        public ScenarioLicenseService License { get; } = new();
        public ScenarioSettingsService Settings { get; } = new();
        public CapturingWatchHistoryService WatchHistory { get; } = new();
        public PlayerViewModel VM { get; }

        public ScenarioContext(bool isPremium = true)
        {
            License.IsPremium = isPremium;

            VM = new PlayerViewModel(
                VideoService,
                new FakeEpgService(),
                new FakeMetadataService(),
                new FakeMediaService(),
                new FakeContentDownloadService(),
                new FakeNetworkService(),
                new SyncDispatcher(),
                Settings,
                License,
                new LocalizationService(),
                null!,  // MainViewModel — not needed for these tests
                WatchHistory,
                new FakeStalkerPortalService());
        }

        // ─── Yardımcı Reflection ─────────────────────────────────────────────

        public void InvokePrivate(string method, params object?[] args)
        {
            var mi = typeof(PlayerViewModel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(mi != null, $"Method '{method}' not found.");
            mi!.Invoke(VM, args);
        }

        public T? GetPrivateField<T>(string fieldName)
        {
            var fi = typeof(PlayerViewModel).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(fi != null, $"Field '{fieldName}' not found.");
            return (T?)fi!.GetValue(VM);
        }

        public void SetPrivateField(string fieldName, object? value)
        {
            var fi = typeof(PlayerViewModel).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(fi != null, $"Field '{fieldName}' not found.");
            fi!.SetValue(VM, value);
        }

        /// <summary>
        /// Kullanım kolaylığı için tam olarak dolu bir VOD kanalı kurar.
        /// </summary>
        public Channel SetupVodChannel(double durationSeconds = 3600, int id = 42)
        {
            var ch = new Channel { Id = id, Name = "Test Film", Type = ChannelType.VOD, StreamUrl = "http://test/film.mp4" };
            VM.CurrentChannel = ch;
            VM.IsLiveContent = false;
            VM.Duration = durationSeconds;
            VM.Position = 0;
            VideoService.Duration = durationSeconds;
            VM.CurrentProfileId = 1;
            VideoService.FirePlayingChanged(true);
            return ch;
        }

        public Channel SetupLiveChannel(int id = 99)
        {
            var ch = new Channel { Id = id, Name = "TRT 1", Type = ChannelType.Live, StreamUrl = "http://test/live.m3u8" };
            VM.CurrentChannel = ch;
            VM.IsLiveContent = true;
            VM.CurrentProfileId = 1;
            VideoService.FirePlayingChanged(true);
            return ch;
        }

        /// <summary>Dizi oynatma bağlamı kurar (bölüm + sonraki bölüm).</summary>
        public (Channel channel, Episode ep, Episode nextEp) SetupSeriesPlayback(double duration = 2700)
        {
            var ep = new Episode { Id = 1, Name = "S01E01", StreamUrl = "http://test/ep1.mp4" };
            var nextEp = new Episode { Id = 2, Name = "S01E02", StreamUrl = "http://test/ep2.mp4" };
            var ch = new Channel { Id = 10, Name = "Breaking Bad", Type = ChannelType.Series, StreamUrl = ep.StreamUrl };

            VM.CurrentChannel = ch;
            VM.IsLiveContent = false;
            VM.IsSeriesContent = true;
            VM.Duration = duration;
            VideoService.Duration = duration;
            VM.CurrentProfileId = 1;
            VM.SetCurrentEpisode(ep, nextEp);
            VideoService.FirePlayingChanged(true);
            return (ch, ep, nextEp);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // KRİTİK SENARYO TESTLERİ
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Uygulama kullanımındaki kritik iş senaryolarını uçtan uca test eder.
    ///
    /// Bölümler:
    ///   A.  Erken Bitiş Tespiti — isPrematureEnd mantığı
    ///   B.  Erken Bitiş Kurtarma Limiti — max 5 deneme, cooldown
    ///   C.  Credits / Sonraki Bölüm Promptu
    ///   D.  İzleme Geçmişi Yazımı (FlushWatchHistory)
    ///   E.  Buffering → IsBuffering durumu
    ///   F.  İçerik Geçişinde Eski Olayları Filtreleme
    ///   G.  Hata → Bağlantı Durumu
    ///   H.  Stop — Durum Temizleme
    ///   I.  SetCurrentEpisode — Bölüm Değişimi Sıfırlama
    ///   J.  Resume Dialog + Watch History Entegrasyonu
    ///   K.  Dizi Tamamlanma Bayrağı Koruma
    /// </summary>
    public class CriticalScenarioTests
    {
        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM A: isPrematureEnd mantığı
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Canlı yayında PlaybackEnded gelirse HER ZAMAN premature end sayılır.
        /// Beklenti: AutoRecoverPrematureEndAsync çağrılmadan önce IsBuffering=true, bağlantı
        /// durumu güncellenir. Testde bunu _prematureEndRecoveryCount > 0 ile doğrularız.
        /// </summary>
        [Fact]
        public void PlaybackEnded_LiveContent_TriggersPrematureEndRecovery()
        {
            var ctx = new ScenarioContext();
            ctx.SetupLiveChannel();

            ctx.VideoService.FirePlaybackEnded();

            // Kurtarma başladıysa sayaç artmış olmalı
            var count = ctx.GetPrivateField<int>("_prematureEndRecoveryCount");
            Assert.True(count > 0,
                "Canlı yayında PlaybackEnded → premature end kurtarma tetiklenmeli.");
        }

        /// <summary>
        /// VOD içerikte oynatma ERKEN biterse (bitime >10sn) premature end kurtarması tetiklenir.
        /// </summary>
        [Fact]
        public void PlaybackEnded_VodWithMoreThan10SecondsRemaining_TriggersPrematureEnd()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel(durationSeconds: 3600);
            ctx.VM.Position = 1000; // 2600sn kaldı → erken bitiş
            ctx.VideoService.Duration = 3600;

            ctx.VideoService.FirePlaybackEnded();

            var count = ctx.GetPrivateField<int>("_prematureEndRecoveryCount");
            Assert.True(count > 0,
                "VOD'da >10sn kalan sürede PlaybackEnded → erken bitiş kurtarması olmalı.");
        }

        /// <summary>
        /// VOD içerikte oynatma GERÇEKTEN TAMAMLANIRSA (son 10sn içinde) premature end
        /// tetiklenmemeli, sonraki bölüm promptu gösterilmeli.
        /// </summary>
        [Fact]
        public void PlaybackEnded_VodAtEnd_DoesNotTriggerPrematureEnd()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel(durationSeconds: 3600);
            ctx.VM.Position = 3595; // 5sn kaldı → gerçek bitiş
            ctx.VideoService.Duration = 3600;

            ctx.VideoService.FirePlaybackEnded();

            var count = ctx.GetPrivateField<int>("_prematureEndRecoveryCount");
            Assert.True(0 == count,
                "VOD son 10sn'de bitti → erken bitiş değil, kurtarma sayacı 0 kalmalı.");
        }

        /// <summary>
        /// Dizi bitişinde (bitime <10sn) ve NextEpisode varsa, krediler (credits) promptu gösterilmeli.
        /// </summary>
        [Fact]
        public void PlaybackEnded_SeriesAtEnd_WithNextEpisode_ShowsNextEpisodePrompt()
        {
            var ctx = new ScenarioContext();
            var (_, _, _) = ctx.SetupSeriesPlayback(duration: 2700);
            ctx.VM.Position = 2695; // son 5sn
            ctx.VideoService.Duration = 2700;

            ctx.VideoService.FirePlaybackEnded();

            Assert.True(ctx.VM.IsNextEpisodePromptVisible,
                "Dizi biterken NextEpisode varsa prompt görünmeli.");
        }

        /// <summary>
        /// NextEpisode yoksa PlaybackEnded → sonraki bölüm promptu gösterilmez.
        /// </summary>
        [Fact]
        public void PlaybackEnded_SeriesAtEnd_WithNoNextEpisode_DoesNotShowPrompt()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel(durationSeconds: 2700);

            var ep = new Episode { Id = 5, Name = "Son Bölüm" };
            var ch = new Channel { Id = 10, Name = "Dizi", Type = ChannelType.Series };
            ctx.VM.CurrentChannel = ch;
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 2700;
            ctx.VideoService.Duration = 2700;
            ctx.VM.SetCurrentEpisode(ep, nextEpisode: null); // Sonraki bölüm yok
            ctx.VideoService.FirePlayingChanged(true);
            ctx.VM.Position = 2695;

            ctx.VideoService.FirePlaybackEnded();

            Assert.False(ctx.VM.IsNextEpisodePromptVisible,
                "NextEpisode null iken prompt gösterilmemeli.");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM B: Erken Bitiş Kurtarma Limiti (Max 5, Cooldown)
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void PrematureEnd_AfterMaxRecoveries_SetsUnstableConnectionStatus()
        {
            var ctx = new ScenarioContext();
            ctx.SetupLiveChannel();

            // _prematureEndRecoveryCount'u manuel olarak limite taşı
            ctx.SetPrivateField("_prematureEndRecoveryCount", 5); // MaxPrematureEndRecoveries = 5
            // _lastPrematureEndRecoveryUtc'yi eskiye taşı (cooldown geçsin)
            ctx.SetPrivateField("_lastPrematureEndRecoveryUtc",
                DateTime.UtcNow - TimeSpan.FromSeconds(10));

            ctx.VideoService.FirePlaybackEnded();

            Assert.Contains("unstable", ctx.VM.ConnectionStatus,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PrematureEnd_WithinCooldown_DoesNotIncrementCounter()
        {
            var ctx = new ScenarioContext();
            ctx.SetupLiveChannel();

            ctx.SetPrivateField("_prematureEndRecoveryCount", 2);
            // Cooldown aktif (son 3sn içinde)
            ctx.SetPrivateField("_lastPrematureEndRecoveryUtc",
                DateTime.UtcNow - TimeSpan.FromSeconds(1));

            ctx.VideoService.FirePlaybackEnded();

            // Cooldown aktifken sayaç artmaz
            var count = ctx.GetPrivateField<int>("_prematureEndRecoveryCount");
            Assert.True(2 == count,
                "Cooldown süresi dolmadan kurtarma sayacı artmamalı.");
        }

        [Fact]
        public void PrematureEnd_AfterWindowReset_ResetsCounter()
        {
            var ctx = new ScenarioContext();
            ctx.SetupLiveChannel();

            ctx.SetPrivateField("_prematureEndRecoveryCount", 4);
            // 2 dakika + 1sn geçti → pencere sıfırlanmalı
            ctx.SetPrivateField("_lastPrematureEndRecoveryUtc",
                DateTime.UtcNow - TimeSpan.FromMinutes(3));

            ctx.VideoService.FirePlaybackEnded();

            // Pencere sıfırlanmışsa sayaç 1'e düşmüş olmalı (0 → 1 ilk denemede)
            var count = ctx.GetPrivateField<int>("_prematureEndRecoveryCount");
            Assert.True(count <= 2,
                $"Pencere sıfırlandıktan sonra sayaç {count} olmamalıydı (beklenen ≤2).");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM C: Credits Zone / Sonraki Bölüm Promptu
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void CheckIntroCreditsPosition_WhenNearEnd_SetsIsCreditsZoneTrue()
        {
            var ctx = new ScenarioContext();
            var (_, _, _) = ctx.SetupSeriesPlayback(duration: 2700);

            // Son %6 bölge = 2700 * 0.94 = ~2538sn
            ctx.VM.SeekCommand.Execute(2600.0);

            Assert.True(ctx.VM.IsCreditsZone,
                "Son bölgeye seek edilince IsCreditsZone=true olmalı.");
        }

        [Fact]
        public void CheckIntroCreditsPosition_WhenNearEnd_ShowsNextEpisodePrompt()
        {
            var ctx = new ScenarioContext();
            ctx.SetupSeriesPlayback(duration: 2700);

            ctx.VM.SeekCommand.Execute(2600.0);

            Assert.True(ctx.VM.IsNextEpisodePromptVisible,
                "Credits zone'a girilince NextEpisodePrompt görünmeli.");
        }

        [Fact]
        public void CheckIntroCreditsPosition_SeekingBack_ExitsCreditsZone()
        {
            var ctx = new ScenarioContext();
            ctx.SetupSeriesPlayback(duration: 2700);

            // Credits zone'a gir
            ctx.VM.SeekCommand.Execute(2600.0);
            Assert.True(ctx.VM.IsCreditsZone);

            // Çok geri sar (eşiğin 3sn öncesinin de gerisine)
            ctx.VM.SeekCommand.Execute(2000.0);

            Assert.False(ctx.VM.IsCreditsZone,
                "Credits zone'un çok gerisine seeklenince IsCreditsZone=false olmalı.");
            Assert.False(ctx.VM.IsNextEpisodePromptVisible,
                "Credits zone'dan çıkılınca prompt kapanmalı.");
        }

        [Fact]
        public void CreditsZone_NotTriggered_WhenNoNextEpisode()
        {
            var ctx = new ScenarioContext();

            var ep = new Episode { Id = 7, Name = "Son Bölüm" };
            ctx.VM.CurrentChannel = new Channel { Id = 10, Type = ChannelType.Series };
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 2700;
            ctx.VideoService.Duration = 2700;
            ctx.VM.SetCurrentEpisode(ep, nextEpisode: null);
            ctx.VideoService.FirePlayingChanged(true);

            ctx.VM.SeekCommand.Execute(2600.0);

            Assert.False(ctx.VM.IsNextEpisodePromptVisible,
                "NextEpisode olmadan credits zone promptu gösterilmemeli.");
        }

        [Fact]
        public void NewEpisode_Set_ResetsCreditsTrigger()
        {
            var ctx = new ScenarioContext();
            var (_, ep1, ep2) = ctx.SetupSeriesPlayback(duration: 2700);

            // Credits zone'a gir
            ctx.VM.SeekCommand.Execute(2600.0);
            Assert.True(ctx.VM.IsCreditsZone);

            // Yeni bölüm başlat
            var ep3 = new Episode { Id = 3, Name = "S01E03", StreamUrl = "http://test/ep3.mp4" };
            ctx.VM.SetCurrentEpisode(ep2, nextEpisode: ep3);

            Assert.False(ctx.VM.IsCreditsZone,
                "Yeni bölüm ayarlandığında IsCreditsZone sıfırlanmalı.");
            Assert.False(ctx.VM.IsNextEpisodePromptVisible,
                "Yeni bölümde prompt kapalı olmalı.");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM D: İzleme Geçmişi Yazımı (FlushWatchHistory)
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Stop_FlushesWatchHistory()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.Position = 900;

            await ctx.VM.StopCommand.ExecuteAsync(null);

            Assert.NotEmpty(ctx.WatchHistory.Calls);
        }

        [Fact]
        public async Task Stop_WatchHistory_IncludesCurrentPosition()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel(durationSeconds: 3600);
            ctx.VM.Position = 1800;

            await ctx.VM.StopCommand.ExecuteAsync(null);

            var lastCall = ctx.WatchHistory.Calls[^1];
            Assert.True(lastCall.Position.TotalSeconds >= 1799 && lastCall.Position.TotalSeconds <= 1801,
                $"Watch history pozisyonu 1800sn olmalı, {lastCall.Position.TotalSeconds} geldi.");
        }

        [Fact]
        public async Task Stop_WatchHistory_MarksCompletedWhenAt90Percent()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel(durationSeconds: 3600);
            ctx.VM.Position = 3241; // >%90

            await ctx.VM.StopCommand.ExecuteAsync(null);

            var lastCall = ctx.WatchHistory.Calls[^1];
            Assert.True(lastCall.Completed,
                "Pozisyon %90'ı geçmişse watch history 'Completed=true' kaydetmeli.");
        }

        [Fact]
        public async Task Stop_WatchHistory_NotMarkedCompleted_WhenUnder90Percent()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel(durationSeconds: 3600);
            ctx.VM.Position = 1800; // %50

            await ctx.VM.StopCommand.ExecuteAsync(null);

            var lastCall = ctx.WatchHistory.Calls[^1];
            Assert.False(lastCall.Completed,
                "Pozisyon %90'ın altında → 'Completed=false' kaydedilmeli.");
        }

        [Fact]
        public async Task Stop_WithEpisodePlayback_UsesEpisodeId()
        {
            var ctx = new ScenarioContext();
            var (_, ep, _) = ctx.SetupSeriesPlayback(duration: 2700);
            ctx.VM.Position = 1000;

            await ctx.VM.StopCommand.ExecuteAsync(null);

            var call = ctx.WatchHistory.Calls[^1];
            Assert.Equal(ep.Id, call.EpisodeId);
            Assert.Null(call.ChannelId);
        }

        [Fact]
        public async Task Stop_WithVodPlayback_UsesChannelId()
        {
            var ctx = new ScenarioContext();
            var ch = ctx.SetupVodChannel(id: 77);
            ctx.VM.Position = 500;

            await ctx.VM.StopCommand.ExecuteAsync(null);

            var call = ctx.WatchHistory.Calls[^1];
            Assert.Equal(77, call.ChannelId);
            Assert.Null(call.EpisodeId);
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM E: Buffering → IsBuffering durumu
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void BufferingProgress100_WhenPlaying_SetsIsBufferingFalse()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.IsBuffering = true;

            ctx.VideoService.FireBufferingChanged(100f);

            Assert.False(ctx.VM.IsBuffering,
                "BufferingProgress=100 ve IsPlaying=true → IsBuffering=false olmalı.");
        }

        [Fact]
        public void BufferingProgress50_KeepsIsBufferingTrue()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.IsBuffering = true;

            ctx.VideoService.FireBufferingChanged(50f);

            Assert.True(ctx.VM.IsBuffering,
                "BufferingProgress=50 → hâlâ yükleniyor, IsBuffering=true kalmalı.");
        }

        [Fact]
        public void BufferingProgress_Updates_BufferingProgressProperty()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();

            ctx.VideoService.FireBufferingChanged(73.5f);

            Assert.Equal(73.5, ctx.VM.BufferingProgress, precision: 1);
        }

        [Fact]
        public void BufferingFinished_SetsPlayerLoadingWarningMessageEmpty()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.PlayerLoadingWarningMessage = "Yükleniyor...";

            ctx.VideoService.FirePlayingChanged(false); // durdur
            ctx.VideoService.FireBufferingChanged(100f);

            // 100% buffering bittiğinde warning temizlenmiş olmalı
            // (test: IsPlaying=false → mesaj korunabilir; IsPlaying=true ise temizlenir)
            // Bu senaryoyu IsPlaying=true ile test edelim:
            ctx.VideoService.FirePlayingChanged(true);
            ctx.VideoService.FireBufferingChanged(100f);

            Assert.Equal(string.Empty, ctx.VM.PlayerLoadingWarningMessage);
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM F: İçerik Geçişinde Eski Olayları Filtreleme
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void PositionChanged_DuringContentTransition_IsIgnored()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.Position = 0;

            // İçerik geçişini simüle et (PrepareForContentLoading _isContentTransitioning=true yapar)
            ctx.InvokePrivate("PrepareForContentLoading");

            // Geçiş sırasında gelen eski position event'i
            ctx.VideoService.FirePositionChanged(999);

            // _isContentTransitioning=true iken PositionChanged görmezden gelinmeli
            Assert.True(0 == ctx.VM.Position,
                "İçerik geçişi sırasında gelen eski position event'i uygulanmamalı.");
        }

        [Fact]
        public void PlaybackEnded_DuringContentTransition_IsIgnored()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel(durationSeconds: 3600);
            ctx.VM.Position = 500;

            ctx.InvokePrivate("PrepareForContentLoading");

            // Geçiş sırasında PlaybackEnded → credits prompt tetiklenmemeli
            ctx.VideoService.FirePlaybackEnded();

            Assert.False(ctx.VM.IsNextEpisodePromptVisible,
                "İçerik geçişi sırasında PlaybackEnded → prompt tetiklenmemeli.");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM G: Hata → Bağlantı Durumu
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void ErrorOccurred_SetsIsBufferingTrue()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();

            ctx.VideoService.FireError("Stream açılamadı");

            Assert.True(ctx.VM.IsBuffering);
        }

        [Fact]
        public void ErrorOccurred_SetsConnectionStatusToErrorMessage()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();

            ctx.VideoService.FireError("Bağlantı zaman aşımı");

            Assert.Equal("Bağlantı zaman aşımı", ctx.VM.ConnectionStatus);
        }

        [Fact]
        public void ErrorOccurred_ResetsBufferingProgressToZero()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.BufferingProgress = 60;

            ctx.VideoService.FireError("Hata");

            Assert.Equal(0, ctx.VM.BufferingProgress);
        }

        [Fact]
        public void ErrorOccurred_ClearsPlayerLoadingWarningMessage()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.PlayerLoadingWarningMessage = "5 saniye içinde...";

            ctx.VideoService.FireError("Akış kesildi");

            // Hata gelince mevcut warning temizlenmeli
            Assert.Equal(string.Empty, ctx.VM.PlayerLoadingWarningMessage);
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM H: Stop — Durum Temizleme
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Stop_ClearsCurrentChannel()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();

            await ctx.VM.StopCommand.ExecuteAsync(null);

            Assert.Null(ctx.VM.CurrentChannel);
        }

        [Fact]
        public async Task Stop_SetsIsVisibleTrue()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.IsVisible = false;

            await ctx.VM.StopCommand.ExecuteAsync(null);

            Assert.True(ctx.VM.IsVisible,
                "Stop sonrası overlay tekrar görünmeli.");
        }

        [Fact]
        public async Task Stop_CallsVideoServiceStop()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            var prevCount = ctx.VideoService.StopCallCount;

            await ctx.VM.StopCommand.ExecuteAsync(null);

            Assert.True(ctx.VideoService.StopCallCount > prevCount,
                "Stop komutu VideoService.Stop()'u çağırmalı.");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM I: SetCurrentEpisode — Bölüm Değişimi Sıfırlama
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void SetCurrentEpisode_SetsCurrentEpisodeReference()
        {
            var ctx = new ScenarioContext();
            ctx.VM.CurrentChannel = new Channel { Id = 10, Type = ChannelType.Series };
            ctx.VM.IsLiveContent = false;

            var ep = new Episode { Id = 5, Name = "S01E05" };
            ctx.VM.SetCurrentEpisode(ep);

            Assert.Equal(ep.Id, ctx.VM.CurrentEpisode?.Id);
        }

        [Fact]
        public void SetCurrentEpisode_SetsNextEpisode()
        {
            var ctx = new ScenarioContext();
            ctx.VM.CurrentChannel = new Channel { Id = 10, Type = ChannelType.Series };

            var ep = new Episode { Id = 1, Name = "S01E01" };
            var next = new Episode { Id = 2, Name = "S01E02" };
            ctx.VM.SetCurrentEpisode(ep, next);

            Assert.Equal(next.Id, ctx.VM.NextEpisode?.Id);
        }

        [Fact]
        public void SetCurrentEpisode_ResetsCreditsState()
        {
            var ctx = new ScenarioContext();
            ctx.SetupSeriesPlayback(duration: 2700);
            ctx.VM.SeekCommand.Execute(2600.0); // credits zone

            var ep2 = new Episode { Id = 2, Name = "S01E02" };
            ctx.VM.SetCurrentEpisode(ep2, nextEpisode: null);

            Assert.False(ctx.VM.IsCreditsZone);
            Assert.False(ctx.VM.IsNextEpisodePromptVisible);
        }

        [Fact]
        public void SetCurrentEpisode_Null_ClearsEpisodeContext()
        {
            var ctx = new ScenarioContext();
            ctx.SetupSeriesPlayback();

            ctx.VM.SetCurrentEpisode(null);

            Assert.Null(ctx.VM.CurrentEpisode);
            Assert.Null(ctx.VM.NextEpisode);
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM J: Resume Dialog + Watch History Entegrasyonu
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task ResumeDialog_WhenPremiumUser_IsResumable()
        {
            var ctx = new ScenarioContext(isPremium: true);
            var task = ctx.VM.ShowResumeDialogAsync(900);

            // Premium kullanıcı → resume butonu aktif
            Assert.True(ctx.VM.IsPremiumResume);
            Assert.True(ctx.VM.IsResumeDialogVisible);

            ctx.VM.CancelResumeDialog();
            try { await task; } catch { }
        }

        [Fact]
        public async Task ResumeDialog_WhenFreeUser_IsPremiumResumeFalse()
        {
            var ctx = new ScenarioContext(isPremium: false);
            var task = ctx.VM.ShowResumeDialogAsync(900);

            Assert.False(ctx.VM.IsPremiumResume);

            ctx.VM.CancelResumeDialog();
            try { await task; } catch { }
        }

        [Fact]
        public async Task ResumeDialog_PrepareForContentLoading_CancelsDialog()
        {
            var ctx = new ScenarioContext();
            var dialogTask = ctx.VM.ShowResumeDialogAsync(600);
            Assert.True(ctx.VM.IsResumeDialogVisible);

            // Yeni içerik yükleme başlatılırsa dialog iptal olmalı
            ctx.InvokePrivate("PrepareForContentLoading");

            Assert.False(ctx.VM.IsResumeDialogVisible,
                "Yeni içerik yüklenirken açık resume dialog kapatılmalı.");

            try { await dialogTask; } catch { /* TaskCanceledException beklenir */ }
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM K: Dizi Tamamlanma Bayrağı Koruma
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task WatchHistory_EpisodeCompleted_FlushedOnStop()
        {
            var ctx = new ScenarioContext();
            var (_, ep, _) = ctx.SetupSeriesPlayback(duration: 2700);

            ctx.VM.Position = 2450; // %90.7 → tamamlandı
            await ctx.VM.StopCommand.ExecuteAsync(null);

            var call = ctx.WatchHistory.Calls.Find(c => c.EpisodeId == ep.Id);
            Assert.NotNull(call);
            Assert.True(call!.Completed,
                "Stop sırasında %90'ı geçilmişse bölüm 'tamamlandı' kaydedilmeli.");
        }

        [Fact]
        public async Task WatchHistory_EpisodeNotCompleted_IsNotMarked()
        {
            var ctx = new ScenarioContext();
            var (_, ep, _) = ctx.SetupSeriesPlayback(duration: 2700);
            ctx.VM.Position = 1000; // %37

            await ctx.VM.StopCommand.ExecuteAsync(null);

            var call = ctx.WatchHistory.Calls.Find(c => c.EpisodeId == ep.Id);
            Assert.NotNull(call);
            Assert.False(call!.Completed,
                "%37'de durulduysa bölüm tamamlanmamış sayılmalı.");
        }

        [Fact]
        public async Task WatchHistory_CallsProfileId_MatchesCurrentProfileId()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel(id: 5);
            ctx.VM.CurrentProfileId = 7;
            ctx.VM.Position = 100;

            await ctx.VM.StopCommand.ExecuteAsync(null);

            var call = ctx.WatchHistory.Calls[^1];
            Assert.Equal(7, call.ProfileId);
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM L: İzleme Sırasında Kanal Değişimi
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void ChannelChange_ResetsPositionTexts()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.Position = 1000;
            ctx.VM.PositionText = "00:16:40";

            // Yeni kanal ayarla
            ctx.VM.CurrentChannel = new Channel { Id = 200, Name = "Yeni Film", Type = ChannelType.VOD };

            Assert.True(0 == ctx.VM.Position,
                "Kanal değişiminde Position sıfırlanmalı.");
            Assert.True("00:00:00" == ctx.VM.PositionText,
                "Kanal değişiminde PositionText sıfırlanmalı.");
        }

        [Fact]
        public void ChannelChange_ToLiveContent_SetsIsLiveContentTrue()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();

            ctx.VM.CurrentChannel = new Channel
            {
                Id = 99,
                Name = "TRT 1",
                Type = ChannelType.Live,
                StreamUrl = "http://test/live.m3u8"
            };

            Assert.True(ctx.VM.IsLiveContent,
                "Live tipinde kanal seçilince IsLiveContent=true olmalı.");
        }

        [Fact]
        public void ChannelChange_ToVodContent_SetsIsLiveContentFalse()
        {
            var ctx = new ScenarioContext();
            ctx.SetupLiveChannel();

            ctx.VM.CurrentChannel = new Channel
            {
                Id = 50,
                Name = "Film",
                Type = ChannelType.VOD,
                StreamUrl = "http://test/movie.mp4"
            };

            Assert.False(ctx.VM.IsLiveContent,
                "VOD tipinde kanal seçilince IsLiveContent=false olmalı.");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM M: Uç Durum — İçerik Yokken Komutlar
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void SeekCommand_WhenNoChannel_DoesNotThrow()
        {
            var ctx = new ScenarioContext();
            ctx.VM.CurrentChannel = null;
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;

            var ex = Record.Exception(() => ctx.VM.SeekCommand.Execute(500.0));
            Assert.Null(ex);
        }

        [Fact]
        public void SkipForward_WhenNoChannel_DoesNotThrow()
        {
            var ctx = new ScenarioContext();
            ctx.VM.CurrentChannel = null;
            ctx.VM.IsLiveContent = false;
            ctx.VM.Duration = 3600;
            ctx.VM.Position = 100;

            var ex = Record.Exception(() => ctx.VM.SkipForwardCommand.Execute(10));
            Assert.Null(ex);
        }

        [Fact]
        public void ToggleMute_WhenNoChannel_DoesNotThrow()
        {
            var ctx = new ScenarioContext();
            ctx.VM.CurrentChannel = null;

            var ex = Record.Exception(() => ctx.VM.ToggleMuteCommand.Execute(null));
            Assert.Null(ex);
        }

        [Fact]
        public async Task StopCommand_WhenNoChannel_DoesNotThrow()
        {
            var ctx = new ScenarioContext();
            ctx.VM.CurrentChannel = null;

            var ex = await Record.ExceptionAsync(() => ctx.VM.StopCommand.ExecuteAsync(null));
            Assert.Null(ex);
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BÖLÜM N: Oynatma Hızı (Playback Rate)
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void SetPlaybackSpeed_1x_IsDefaultRate()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();

            ctx.VM.SetPlaybackSpeedCommand.Execute(1.0f);

            Assert.Equal(1.0f, ctx.VideoService.PlaybackRate, precision: 2);
        }

        [Fact]
        public void SetPlaybackSpeed_2x_UpdatesService()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();

            ctx.VM.SetPlaybackSpeedCommand.Execute(2.0f);

            Assert.Equal(2.0f, ctx.VideoService.PlaybackRate, precision: 2);
        }

        [Fact]
        public void SetPlaybackSpeed_HalfX_UpdatesService()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();

            ctx.VM.SetPlaybackSpeedCommand.Execute(0.5f);

            Assert.Equal(0.5f, ctx.VideoService.PlaybackRate, precision: 2);
        }

        [Fact]
        public void SetPlaybackSpeed_UpdatesBindableCurrentRate()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            var currentRate = typeof(PlayerViewModel).GetProperty("CurrentPlaybackRate");

            Assert.NotNull(currentRate);
            Assert.Equal(1.0f, Assert.IsType<float>(currentRate.GetValue(ctx.VM)));

            ctx.VM.SetPlaybackSpeedCommand.Execute(1.25f);

            Assert.Equal(1.25f, Assert.IsType<float>(currentRate.GetValue(ctx.VM)));
            Assert.Equal(1.25f, ctx.VideoService.PlaybackRate, precision: 2);
        }

        [Fact]
        public void SetPlaybackSpeed_UnsupportedRateFallsBackToNormal()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            var currentRate = typeof(PlayerViewModel).GetProperty("CurrentPlaybackRate");

            Assert.NotNull(currentRate);
            ctx.VM.SetPlaybackSpeedCommand.Execute(1.1f);

            Assert.Equal(1.0f, Assert.IsType<float>(currentRate.GetValue(ctx.VM)));
            Assert.Equal(1.0f, ctx.VideoService.PlaybackRate, precision: 2);
        }

        [Fact]
        public void PrepareForContentLoading_ResetsPlaybackRate()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            var currentRate = typeof(PlayerViewModel).GetProperty("CurrentPlaybackRate");

            Assert.NotNull(currentRate);
            ctx.VM.SetPlaybackSpeedCommand.Execute(2.0f);

            ctx.InvokePrivate("PrepareForContentLoading");

            Assert.Equal(1.0f, Assert.IsType<float>(currentRate.GetValue(ctx.VM)));
            Assert.Equal(1.0f, ctx.VideoService.PlaybackRate, precision: 2);
        }

        [Fact]
        public void SeekPreview_ChangesDisplayedTimeWithoutChangingPlaybackPosition()
        {
            var ctx = new ScenarioContext();
            ctx.SetupVodChannel();
            ctx.VM.Position = 120;
            ctx.VM.PositionText = "00:02:00";

            var updatePreview = typeof(PlayerViewModel).GetMethod("UpdateSeekPreview");
            var clearPreview = typeof(PlayerViewModel).GetMethod("ClearSeekPreview");
            var displayedPosition = typeof(PlayerViewModel).GetProperty("DisplayedPositionText");

            Assert.NotNull(updatePreview);
            Assert.NotNull(clearPreview);
            Assert.NotNull(displayedPosition);

            updatePreview.Invoke(ctx.VM, [1458d]);

            Assert.Equal("00:24:18", displayedPosition.GetValue(ctx.VM));
            Assert.Equal(120, ctx.VM.Position);

            clearPreview.Invoke(ctx.VM, null);

            Assert.Equal("00:02:00", displayedPosition.GetValue(ctx.VM));
        }
    }
}
