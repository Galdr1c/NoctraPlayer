using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// WatchHistoryService için mevcut testlerin kapsamadığı edge case'leri test eder:
    ///
    /// - Hem channelId hem episodeId null → sessizce döner
    /// - Tamamlanan içerik bir daha "tamamlanmamış" yazılamaz (idempotent completed flag)
    /// - Duration güncelleme: null duration geldiğinde eski duration korunur
    /// - DeleteProfileHistoryAsync yalnızca o profile'ın verisini siler
    /// - CleanupOlderThanDaysAsync yalnızca eski kayıtları siler
    /// - GetLatestForMediaAsync doğru kaydı getirir
    /// - WatchedDuration delta birikimli artıyor
    /// </summary>
    public class WatchHistoryServiceEdgeCaseTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly AppDbContext _context;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public WatchHistoryServiceEdgeCaseTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new AppDbContext(_options);
            _context.Database.EnsureCreated();

            _contextFactory = new TestDbContextFactory(_options);
        }

        private async Task<(Profile profile, Playlist playlist)> SeedProfileAndPlaylistAsync()
        {
            var account = new ProviderAccount { Name = "Test", Url = "http://test.com" };
            var profile = new Profile { Name = "Test Profile", ProviderAccount = account };
            _context.ProviderAccounts.Add(account);
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var playlist = new Playlist { Name = "Test Playlist", ProfileId = profile.Id };
            _context.Playlists.Add(playlist);
            await _context.SaveChangesAsync();

            return (profile, playlist);
        }

        private async Task<Channel> SeedVodChannelAsync(int playlistId)
        {
            var channel = new Channel
            {
                Name       = "Test Movie",
                StreamUrl  = "http://test.com/vod/1.mp4",
                Type       = ChannelType.VOD,
                PlaylistId = playlistId
            };
            _context.Channels.Add(channel);
            await _context.SaveChangesAsync();
            return channel;
        }

        // ─── Null Guard ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task TrackWatchAsync_BothIdsNull_DoesNotThrowAndDoesNotWrite()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, _) = await SeedProfileAndPlaylistAsync();

            // İki null → sessizce döner, kayıt oluşturulmaz
            await service.TrackWatchAsync(profile.Id, null, null, TimeSpan.FromMinutes(10));

            var count = await _context.WatchHistories.CountAsync();
            Assert.Equal(0, count);
        }

        // ─── Completed Flag Idempotency ───────────────────────────────────────────────

        [Fact]
        public async Task TrackWatchAsync_OnceCompleted_CannotBeMarkedIncomplete()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();
            var channel = await SeedVodChannelAsync(playlist.Id);

            // 1. İzleme: tamamlandı olarak işaretle
            await service.TrackWatchAsync(
                profile.Id, channel.Id, null,
                position: TimeSpan.FromHours(2),
                completed: true,
                duration: TimeSpan.FromHours(2));

            // 2. İzleme: baştan başlıyor gibi davranıp completed=false gönder
            await service.TrackWatchAsync(
                profile.Id, channel.Id, null,
                position: TimeSpan.FromMinutes(5),
                completed: false,
                duration: TimeSpan.FromHours(2));

            // completed flag hâlâ true olmalı (isCompletedNow = existing.Completed || completed)
            var history = await _context.WatchHistories
                .FirstAsync(h => h.ChannelId == channel.Id);
            Assert.True(history.Completed,
                "Tamamlanan içerik bir daha 'tamamlanmamış' olarak işaretlenemez.");
        }

        // ─── Duration Preservation ────────────────────────────────────────────────────

        [Fact]
        public async Task TrackWatchAsync_NullDuration_PreservesExistingDuration()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();
            var channel = await SeedVodChannelAsync(playlist.Id);

            var knownDuration = TimeSpan.FromHours(1.5);

            // 1. İzleme: duration ile kaydet
            await service.TrackWatchAsync(
                profile.Id, channel.Id, null,
                position: TimeSpan.FromMinutes(30),
                duration: knownDuration);

            // 2. İzleme: duration=null → eski duration korunmalı
            await service.TrackWatchAsync(
                profile.Id, channel.Id, null,
                position: TimeSpan.FromMinutes(45),
                duration: null);

            var dbChannel = await _context.Channels.FindAsync(channel.Id);
            if (dbChannel != null) await _context.Entry(dbChannel).ReloadAsync();
            
            Assert.NotNull(dbChannel);
            // Duration null gelmediğinde kanal'ın Duration'ı 0'dan büyük kalmalı
            Assert.Equal(knownDuration, dbChannel!.Duration);
        }

        // ─── WatchedDuration Accumulation ────────────────────────────────────────────

        [Fact]
        public async Task TrackWatchAsync_WatchedDuration_AccumulatesCorrectly()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();
            var channel = await SeedVodChannelAsync(playlist.Id);

            var delta1 = TimeSpan.FromMinutes(10);
            var delta2 = TimeSpan.FromMinutes(15);

            await service.TrackWatchAsync(
                profile.Id, channel.Id, null,
                position: TimeSpan.FromMinutes(10),
                incrementDelta: delta1);

            await service.TrackWatchAsync(
                profile.Id, channel.Id, null,
                position: TimeSpan.FromMinutes(25),
                incrementDelta: delta2);

            var history = await _context.WatchHistories.FirstAsync(h => h.ChannelId == channel.Id);
            Assert.Equal(delta1 + delta2, history.WatchedDuration);
        }

        // ─── DeleteProfileHistoryAsync ────────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteProfileHistoryAsync_OnlyClearsTargetProfile()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);

            // İki farklı profil kur
            var account1 = new ProviderAccount { Name = "A1", Url = "http://a.com" };
            var profile1 = new Profile { Name = "Profile 1", ProviderAccount = account1 };
            var account2 = new ProviderAccount { Name = "A2", Url = "http://b.com" };
            var profile2 = new Profile { Name = "Profile 2", ProviderAccount = account2 };
            _context.ProviderAccounts.AddRange(account1, account2);
            _context.Profiles.AddRange(profile1, profile2);
            await _context.SaveChangesAsync();

            var playlist = new Playlist { Name = "Shared Playlist", ProfileId = profile1.Id };
            _context.Playlists.Add(playlist);
            await _context.SaveChangesAsync();

            var ch1 = new Channel { Name = "Ch1", StreamUrl = "u1", Type = ChannelType.VOD, PlaylistId = playlist.Id };
            var ch2 = new Channel { Name = "Ch2", StreamUrl = "u2", Type = ChannelType.VOD, PlaylistId = playlist.Id };
            _context.Channels.AddRange(ch1, ch2);
            await _context.SaveChangesAsync();

            // İki profile de geçmiş yaz
            await service.TrackWatchAsync(profile1.Id, ch1.Id, null, TimeSpan.FromMinutes(20));
            await service.TrackWatchAsync(profile2.Id, ch2.Id, null, TimeSpan.FromMinutes(20));

            // Sadece profile1'in geçmişini temizle
            await service.DeleteProfileHistoryAsync(profile1.Id);

            var remaining = await _context.WatchHistories.ToListAsync();
            Assert.All(remaining, h => Assert.Equal(profile2.Id, h.ProfileId));
            Assert.Single(remaining);
        }

        // ─── CleanupOlderThanDaysAsync ────────────────────────────────────────────────

        [Fact]
        public async Task CleanupOlderThanDaysAsync_RemovesOldEntries_KeepsNew()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();

            var oldChannel = new Channel { Name = "Old", StreamUrl = "old", Type = ChannelType.VOD, PlaylistId = playlist.Id };
            var newChannel = new Channel { Name = "New", StreamUrl = "new", Type = ChannelType.VOD, PlaylistId = playlist.Id };
            _context.Channels.AddRange(oldChannel, newChannel);
            await _context.SaveChangesAsync();

            // Eski kayıt (40 gün önce)
            _context.WatchHistories.Add(new WatchHistory
            {
                ProfileId = profile.Id,
                ChannelId = oldChannel.Id,
                WatchedAt = DateTime.UtcNow.AddDays(-40),
                StoppedAt = TimeSpan.FromMinutes(30)
            });
            // Yeni kayıt (1 gün önce)
            _context.WatchHistories.Add(new WatchHistory
            {
                ProfileId = profile.Id,
                ChannelId = newChannel.Id,
                WatchedAt = DateTime.UtcNow.AddDays(-1),
                StoppedAt = TimeSpan.FromMinutes(10)
            });
            await _context.SaveChangesAsync();

            // 30 günden eski kayıtları temizle
            await service.CleanupOlderThanDaysAsync(profile.Id, 30);

            var remaining = await _context.WatchHistories.ToListAsync();
            Assert.Single(remaining);
            Assert.Equal(newChannel.Id, remaining[0].ChannelId);
        }

        [Fact]
        public async Task CleanupOlderThanDaysAsync_WithZeroOrNegativeDays_DoesNothing()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();
            var channel = await SeedVodChannelAsync(playlist.Id);

            _context.WatchHistories.Add(new WatchHistory
            {
                ProfileId = profile.Id,
                ChannelId = channel.Id,
                WatchedAt = DateTime.UtcNow.AddDays(-100),
                StoppedAt = TimeSpan.FromMinutes(10)
            });
            await _context.SaveChangesAsync();

            // days <= 0 → hiçbir şey silmemeli
            await service.CleanupOlderThanDaysAsync(profile.Id, 0);
            await service.CleanupOlderThanDaysAsync(profile.Id, -5);

            var count = await _context.WatchHistories.CountAsync();
            Assert.Equal(1, count);
        }

        // ─── GetLatestForMediaAsync ───────────────────────────────────────────────────

        [Fact]
        public async Task GetLatestForMediaAsync_ByChannelId_ReturnsCorrectRecord()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();
            var channel = await SeedVodChannelAsync(playlist.Id);

            await service.TrackWatchAsync(profile.Id, channel.Id, null, TimeSpan.FromMinutes(20));

            var result = await service.GetLatestForMediaAsync(profile.Id, channel.Id, null);

            Assert.NotNull(result);
            Assert.Equal(channel.Id, result!.ChannelId);
        }

        [Fact]
        public async Task GetLatestForMediaAsync_WhenNotFound_ReturnsNull()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, _) = await SeedProfileAndPlaylistAsync();

            var result = await service.GetLatestForMediaAsync(profile.Id, 9999, null);
            Assert.Null(result);
        }

        // ─── StoppedAt: Completed → duration, Not Completed → position ───────────────

        [Fact]
        public async Task TrackWatchAsync_WhenCompleted_StoppedAtSetToDuration()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();
            var channel = await SeedVodChannelAsync(playlist.Id);

            var duration = TimeSpan.FromHours(2);
            var position = TimeSpan.FromMinutes(110); // %91.6

            await service.TrackWatchAsync(
                profile.Id, channel.Id, null,
                position: position,
                completed: true,
                duration: duration);

            var history = await _context.WatchHistories.FirstAsync(h => h.ChannelId == channel.Id);
            // Tamamlanan içerikte StoppedAt = duration (tam sonuna çekilir)
            Assert.Equal(duration, history.StoppedAt);
        }

        [Fact]
        public async Task TrackWatchAsync_WhenNotCompleted_StoppedAtSetToPosition()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();
            var channel = await SeedVodChannelAsync(playlist.Id);

            var position = TimeSpan.FromMinutes(45);
            await service.TrackWatchAsync(
                profile.Id, channel.Id, null,
                position: position,
                completed: false,
                duration: TimeSpan.FromHours(2));

            var history = await _context.WatchHistories.FirstAsync(h => h.ChannelId == channel.Id);
            Assert.Equal(position, history.StoppedAt);
        }

        // ─── GetHistoryAsync ─────────────────────────────────────────────────────────

        [Fact]
        public async Task GetHistoryAsync_ReturnsDescendingByWatchedAt()
        {
            var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
            var (profile, playlist) = await SeedProfileAndPlaylistAsync();

            var ch1 = new Channel { Name = "First", StreamUrl = "u1", Type = ChannelType.VOD, PlaylistId = playlist.Id };
            var ch2 = new Channel { Name = "Second", StreamUrl = "u2", Type = ChannelType.VOD, PlaylistId = playlist.Id };
            _context.Channels.AddRange(ch1, ch2);
            await _context.SaveChangesAsync();

            // ch1 daha eski, ch2 daha yeni
            _context.WatchHistories.AddRange(
                new WatchHistory { ProfileId = profile.Id, ChannelId = ch1.Id, WatchedAt = DateTime.UtcNow.AddDays(-2), StoppedAt = TimeSpan.FromMinutes(10) },
                new WatchHistory { ProfileId = profile.Id, ChannelId = ch2.Id, WatchedAt = DateTime.UtcNow.AddDays(-1), StoppedAt = TimeSpan.FromMinutes(20) }
            );
            await _context.SaveChangesAsync();

            var history = await service.GetHistoryAsync(profile.Id);

            // En yeni (ch2) başta gelmeli
            Assert.Equal(2, history.Count);
            Assert.Equal(ch2.Id, history[0].ChannelId);
            Assert.Equal(ch1.Id, history[1].ChannelId);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Close();
            _connection.Dispose();
        }

        // ─── Helper: Test DB Factory ──────────────────────────────────────────────────

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
        {
            private readonly DbContextOptions<AppDbContext> _options;
            public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
            public AppDbContext CreateDbContext() => new AppDbContext(_options);
            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        }
    }
}
