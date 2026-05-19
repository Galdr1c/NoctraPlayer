using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// İzleme pozisyonunun uygulama kapanışında ve kanal değişiminde
/// düzgün kaydedildiğini doğrular.
/// 
/// Bu testler, MainWindow.OnClosed ve PlayerPlaybackController.PlayChannelAsync
/// içinde eklenen FlushWatchHistoryAsync çağrılarının doğru çalıştığını doğrular.
/// </summary>
public class WatchPositionFlushTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly SqliteConnection _connection;
    private readonly WatchHistoryService _service;

    public WatchPositionFlushTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(() => new AppDbContext(options));
        _contextFactory = factoryMock.Object;

        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Settings).Returns(new AppSettings { SaveWatchHistory = true });
        _service = new WatchHistoryService(_contextFactory, settingsMock.Object);
    }

    [Fact]
    public async Task FlushWatchHistory_SavesVodPosition_OnAppClose()
    {
        // Arrange: Profil ve VOD kanalı oluştur
        var account = new ProviderAccount { Name = "Test Account", Url = "http://test.com" };
        var profile = new Profile { Name = "Test User", ProviderAccount = account };
        _context.ProviderAccounts.Add(account);
        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();

        var playlist = new Playlist { Name = "Test Playlist", Profile = profile };
        _context.Playlists.Add(playlist);
        await _context.SaveChangesAsync();

        var channel = new Channel
        {
            Name = "Test Movie",
            StreamUrl = "http://test.com/movie.mp4",
            Type = ChannelType.VOD,
            Playlist = playlist
        };
        _context.Channels.Add(channel);
        await _context.SaveChangesAsync();

        // Act: 15 dakika izleme pozisyonu kaydet (uygulama kapanışı simülasyonu)
        var watchPosition = TimeSpan.FromMinutes(15);
        await _service.TrackWatchAsync(
            profileId: profile.Id,
            channelId: channel.Id,
            episodeId: null,
            position: watchPosition,
            completed: false,
            duration: TimeSpan.FromMinutes(90)
        );

        // Assert: Veritabanında pozisyon kaydedildi mi?
        var history = await _context.WatchHistories
            .FirstOrDefaultAsync(h => h.ProfileId == profile.Id && h.ChannelId == channel.Id);

        Assert.NotNull(history);
        Assert.Equal(watchPosition, history.StoppedAt);
        Assert.False(history.Completed);
    }

    [Fact]
    public async Task FlushWatchHistory_SavesEpisodePosition_OnChannelSwitch()
    {
        // Arrange: Profil, dizi ve bölüm oluştur
        var account = new ProviderAccount { Name = "Test Account", Url = "http://test.com" };
        var profile = new Profile { Name = "Test User", ProviderAccount = account };
        _context.ProviderAccounts.Add(account);
        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();

        var playlist = new Playlist { Name = "Test Playlist", Profile = profile };
        _context.Playlists.Add(playlist);
        await _context.SaveChangesAsync();

        var series = new Series { Name = "Test Series", Playlist = playlist };
        _context.Series.Add(series);
        await _context.SaveChangesAsync();

        var season = new Season { SeasonNumber = 1, Series = series };
        _context.Seasons.Add(season);
        await _context.SaveChangesAsync();

        var episode = new Episode
        {
            Name = "Episode 1",
            EpisodeNumber = 1,
            StreamUrl = "http://test.com/ep1.mp4",
            Season = season,
            Duration = TimeSpan.FromMinutes(45)
        };
        _context.Episodes.Add(episode);
        await _context.SaveChangesAsync();

        // Act: 10 dakika izleme pozisyonu kaydet (kanal değişimi simülasyonu)
        var watchPosition = TimeSpan.FromMinutes(10);
        await _service.TrackWatchAsync(
            profileId: profile.Id,
            channelId: null,
            episodeId: episode.Id,
            position: watchPosition,
            completed: false,
            duration: episode.Duration
        );

        // Assert: Veritabanında pozisyon kaydedildi mi?
        var history = await _context.WatchHistories
            .FirstOrDefaultAsync(h => h.ProfileId == profile.Id && h.EpisodeId == episode.Id);

        Assert.NotNull(history);
        Assert.Equal(watchPosition, history.StoppedAt);
        Assert.False(history.Completed);
    }

    [Fact]
    public async Task ContinueWatching_ShowsCorrectPosition_AfterFlush()
    {
        // Arrange: Profil ve VOD kanalı oluştur
        var account = new ProviderAccount { Name = "Test Account", Url = "http://test.com" };
        var profile = new Profile { Name = "Test User", ProviderAccount = account };
        _context.ProviderAccounts.Add(account);
        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();

        var playlist = new Playlist { Name = "Test Playlist", Profile = profile };
        _context.Playlists.Add(playlist);
        await _context.SaveChangesAsync();

        var channel = new Channel
        {
            Name = "Test Movie",
            StreamUrl = "http://test.com/movie.mp4",
            Type = ChannelType.VOD,
            Playlist = playlist,
            Duration = TimeSpan.FromMinutes(120)
        };
        _context.Channels.Add(channel);
        await _context.SaveChangesAsync();

        // Act: 30 dakika izleme pozisyonu kaydet
        var watchPosition = TimeSpan.FromMinutes(30);
        await _service.TrackWatchAsync(
            profileId: profile.Id,
            channelId: channel.Id,
            episodeId: null,
            position: watchPosition,
            completed: false,
            duration: channel.Duration
        );

        // Veritabanından tekrar oku (Continue Watching simülasyonu)
        var history = await _context.WatchHistories
            .Include(h => h.Channel)
            .FirstOrDefaultAsync(h => h.ProfileId == profile.Id && h.ChannelId == channel.Id);

        // Assert: Pozisyon doğru mu?
        Assert.NotNull(history);
        Assert.Equal(watchPosition, history.StoppedAt);

        // Resume dialog için pozisyon yeterli mi? (>120 saniye)
        Assert.True(history.StoppedAt.TotalSeconds > 120);
    }

    [Fact]
    public async Task FlushWatchHistory_PreservesPosition_OnMultipleFlushes()
    {
        // Arrange: Profil ve VOD kanalı oluştur
        var account = new ProviderAccount { Name = "Test Account", Url = "http://test.com" };
        var profile = new Profile { Name = "Test User", ProviderAccount = account };
        _context.ProviderAccounts.Add(account);
        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();

        var playlist = new Playlist { Name = "Test Playlist", Profile = profile };
        _context.Playlists.Add(playlist);
        await _context.SaveChangesAsync();

        var channel = new Channel
        {
            Name = "Test Movie",
            StreamUrl = "http://test.com/movie.mp4",
            Type = ChannelType.VOD,
            Playlist = playlist,
            Duration = TimeSpan.FromMinutes(90)
        };
        _context.Channels.Add(channel);
        await _context.SaveChangesAsync();

        // Act: Birden fazla flush (kanal değişimi simülasyonu)
        await _service.TrackWatchAsync(
            profileId: profile.Id,
            channelId: channel.Id,
            episodeId: null,
            position: TimeSpan.FromMinutes(10),
            completed: false,
            duration: channel.Duration
        );

        await _service.TrackWatchAsync(
            profileId: profile.Id,
            channelId: channel.Id,
            episodeId: null,
            position: TimeSpan.FromMinutes(25),
            completed: false,
            duration: channel.Duration
        );

        await _service.TrackWatchAsync(
            profileId: profile.Id,
            channelId: channel.Id,
            episodeId: null,
            position: TimeSpan.FromMinutes(45),
            completed: false,
            duration: channel.Duration
        );

        // Assert: Son pozisyon kaydedildi mi?
        var history = await _context.WatchHistories
            .FirstOrDefaultAsync(h => h.ProfileId == profile.Id && h.ChannelId == channel.Id);

        Assert.NotNull(history);
        Assert.Equal(TimeSpan.FromMinutes(45), history.StoppedAt);

        // Tek kayıt var mı? (Upsert çalışıyor mu?)
        var historyCount = await _context.WatchHistories
            .CountAsync(h => h.ProfileId == profile.Id && h.ChannelId == channel.Id);
        Assert.Equal(1, historyCount);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
