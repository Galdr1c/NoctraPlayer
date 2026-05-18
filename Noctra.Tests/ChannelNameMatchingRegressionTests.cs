using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System.Reflection;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// Regression tests for CheckAndPurgeUnsafeSeriesAsync word-boundary matching fix.
/// 
/// BUG: c.Name.Contains(dbSeries.Name) kullanıldığında "Man" adlı bir dizi,
/// "Superman", "Batman", "Mandalorian" gibi kanalları da siliyordu.
/// 
/// FIX: Contains yerine tam eşleşme (==) ve başlık başı eşleşmesi
/// (StartsWith + boşluk/nokta/" - ") kullanıldı.
/// </summary>
public class ChannelNameMatchingRegressionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TmdbSyncService _service;
    private readonly int _playlistId;
    private readonly int _profileId;

    public ChannelNameMatchingRegressionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ChannelMatchTest_{Guid.NewGuid()}")
            .Options;

        _db = new AppDbContext(options);

        // Setup: ProviderAccount → Profile (child) → Playlist
        var provider = new ProviderAccount
        {
            Name = "Test Provider",
            Type = ProfileType.M3U,
            Url = "http://example.com"
        };
        _db.ProviderAccounts.Add(provider);
        _db.SaveChanges();

        var profile = new Profile
        {
            Name = "Child Profile",
            IsChild = true,
            ProviderAccountId = provider.Id
        };
        _db.Profiles.Add(profile);
        _db.SaveChanges();
        _profileId = profile.Id;

        var playlist = new Playlist
        {
            Name = "Test Playlist",
            ProfileId = profile.Id,
            Url = "http://example.com/playlist"
        };
        _db.Playlists.Add(playlist);
        _db.SaveChanges();
        _playlistId = playlist.Id;

        // Mock dependencies
        var metadataMock = new Mock<IMetadataService>();
        var dispatcherMock = new Mock<IDispatcherService>();
        dispatcherMock.Setup(d => d.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(a => a());
        var loggerMock = new Mock<ILogger<TmdbSyncService>>();

        _service = new TmdbSyncService(
            new MockDbContextFactory(options),
            metadataMock.Object,
            dispatcherMock.Object,
            loggerMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    /// <summary>
    /// Helper: Reflection ile private CheckAndPurgeUnsafeSeriesAsync metodunu çağır.
    /// </summary>
    private async Task<bool> InvokeCheckAndPurgeUnsafeSeriesAsync(Series series)
    {
        var method = typeof(TmdbSyncService).GetMethod("CheckAndPurgeUnsafeSeriesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var parameters = new object[] { _db, series, CancellationToken.None };
        var task = (Task<bool>)method.Invoke(_service, parameters)!;
        return await task;
    }

    /// <summary>
    /// Helper: Test kanallarını DB'ye ekle ve silinen kanal isimlerini döndür.
    /// </summary>
    private async Task<HashSet<string>> RunPurgeTestAsync(string seriesName, string contentRating, string[] channelNames)
    {
        // Arrange: Series with unsafe rating
        var series = new Series
        {
            Name = seriesName,
            ContentRating = contentRating,
            PlaylistId = _playlistId
        };
        _db.Series.Add(series);
        await _db.SaveChangesAsync();

        // Arrange: Channels
        foreach (var name in channelNames)
        {
            _db.Channels.Add(new Channel
            {
                Name = name,
                StreamUrl = $"http://example.com/{name}",
                Type = ChannelType.Series,
                PlaylistId = _playlistId
            });
        }
        await _db.SaveChangesAsync();

        // Act
        await InvokeCheckAndPurgeUnsafeSeriesAsync(series);

        // Assert: Get remaining channel names
        var remaining = await _db.Channels
            .Where(c => c.PlaylistId == _playlistId)
            .Select(c => c.Name)
            .ToListAsync();

        var deleted = new HashSet<string>(channelNames);
        deleted.ExceptWith(remaining);
        return deleted;
    }

    // ──────────────────────────────────────────────
    // "Man" — Temel Regresyon Testi
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Man_DoesNotMatch_SupermanBatmanMandalorian()
    {
        var deleted = await RunPurgeTestAsync(
            seriesName: "Man",
            contentRating: "R", // Unsafe → triggers purge
            channelNames: ["Man", "Superman", "Batman", "Mandalorian", "Woman"]);

        // "Man" exact match should be deleted
        Assert.Contains("Man", deleted);
        // None of these should be deleted
        Assert.DoesNotContain("Superman", deleted);
        Assert.DoesNotContain("Batman", deleted);
        Assert.DoesNotContain("Mandalorian", deleted);
        Assert.DoesNotContain("Woman", deleted);
    }

    [Fact]
    public async Task Man_Matches_ManWithSpacePrefix()
    {
        var deleted = await RunPurgeTestAsync(
            seriesName: "Man",
            contentRating: "R",
            channelNames: ["Man S01E01", "Man - 1080p", "Man.Show", "The Man Show"]);

        // StartsWith("Man ") and StartsWith("Man - ") and StartsWith("Man.") should match
        Assert.Contains("Man S01E01", deleted);
        Assert.Contains("Man - 1080p", deleted);
        Assert.Contains("Man.Show", deleted);
        // "The Man Show" does NOT start with "Man" → should NOT be deleted
        Assert.DoesNotContain("The Man Show", deleted);
    }

    [Fact]
    public async Task Man_DoesNotMatch_Mankind()
    {
        var deleted = await RunPurgeTestAsync(
            seriesName: "Man",
            contentRating: "R",
            channelNames: ["Man", "Mankind", "ManKind"]);

        Assert.Contains("Man", deleted);
        Assert.DoesNotContain("Mankind", deleted);
        Assert.DoesNotContain("ManKind", deleted);
    }

    // ──────────────────────────────────────────────
    // "Breaking Bad" — Multi-word series
    // ──────────────────────────────────────────────

    [Fact]
    public async Task BreakingBad_ExactMatch_Deleted()
    {
        var deleted = await RunPurgeTestAsync(
            seriesName: "Breaking Bad",
            contentRating: "R",
            channelNames: ["Breaking Bad", "Breaking Bad S01E01", "The Breaking Bad"]);

        Assert.Contains("Breaking Bad", deleted);
        Assert.Contains("Breaking Bad S01E01", deleted);
        // "The Breaking Bad" does NOT start with "Breaking Bad " → should NOT match
        Assert.DoesNotContain("The Breaking Bad", deleted);
    }

    // ──────────────────────────────────────────────
    // Single-word series edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SeriesWithDotInName_MatchesCorrectly()
    {
        var deleted = await RunPurgeTestAsync(
            seriesName: "Mr. Robot",
            contentRating: "R",
            channelNames: ["Mr. Robot", "Mr. Robot S01E01", "Mr.Robot"]);

        // Exact match
        Assert.Contains("Mr. Robot", deleted);
        // StartsWith "Mr. Robot " → match
        Assert.Contains("Mr. Robot S01E01", deleted);
        // "Mr.Robot" does NOT start with "Mr. Robot " or "Mr. Robot." → no match (no dot after "Robot")
        Assert.DoesNotContain("Mr.Robot", deleted);
    }

    // ──────────────────────────────────────────────
    // "The" prefix should not interfere
    // ──────────────────────────────────────────────

    [Fact]
    public async Task TheOffice_DoesNotMatch_Office()
    {
        var deleted = await RunPurgeTestAsync(
            seriesName: "Office",
            contentRating: "R",
            channelNames: ["Office", "The Office", "Office Space"]);

        Assert.Contains("Office", deleted);
        Assert.Contains("Office Space", deleted); // StartsWith "Office "
        Assert.DoesNotContain("The Office", deleted); // Does NOT start with "Office"
    }

    // ──────────────────────────────────────────────
    // Contains-style substring that should NOT match
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Contains_SubstringNoLongerMatches()
    {
        // These would have been incorrectly matched by the old Contains() logic
        var deleted = await RunPurgeTestAsync(
            seriesName: "at",
            contentRating: "R",
            channelNames: ["at", "Bat", "Cat", "Rat", "At", "Batman Begins"]);

        Assert.Contains("at", deleted);
        Assert.DoesNotContain("Bat", deleted);
        Assert.DoesNotContain("Cat", deleted);
        Assert.DoesNotContain("Rat", deleted);
        Assert.DoesNotContain("At", deleted); // Case-sensitive: "At" != "at"
        Assert.DoesNotContain("Batman Begins", deleted);
    }

    [Fact]
    public async Task SafeRating_DoesNotTriggerPurge()
    {
        // When ContentRating is safe (G), the method should NOT purge anything
        var series = new Series
        {
            Name = "Man",
            ContentRating = "G", // Safe → should NOT purge
            PlaylistId = _playlistId
        };
        _db.Series.Add(series);
        await _db.SaveChangesAsync();

        _db.Channels.Add(new Channel
        {
            Name = "Superman",
            StreamUrl = "http://example.com/superman",
            Type = ChannelType.Series,
            PlaylistId = _playlistId
        });
        await _db.SaveChangesAsync();

        await InvokeCheckAndPurgeUnsafeSeriesAsync(series);

        var remaining = await _db.Channels.CountAsync(c => c.PlaylistId == _playlistId);
        Assert.Equal(1, remaining); // Channel was NOT deleted
    }

    [Fact]
    public async Task NonChildProfile_DoesNotTriggerPurge()
    {
        // Create a non-child profile's playlist
        var nonChildPlaylist = new Playlist
        {
            Name = "Adult Playlist",
            ProfileId = _profileId // Same profile, but we need a non-child one
        };
        
        // Actually create a whole new non-child profile setup
        var provider = new ProviderAccount
        {
            Name = "Test Provider 2",
            Type = ProfileType.M3U,
            Url = "http://example.com/2"
        };
        _db.ProviderAccounts.Add(provider);
        await _db.SaveChangesAsync();

        var adultProfile = new Profile
        {
            Name = "Adult Profile",
            IsChild = false, // NOT a child profile
            ProviderAccountId = provider.Id
        };
        _db.Profiles.Add(adultProfile);
        await _db.SaveChangesAsync();

        var adultPlaylist = new Playlist
        {
            Name = "Adult Playlist",
            ProfileId = adultProfile.Id,
            Url = "http://example.com/playlist2"
        };
        _db.Playlists.Add(adultPlaylist);
        await _db.SaveChangesAsync();

        var series = new Series
        {
            Name = "Man",
            ContentRating = "R", // Unsafe, but non-child profile so should NOT purge
            PlaylistId = adultPlaylist.Id
        };
        _db.Series.Add(series);
        await _db.SaveChangesAsync();

        _db.Channels.Add(new Channel
        {
            Name = "Superman",
            StreamUrl = "http://example.com/superman",
            Type = ChannelType.Series,
            PlaylistId = adultPlaylist.Id
        });
        await _db.SaveChangesAsync();

        await InvokeCheckAndPurgeUnsafeSeriesAsync(series);

        var remaining = await _db.Channels.CountAsync(c => c.PlaylistId == adultPlaylist.Id);
        Assert.Equal(1, remaining); // Channel was NOT deleted because profile is not child
    }
}

/// <summary>
/// Mock IDbContextFactory for in-memory database testing.
/// </summary>
public class MockDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public MockDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    public AppDbContext CreateDbContext()
    {
        return new AppDbContext(_options);
    }

    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(new AppDbContext(_options));
    }
}
