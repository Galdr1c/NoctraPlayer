using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Noctra.Tests;

[CollectionDefinition(nameof(PlaylistServiceSearchQueryTests), DisableParallelization = true)]
public sealed class PlaylistServiceSearchQueryTestsCollection
{
}

[Collection(nameof(PlaylistServiceSearchQueryTests))]
public class PlaylistServiceSearchQueryTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Mock<IM3UParser> _parserMock = new();
    private readonly Mock<IMediaService> _mediaServiceMock = new();
    private readonly Mock<IPlaylistOrganizerService> _organizerMock = new();
    private readonly Mock<IEpgService> _epgServiceMock = new();
    private readonly Mock<ISettingsService> _settingsServiceMock = new();
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();
    private readonly LanguageDetectionService _languageDetection = new();
    private readonly EpgSourceResolver _epgSourceResolver = new();
    private readonly HttpClient _httpClient = new();

    public PlaylistServiceSearchQueryTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"NoctraSearchTests-{Guid.NewGuid():N}.db");

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        using (var context = new AppDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _contextFactory = new SimpleDbContextFactory(_options);
        _settingsServiceMock.Setup(s => s.Settings).Returns(new AppSettings());
        _localizationServiceMock.Setup(l => l.GetString(It.IsAny<string>())).Returns<string>(k => k);
        _organizerMock.Setup(o => o.Organize(It.IsAny<List<Channel>>(), It.IsAny<bool>()))
            .Returns<List<Channel>, bool>((c, _) => c);
        _mediaServiceMock
            .Setup(m => m.AggregateContentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        try { File.Delete(_databasePath); } catch { /* test cleanup best effort */ }
    }

    private PlaylistService CreateService()
    {
        return new PlaylistService(
            _contextFactory,
            _parserMock.Object,
            _mediaServiceMock.Object,
            _organizerMock.Object,
            _languageDetection,
            _epgSourceResolver,
            _epgServiceMock.Object,
            _httpClient,
            _settingsServiceMock.Object,
            _localizationServiceMock.Object,
            new ImportJobService(_contextFactory)
        );
    }

    [Fact]
    public void ExtractSearchTokens_SplitsByPunctuationAndLimitsTokens()
    {
        var tokens = PlaylistService.ExtractSearchTokens("TR: Seni - Öldürecekler (2024) [FHD]");
        Assert.Equal(4, tokens.Count);
        Assert.Equal("TR", tokens[0]);
        Assert.Equal("Seni", tokens[1]);
        Assert.Equal("Öldürecekler", tokens[2]);
        Assert.Equal("2024", tokens[3]);
    }

    [Fact]
    public void GenerateSearchVariants_ForTurkishToken_IncludesTitleUpperLowerAndAscii()
    {
        var variants = PlaylistService.GenerateSearchVariants("öldürecekler");
        Assert.Contains("öldürecekler", variants);
        Assert.Contains("Öldürecekler", variants);
        Assert.Contains("ÖLDÜRECEKLER", variants);
        Assert.Contains("oldurecekler", variants);
    }

    [Fact]
    public void GenerateSearchVariants_ForAsciiToken_IncludesTurkishUmlautVariants()
    {
        var variants = PlaylistService.GenerateSearchVariants("oldurecekler");
        Assert.Contains("oldurecekler", variants);
        Assert.Contains("öldürecekler", variants);
        Assert.Contains("Öldürecekler", variants);
        Assert.Contains("ÖLDÜRECEKLER", variants);
    }

    [Fact]
    public void GenerateSearchVariants_ForSeni_IncludesTurkishDottedIUpper()
    {
        var variants = PlaylistService.GenerateSearchVariants("seni");
        Assert.Contains("seni", variants);
        Assert.Contains("SENİ", variants);
        Assert.Contains("Seni", variants);
    }

    [Theory]
    [InlineData("seni öldürecekler")]
    [InlineData("seni oldurecekler")]
    [InlineData("Seni Öldürecekler")]
    [InlineData("SENİ ÖLDÜRECEKLER")]
    [InlineData("öldürecekler")]
    [InlineData("oldurecekler")]
    public async Task GetChannelsFilteredPageAsync_FindsTurkishAndAsciiChannels(string searchQuery)
    {
        // Arrange
        var service = CreateService();
        var playlist = await service.AddFromChannelsAsync(
            "Turkish Search Test",
            "https://test.provider/m3u",
            new[]
            {
                new Channel { Name = "TR | Seni Öldürecekler (2024)", StreamUrl = "http://test/1", Type = ChannelType.VOD },
                new Channel { Name = "TR | SENİ ÖLDÜRECEKLER", StreamUrl = "http://test/2", Type = ChannelType.VOD },
                new Channel { Name = "TR | Seni Oldurecekler", StreamUrl = "http://test/3", Type = ChannelType.VOD },
                new Channel { Name = "TR | SENI OLDURECEKLER", StreamUrl = "http://test/4", Type = ChannelType.VOD },
                new Channel { Name = "TR | Tamamen Farklı Bir Film", StreamUrl = "http://test/5", Type = ChannelType.VOD }
            });

        // Act
        var results = await service.GetChannelsFilteredPageAsync(
            playlist.Id,
            skip: 0,
            take: 20,
            searchText: searchQuery,
            type: ChannelType.VOD);

        // Assert: all 4 variations of the movie must be returned as candidates; the unrelated channel must NOT be returned.
        Assert.Equal(4, results.Count);
        var names = results.Select(c => c.Name).ToList();
        Assert.Contains("TR | Seni Öldürecekler (2024)", names);
        Assert.Contains("TR | SENİ ÖLDÜRECEKLER", names);
        Assert.Contains("TR | Seni Oldurecekler", names);
        Assert.Contains("TR | SENI OLDURECEKLER", names);
        Assert.DoesNotContain("TR | Tamamen Farklı Bir Film", names);
    }

    [Fact]
    public async Task GetChannelsFilteredPageAsync_MatchesGroupTitleWithTurkishVariants()
    {
        // Arrange
        var service = CreateService();
        var playlist = await service.AddFromChannelsAsync(
            "Group Search Test",
            "https://test.provider/m3u",
            new[]
            {
                new Channel { Name = "Kanal 1", GroupTitle = "Öldüren Filmler", StreamUrl = "http://test/g1", Type = ChannelType.Live },
                new Channel { Name = "Kanal 2", GroupTitle = "OLDUREN FILMLER", StreamUrl = "http://test/g2", Type = ChannelType.Live },
                new Channel { Name = "Kanal 3", GroupTitle = "Komedi", StreamUrl = "http://test/g3", Type = ChannelType.Live }
            });

        // Act
        var results = await service.GetChannelsFilteredPageAsync(
            playlist.Id,
            skip: 0,
            take: 20,
            searchText: "öldüren",
            type: ChannelType.Live);

        // Assert
        Assert.Equal(2, results.Count);
        var names = results.Select(c => c.Name).ToList();
        Assert.Contains("Kanal 1", names);
        Assert.Contains("Kanal 2", names);
        Assert.DoesNotContain("Kanal 3", names);
    }

    [Theory]
    [InlineData("café")]
    [InlineData("cafe")]
    [InlineData("CAFÉ")]
    [InlineData("Café")]
    public async Task GetChannelsFilteredPageAsync_FindsFrenchDiacritics(string searchQuery)
    {
        var service = CreateService();
        var playlist = await service.AddFromChannelsAsync(
            "French Test",
            "https://test.provider/m3u",
            new[]
            {
                new Channel { Name = "CAFÉ de France", StreamUrl = "http://test/fr1", Type = ChannelType.Live },
                new Channel { Name = "Café du Monde", StreamUrl = "http://test/fr2", Type = ChannelType.Live },
                new Channel { Name = "Sports Channel", StreamUrl = "http://test/fr3", Type = ChannelType.Live }
            });

        var results = await service.GetChannelsFilteredPageAsync(
            playlist.Id, skip: 0, take: 20, searchText: searchQuery, type: ChannelType.Live);

        Assert.Equal(2, results.Count);
        var names = results.Select(c => c.Name).ToList();
        Assert.Contains("CAFÉ de France", names);
        Assert.Contains("Café du Monde", names);
        Assert.DoesNotContain("Sports Channel", names);
    }

    [Theory]
    [InlineData("über")]
    [InlineData("uber")]
    [InlineData("ÜBER")]
    [InlineData("Über")]
    public async Task GetChannelsFilteredPageAsync_FindsGermanUmlauts(string searchQuery)
    {
        var service = CreateService();
        var playlist = await service.AddFromChannelsAsync(
            "German Test",
            "https://test.provider/m3u",
            new[]
            {
                new Channel { Name = "Über Spirit", StreamUrl = "http://test/de1", Type = ChannelType.Live },
                new Channel { Name = "ÜBER Kino", StreamUrl = "http://test/de2", Type = ChannelType.Live },
                new Channel { Name = "Action Films", StreamUrl = "http://test/de3", Type = ChannelType.Live }
            });

        var results = await service.GetChannelsFilteredPageAsync(
            playlist.Id, skip: 0, take: 20, searchText: searchQuery, type: ChannelType.Live);

        Assert.Equal(2, results.Count);
        var names = results.Select(c => c.Name).ToList();
        Assert.Contains("Über Spirit", names);
        Assert.Contains("ÜBER Kino", names);
        Assert.DoesNotContain("Action Films", names);
    }

    [Theory]
    [InlineData("señor")]
    [InlineData("senor")]
    [InlineData("SEÑOR")]
    public async Task GetChannelsFilteredPageAsync_FindsSpanishTilde(string searchQuery)
    {
        var service = CreateService();
        var playlist = await service.AddFromChannelsAsync(
            "Spanish Test",
            "https://test.provider/m3u",
            new[]
            {
                new Channel { Name = "El Señor de los Anillos", StreamUrl = "http://test/es1", Type = ChannelType.VOD },
                new Channel { Name = "Comedy Central", StreamUrl = "http://test/es2", Type = ChannelType.Live }
            });

        var results = await service.GetChannelsFilteredPageAsync(
            playlist.Id, skip: 0, take: 20, searchText: searchQuery, type: ChannelType.VOD);

        Assert.Single(results);
        Assert.Equal("El Señor de los Anillos", results[0].Name);
    }

    [Theory]
    [InlineData("naïve")]
    [InlineData("naive")]
    [InlineData("NAÏVE")]
    public async Task GetChannelsFilteredPageAsync_FindsFrenchDiaeresis(string searchQuery)
    {
        var service = CreateService();
        var playlist = await service.AddFromChannelsAsync(
            "Diaeresis Test",
            "https://test.provider/m3u",
            new[]
            {
                new Channel { Name = "Naïve Pictures", StreamUrl = "http://test/d1", Type = ChannelType.Live },
                new Channel { Name = "Kids Zone", StreamUrl = "http://test/d2", Type = ChannelType.Live }
            });

        var results = await service.GetChannelsFilteredPageAsync(
            playlist.Id, skip: 0, take: 20, searchText: searchQuery, type: ChannelType.Live);

        Assert.Single(results);
        Assert.Equal("Naïve Pictures", results[0].Name);
    }

    [Theory]
    [InlineData("äger")]
    [InlineData("ager")]
    [InlineData("ÄGER")]
    public async Task GetChannelsFilteredPageAsync_FindsGermanUmlautA(string searchQuery)
    {
        var service = CreateService();
        var playlist = await service.AddFromChannelsAsync(
            "German A Test",
            "https://test.provider/m3u",
            new[]
            {
                new Channel { Name = "Äger TV", StreamUrl = "http://test/dea1", Type = ChannelType.Live },
                new Channel { Name = "Sports Plus", StreamUrl = "http://test/dea2", Type = ChannelType.Live }
            });

        var results = await service.GetChannelsFilteredPageAsync(
            playlist.Id, skip: 0, take: 20, searchText: searchQuery, type: ChannelType.Live);

        Assert.Single(results);
        Assert.Equal("Äger TV", results[0].Name);
    }
}
