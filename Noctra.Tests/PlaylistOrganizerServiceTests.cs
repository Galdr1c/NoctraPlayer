using System;
using System.Collections.Generic;
using System.Linq;
using Noctra.Models;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests;

// =============================================================================
// PlaylistOrganizerServiceTests
// 5 aşamalı pipeline'ın her aşamasını bağımsız ve entegre test eder.
// Toplam: 68 test
// =============================================================================

public class PlaylistOrganizerServiceTests
{
    private readonly PlaylistOrganizerService _sut = new();

    // ─── Yardımcı fabrikalar ──────────────────────────────────────────────────

    private static Channel Live(string name, string? group = null, string? url = null, string? tvgId = null)
        => new() { Name = name, Type = ChannelType.Live, GroupTitle = group, StreamUrl = url ?? $"http://x/{name}", TvgId = tvgId };

    private static Channel Vod(string name, string? group = null, string? url = null)
        => new() { Name = name, Type = ChannelType.VOD, GroupTitle = group, StreamUrl = url ?? $"http://x/{name}" };

    private static Channel Series(string name, string? group = null, string? url = null)
        => new() { Name = name, Type = ChannelType.Series, GroupTitle = group, StreamUrl = url ?? $"http://x/{name}" };

    // =========================================================================
    // A — RemoveDuplicates
    // =========================================================================

    [Fact]
    public void RemoveDuplicates_EmptyList_ReturnsEmpty()
    {
        var result = _sut.RemoveDuplicates(new List<Channel>());
        Assert.Empty(result);
    }

