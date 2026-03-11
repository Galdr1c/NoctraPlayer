using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Noctra.Tests;

public class WatchHistoryServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly SqliteConnection _connection;

    public WatchHistoryServiceTests()
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
    }

    [Fact]
    public async Task UpsertSeriesProgressAsync_CrossProviderTmdbMatch_StrangerThingsScenario()
    {
        // Arrange
        var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
        
        var account = new ProviderAccount { Name = "Test Account", Url = "http://test.com" };
        var profile = new Profile { Name = "Test Profile", ProviderAccount = account };
        _context.ProviderAccounts.Add(account);
        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();
        
        var profileId = profile.Id;

        // Provider A
        var playlistA = new Playlist { Name = "ProviderA" };
        var seriesA = new Series { Name = "Stranger Things", TmdbId = 66732, Playlist = playlistA };
        var season1A = new Season { SeasonNumber = 1, Series = seriesA };
        var ep3A = new Episode { EpisodeNumber = 3, Name = "Chapter Three", Season = season1A };
        var ep4A = new Episode { EpisodeNumber = 4, Name = "Chapter Four", Season = season1A };
        
        // Provider B
        var playlistB = new Playlist { Name = "ProviderB" };
        var seriesB = new Series { Name = "Things of Stranger (4K)", TmdbId = 66732, Playlist = playlistB };
        var season1B = new Season { SeasonNumber = 1, Series = seriesB };
        var ep4B = new Episode { EpisodeNumber = 4, Name = "Chapter Four (4K)", Season = season1B };

        _context.Playlists.AddRange(playlistA, playlistB);
        _context.Series.AddRange(seriesA, seriesB);
        _context.Seasons.AddRange(season1A, season1B);
        _context.Episodes.AddRange(ep3A, ep4A, ep4B);
        await _context.SaveChangesAsync();

        // Act & Assert

        // User watches Season 1, Episode 3 until completion on Provider A
        await service.TrackWatchAsync(profileId, null, ep3A.Id, TimeSpan.FromMinutes(45), true, TimeSpan.FromMinutes(45));
        
        // User watches Season 1, Episode 4 halfway (24 minutes) on Provider A
        await service.TrackWatchAsync(profileId, null, ep4A.Id, TimeSpan.FromMinutes(24), false, TimeSpan.FromMinutes(48));

        // User switches to Provider B and continues Episode 4
        // The service should update the existing record based on TmdbId
        await service.TrackWatchAsync(profileId, null, ep4B.Id, TimeSpan.FromMinutes(30), false, TimeSpan.FromMinutes(48));

        var progresses = await _context.SeriesEpisodeProgresses.ToListAsync();
        
        // We should only have 2 records total (S01E03 and S01E04), NOT 3 records.
        Assert.Equal(2, progresses.Count);

        var ep4Progress = progresses.Find(p => p.EpisodeNumber == 4);
        Assert.NotNull(ep4Progress);
        Assert.Equal(TimeSpan.FromMinutes(30), ep4Progress.StoppedAt); // Updated from Provider B
        Assert.Equal(66732, ep4Progress.TmdbId); // The ID is preserved
        Assert.False(ep4Progress.Completed);
    }

    [Fact]
    public async Task UpsertSeriesProgressAsync_FallbackMatch_WhenTmdbIdIsNull()
    {
        // Arrange
        var service = new WatchHistoryService(_contextFactory, new Moq.Mock<Noctra.Services.ISettingsService>().Object);
        
        var account = new ProviderAccount { Name = "Test Account 2", Url = "http://test.com" };
        var profile = new Profile { Name = "Test Profile 2", ProviderAccount = account };
        _context.ProviderAccounts.Add(account);
        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();
        
        var profileId = profile.Id;

        var playlist = new Playlist { Name = "Provider" };
        
        var series1 = new Series { Name = "Breaking Bad", Playlist = playlist };
        var season1 = new Season { SeasonNumber = 1, Series = series1 };
        var ep1 = new Episode { EpisodeNumber = 1, Name = "Pilot", Season = season1 };

        var series2 = new Series { Name = "Breaking Bad (TR)", Playlist = playlist };
        var season2 = new Season { SeasonNumber = 1, Series = series2 };
        var ep2 = new Episode { EpisodeNumber = 1, Name = "Pilot", Season = season2 };

        _context.Playlists.Add(playlist);
        _context.Series.AddRange(series1, series2);
        _context.Seasons.AddRange(season1, season2);
        _context.Episodes.AddRange(ep1, ep2);
        await _context.SaveChangesAsync();

        // Act
        await service.TrackWatchAsync(profileId, null, ep1.Id, TimeSpan.FromMinutes(10), false, TimeSpan.FromMinutes(45));
        await service.TrackWatchAsync(profileId, null, ep2.Id, TimeSpan.FromMinutes(20), false, TimeSpan.FromMinutes(45));

        // Assert
        var progresses = await _context.SeriesEpisodeProgresses.ToListAsync();
        
        // Should only be 1 record because SeriesKey ("breakingbad") matched them
        Assert.Single(progresses);
        Assert.Equal(TimeSpan.FromMinutes(20), progresses[0].StoppedAt);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