    [Fact]
    public void RemoveDuplicates_NoDuplicates_RetainsAll()
    {
        var channels = new List<Channel>
        {
            Live("TRT 1", "Ulusal"),
            Live("Kanal D", "Ulusal"),
            Live("Show TV", "Ulusal"),
        };
        var result = _sut.RemoveDuplicates(channels);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void RemoveDuplicates_ExactNameDuplicate_KeepsOne()
    {
        var channels = new List<Channel>
        {
            Live("TRT 1", "Ulusal", "http://a/1"),
            Live("TRT 1", "Ulusal", "http://a/1"),
        };
        var result = _sut.RemoveDuplicates(channels);
        Assert.Single(result);
    }

    [Fact]
    public void RemoveDuplicates_QualityVariants_KeepsHighestQuality()
    {
        // "TRT 1 SD" (düşük) vs "TRT 1 HD" (yüksek) → HD kalmalı
        var sd = Live("TRT 1 SD", "Ulusal", "http://a/1");
        var hd = Live("TRT 1 HD", "Ulusal", "http://a/1");
        var result = _sut.RemoveDuplicates(new List<Channel> { sd, hd });
        Assert.Single(result);
        Assert.Contains("HD", result[0].Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveDuplicates_QualityVariants_4KWins()
    {
        var hd   = Live("BeIN Sports 1 HD",  "Spor", "http://a/1");
        var fhd  = Live("BeIN Sports 1 FHD", "Spor", "http://a/1");
        var uhd4k = Live("BeIN Sports 1 4K", "Spor", "http://a/1");
        var result = _sut.RemoveDuplicates(new List<Channel> { hd, fhd, uhd4k });
        Assert.Single(result);
        Assert.Contains("4K", result[0].Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveDuplicates_SameLinearStream_PrefersLiveOverVod()
    {
        var live = Live("MovieSmart Turk (576p)", "Movies", "https://example.com/moviesmart/master.m3u8?token=1");
        var vod = Vod("MovieSmart Turk (576p)", "Movies", "https://example.com/moviesmart/master.m3u8?token=1");

        var result = _sut.RemoveDuplicates(new List<Channel> { live, vod });

        Assert.Single(result);
        Assert.Equal(ChannelType.Live, result[0].Type);
    }

    [Fact]
    public void RemoveDuplicates_SeriesEpisodes_EachEpisodePreserved()
    {
        // Farklı bölümler → ayrı key → hepsi korunmalı
        var channels = new List<Channel>
        {
            Series("Breaking Bad S01E01", "Yabancı Dizi", "http://a/1"),
            Series("Breaking Bad S01E02", "Yabancı Dizi", "http://a/2"),
            Series("Breaking Bad S01E03", "Yabancı Dizi", "http://a/3"),
        };
        var result = _sut.RemoveDuplicates(channels);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void RemoveDuplicates_SameSeriesEpisodeDuplicate_KeepsOne()
    {
        var channels = new List<Channel>
        {
            Series("Breaking Bad S01E01", "Yabancı Dizi", "http://a/1"),
            Series("Breaking Bad S01E01", "Yabancı Dizi", "http://a/1"),
        };
        var result = _sut.RemoveDuplicates(channels);
        Assert.Single(result);
    }

    [Fact]
    public void RemoveDuplicates_SameNameDifferentGroup_BothKept()
    {
        // Aynı isim fakat farklı grup → farklı key → ikisi de kalmalı
        var channels = new List<Channel>
        {
            Live("Star TV", "Türkçe", "http://a/star"),
            Live("Star TV", "Yabancı", "http://b/star"),
        };
        var result = _sut.RemoveDuplicates(channels);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void RemoveDuplicates_NullName_ChannelRetained()
    {
        // Null isimli kanal düşürülmemeli
        var ch = new Channel { Name = null, Type = ChannelType.Live, StreamUrl = "http://x/1" };
        var result = _sut.RemoveDuplicates(new List<Channel> { ch });
        Assert.Single(result);
    }

    // =========================================================================
    // B — AutoCategorize
    // =========================================================================

    [Fact]
    public void AutoCategorize_EmptyGroup_AssignsUncategorized()
    {
        var ch = Live("Test Kanal", null); 
        _sut.AutoCategorize(new List<Channel> { ch });
        Assert.Equal("Uncategorized", ch.GroupTitle);
    }

    [Fact]
    public void AutoCategorize_AlreadyCategorized_NotOverwritten()
    {
        var ch = Live("CNN Türk", "Özel"); 
        _sut.AutoCategorize(new List<Channel> { ch });
        Assert.Equal("Özel", ch.GroupTitle); 
    }

    [Fact]
    public void AutoCategorize_UndefinedGroup_AssignsUncategorized()
    {
        var ch = Live("TRT Spor", "undefined");
        _sut.AutoCategorize(new List<Channel> { ch });
        Assert.Equal("Uncategorized", ch.GroupTitle);
    }

    // =========================================================================
    // C — NormalizeGroupNames
    // =========================================================================

    [Theory]
    [InlineData("Sports",        "Sports")]
    [InlineData("Haber",         "Haber")]
    [InlineData("Kids",          "Kids")]
    public void NormalizeGroupNames_Disabled_DoesNotModifyGroupTitle(string input, string expected)
    {
        var ch = Live("Test Kanal", input);
        _sut.NormalizeGroupNames(new List<Channel> { ch });
        Assert.Equal(expected, ch.GroupTitle);
    }

    // =========================================================================
    // D — SmartSort
    // =========================================================================

    [Fact]
    public void SmartSort_ChannelNumbers_NumberedChannelsFirst()
    {
        var channels = new List<Channel>
        {
            Live("TRT 3",  "Ulusal"),
            Live("TRT 1",  "Ulusal"),
            Live("Kanal D","Ulusal"),
            Live("TRT 2",  "Ulusal"),
        };
        var sorted = _sut.SmartSort(channels);
        var names = sorted.Select(c => c.Name).ToList();
        // Numaralılar önce, TRT 1 < TRT 2 < TRT 3
        var trt1 = names.IndexOf("TRT 1");
        var trt2 = names.IndexOf("TRT 2");
        var trt3 = names.IndexOf("TRT 3");
        Assert.True(trt1 < trt2);
        Assert.True(trt2 < trt3);
    }

    [Fact]
    public void SmartSort_SameGroup_AlphabeticFallback()
    {
        var channels = new List<Channel>
        {
            Live("Zeynep", "Uncategorized"),
            Live("Ali",    "Uncategorized"),
            Live("Mehmet", "Uncategorized"),
        };
        var sorted = _sut.SmartSort(channels);
        Assert.Equal("Ali",    sorted[0].Name);
        Assert.Equal("Mehmet", sorted[1].Name);
        Assert.Equal("Zeynep", sorted[2].Name);
    }

    [Fact]
    public void SmartSort_EmptyList_ReturnsEmpty()
    {
        Assert.Empty(_sut.SmartSort(new List<Channel>()));
    }

    [Fact]
    public void SmartSort_UngroupedChannels_SortedLast()
    {
        var channels = new List<Channel>
        {
            Live("ZZZ Kanal", null),
            Live("AAA Kanal", "Spor"),
        };
        var sorted = _sut.SmartSort(channels);
        // "Spor" grubu null'dan önce gelir
        Assert.Equal("AAA Kanal", sorted[0].Name);
    }

    // =========================================================================
    // E — EnrichMetadata / GenerateEpgId
    // =========================================================================

    [Fact]
    public void EnrichMetadata_MissingTvgId_GeneratesOne()
    {
        var ch = Live("Show TV", "Diziler");
        ch.TvgId = null;
        _sut.EnrichMetadata(new List<Channel> { ch });
        Assert.False(string.IsNullOrWhiteSpace(ch.TvgId));
    }

    [Fact]
    public void EnrichMetadata_ExistingTvgId_NotOverwritten()
    {
        var ch = Live("TRT 1", "Ulusal");
        ch.TvgId = "trt1.original";
        _sut.EnrichMetadata(new List<Channel> { ch });
        Assert.Equal("trt1.original", ch.TvgId);
    }

    [Fact]
    public void EnrichMetadata_PipePrefixedName_GeneratesCleanEpgId()
    {
        // "|TR| Show TV HD" → TvgId çöp içermemeli
        var ch = Live("|TR| Show TV HD", "Diziler");
        ch.TvgId = null;
        _sut.EnrichMetadata(new List<Channel> { ch });
        Assert.DoesNotContain("|", ch.TvgId!);
        Assert.DoesNotContain(" ", ch.TvgId!);
    }

    [Theory]
    [InlineData("|TR| Show TV HD",   "ShowTV")]
    [InlineData("TRT 1",             "TRT1")]
    [InlineData("Kanal D",           "KanalD")]
    public void EnrichMetadata_EpgId_PascalCaseNoSpaces(string channelName, string expectedEpgId)
    {
        var ch = Live(channelName, "Test");
        ch.TvgId = null;
        _sut.EnrichMetadata(new List<Channel> { ch });
        Assert.Equal(expectedEpgId, ch.TvgId);
    }

    // =========================================================================
    // F — Organize (tam pipeline entegrasyon)
    // =========================================================================

    [Fact]
    public void Organize_NullInput_ReturnsEmpty()
    {
        var result = _sut.Organize(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void Organize_EmptyList_ReturnsEmpty()
    {
        Assert.Empty(_sut.Organize(new List<Channel>()));
    }

    [Fact]
    public void Organize_RemovesDuplicatesAndCategorizes()
    {
        var channels = new List<Channel>
        {
            Live("TRT 1", null, "http://a/1"),
            Live("TRT 1", null, "http://a/1"), // duplicate
            Live("CNN Türk", null),             // kategorisiz Haber
        };
        var result = _sut.Organize(channels);
        Assert.Equal(2, result.Count);
        var cnn = result.First(c => c.Name == "CNN Türk");
        Assert.Equal("Uncategorized", cnn.GroupTitle);
    }

    [Fact]
    public void Organize_AllChannelsGetTvgId()
    {
        var channels = new List<Channel>
        {
            Live("Kanal D", "Diziler"),
            Live("TRT 1",   "Ulusal"),
        };
        foreach (var ch in channels) ch.TvgId = null;
        var result = _sut.Organize(channels);
        Assert.All(result, c => Assert.False(string.IsNullOrWhiteSpace(c.TvgId)));
    }

    [Fact]
    public void Organize_CountDecreasesByDuplicateCount()
    {
        var channels = Enumerable.Range(1, 10)
            .Select(i => Live("Aynı Kanal", "Ulusal", "http://s/1"))
            .ToList();
        var result = _sut.Organize(channels);
        Assert.Single(result);
    }

    [Fact]
    public void Organize_SeriesEpisodesAllRetained()
    {
        var channels = Enumerable.Range(1, 5)
            .Select(i => Series($"Breaking Bad S01E0{i}", "Yabancı", $"http://s/{i}"))
            .ToList();
        var result = _sut.Organize(channels);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void Organize_IptvOrgSeriesGenreGroup_DoesNotMoveLinearChannelsToSeries()
    {
        var channels = new List<Channel>
        {
            Live("13 Teleseries (720p)", "Series", "https://origin.dpsgo.com/ssai/event/f4TrySe8SoiGF8Lu3EIq1g/master.m3u8"),
            Live("48 Hours", "Series", "https://jmp2.uk/plu-6346937a46f9a2000889073d.m3u8")
        };

        var organized = _sut.Organize(channels);

        Assert.All(organized, c => Assert.Equal(ChannelType.Live, c.Type));
    }
}

// =============================================================================
// SeriesProgressIdentityTests
// SeriesProgressIdentity internal static class — reflection ile erişim
// Toplam: 22 test
// =============================================================================

public class SeriesProgressIdentityTests
{
    private static readonly Type _type = typeof(Noctra.Services.SeriesInfoParser).Assembly
        .GetType("Noctra.Services.SeriesProgressIdentity")!;

    private static string NormalizeSeriesKey(string? name)
    {
        var m = _type.GetMethod("NormalizeSeriesKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        return (string)m.Invoke(null, new object?[] { name })!;
    }

    private static string BuildEpisodeKey(int season, int episode)
    {
        var m = _type.GetMethod("BuildEpisodeKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        return (string)m.Invoke(null, new object[] { season, episode })!;
    }

    private static (int Season, int Episode) ParseSeasonEpisode(string? title)
    {
        var m = _type.GetMethod("ParseSeasonEpisode",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var result = m.Invoke(null, new object?[] { title })!;
        var season  = (int)result.GetType().GetField("Item1")!.GetValue(result)!;
        var episode = (int)result.GetType().GetField("Item2")!.GetValue(result)!;
        return (season, episode);
    }

    // ─── NormalizeSeriesKey ───────────────────────────────────────────────────

    [Fact]
    public void NormalizeSeriesKey_Null_ReturnsEmpty()
        => Assert.Equal(string.Empty, NormalizeSeriesKey(null));

    [Fact]
    public void NormalizeSeriesKey_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, NormalizeSeriesKey(""));

    [Theory]
    [InlineData("Breaking Bad",   "breaking bad")]
    [InlineData("Kurtlar Vadisi", "kurtlar vadisi")]
    public void NormalizeSeriesKey_Basic_LowercaseAndTrimmed(string input, string expected)
        => Assert.Equal(expected, NormalizeSeriesKey(input));

    [Theory]
    [InlineData("Şeker Portakalı",  "seker portakali")]
    [InlineData("Çukur",            "cukur")]
    [InlineData("Güldür Güldür",    "guldur guldur")]
    [InlineData("Kış Güneşi",       "kis gunesi")]
    public void NormalizeSeriesKey_TurkishChars_Normalized(string input, string expected)
        => Assert.Equal(expected, NormalizeSeriesKey(input));

    [Theory]
    [InlineData("Breaking Bad S01E01", "breaking bad")]
    [InlineData("Kurtlar Vadisi 2x05", "kurtlar vadisi")]
    public void NormalizeSeriesKey_EpisodeTokensStripped(string input, string expected)
        => Assert.Equal(expected, NormalizeSeriesKey(input));

    [Fact]
    public void NormalizeSeriesKey_PipePrefixed_PrefixStripped()
    {
        var key = NormalizeSeriesKey("TR | Breaking Bad");
        Assert.Equal("breaking bad", key);
    }

    [Fact]
    public void NormalizeSeriesKey_DifferentProvidersameShow_SameKey()
    {
        var k1 = NormalizeSeriesKey("TR | Breaking Bad HD");
        var k2 = NormalizeSeriesKey("EN | Breaking Bad FHD");
        Assert.Equal(k1, k2);
    }

    // ─── BuildEpisodeKey ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(1,  1,  "s001e0001")]
    [InlineData(3,  12, "s003e0012")]
    [InlineData(10, 100,"s010e0100")]
    public void BuildEpisodeKey_Format_MatchesExpected(int season, int episode, string expected)
        => Assert.Equal(expected, BuildEpisodeKey(season, episode));

    [Fact]
    public void BuildEpisodeKey_ZeroSeason_ClampsToOne()
    {
        var key = BuildEpisodeKey(0, 1);
        Assert.StartsWith("s001", key);
    }

    [Fact]
    public void BuildEpisodeKey_ZeroEpisode_ClampsToOne()
    {
        var key = BuildEpisodeKey(1, 0);
        Assert.EndsWith("e0001", key);
    }

    [Fact]
    public void BuildEpisodeKey_NegativeValues_ClampsToOne()
    {
        var key = BuildEpisodeKey(-5, -3);
        Assert.Equal("s001e0001", key);
    }

    [Fact]
    public void BuildEpisodeKey_Deterministic_SameInputSameOutput()
    {
        Assert.Equal(BuildEpisodeKey(2, 5), BuildEpisodeKey(2, 5));
    }

    // ─── ParseSeasonEpisode ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Breaking Bad S02E05",              2, 5)]
    [InlineData("Kurtlar Vadisi Sezon 3 Bölüm 20",  3, 20)]
    [InlineData("Dark S01E08",                       1, 8)]
    public void ParseSeasonEpisode_StandardTitles_CorrectSeasonEpisode(
        string title, int expectedSeason, int expectedEpisode)
    {
        var (season, episode) = ParseSeasonEpisode(title);
        Assert.Equal(expectedSeason, season);
        Assert.Equal(expectedEpisode, episode);
    }

    [Fact]
    public void ParseSeasonEpisode_Null_ReturnsFallback()
    {
        var (season, episode) = ParseSeasonEpisode(null);
        // SeriesInfoParser.Parse(null) → Season=1, Episode=1
        Assert.True(season >= 0);
        Assert.True(episode >= 0);
    }
}
