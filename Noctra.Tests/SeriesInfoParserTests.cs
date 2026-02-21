using Noctra.Services;

namespace Noctra.Tests;

public class SeriesInfoParserTests
{
    [Theory]
    [InlineData("Breaking Bad S01E05", "Breaking Bad", 1, 5)]
    [InlineData("The Witcher 2x03", "The Witcher", 2, 3)]
    [InlineData("Dark S03 - E08", "Dark", 3, 8)]
    [InlineData("Kurtlar Vadisi Sezon 1 Bölüm 45", "Kurtlar Vadisi", 1, 45)]
    [InlineData("Game of Thrones Season 8 Episode 6", "Game of Thrones", 8, 6)]
    [InlineData("La Casa de Papel Temporada 2 Episodio 4", "La Casa de Papel", 2, 4)]
    [InlineData("Lupin Saison 1 Episode 3", "Lupin", 1, 3)]
    [InlineData("Tatort Staffel 3 Folge 115", "Tatort", 3, 115)]
    public void Parse_StandardPatterns_ReturnsCorrectInfo(string title, string expectedName, int expectedSeason, int expectedEpisode)
    {
        var result = SeriesInfoParser.Parse(title);

        Assert.Equal(expectedName, result.SeriesName);
        Assert.Equal(expectedSeason, result.Season);
        Assert.Equal(expectedEpisode, result.Episode);
    }

    [Theory]
    [InlineData("TR | Kanal D | Arka Sokaklar S15E560", "Arka Sokaklar", 15, 560)]
    [InlineData("TR | Show TV | Kurtlar Vadisi S02E10", "Kurtlar Vadisi", 2, 10)]
    [InlineData("EN | HBO | Game of Thrones S08E06", "Game of Thrones", 8, 6)]
    public void Parse_WithPipePrefixes_CleansNameCorrectly(string title, string expectedName, int expectedSeason, int expectedEpisode)
    {
        var result = SeriesInfoParser.Parse(title);

        Assert.Equal(expectedName, result.SeriesName);
        Assert.Equal(expectedSeason, result.Season);
        Assert.Equal(expectedEpisode, result.Episode);
    }

    [Theory]
    [InlineData("The Mandalorian Season 2", "The Mandalorian", 2, 1)]
    [InlineData("Friends Sezon 10", "Friends", 10, 1)]
    public void Parse_SeasonOnly_ReturnsEpisodeOne(string title, string expectedName, int expectedSeason, int expectedEpisode)
    {
        var result = SeriesInfoParser.Parse(title);

        Assert.Equal(expectedName, result.SeriesName);
        Assert.Equal(expectedSeason, result.Season);
        Assert.Equal(expectedEpisode, result.Episode);
    }

    [Theory]
    [InlineData(null, "Bilinmeyen Dizi", 1, 1)]
    [InlineData("", "Bilinmeyen Dizi", 1, 1)]
    [InlineData("   ", "Bilinmeyen Dizi", 1, 1)]
    public void Parse_EmptyOrNull_ReturnsFallback(string? title, string expectedName, int expectedSeason, int expectedEpisode)
    {
        var result = SeriesInfoParser.Parse(title);

        Assert.Equal(expectedName, result.SeriesName);
        Assert.Equal(expectedSeason, result.Season);
        Assert.Equal(expectedEpisode, result.Episode);
    }

    [Theory]
    [InlineData("Avengers Endgame (2019) 4k", false)]
    [InlineData("CNN International HD", false)]
    [InlineData("The Boys S03E01", true)]
    [InlineData("Vikings 5x10", true)]
    [InlineData("Leyla ile Mecnun Sezon 1", true)]
    public void IsSeries_IdentifiesCorrectType(string title, bool expectedIsSeries)
    {
        var result = SeriesInfoParser.IsSeries(title);

        Assert.Equal(expectedIsSeries, result);
    }

    [Theory]
    [InlineData("Breaking Bad", "breaking bad")]
    [InlineData("The Witcher S01E01", "the witcher")]
    [InlineData("TR | Kanal D | Arka Sokaklar (2020)", "arka sokaklar")]
    public void NormalizeKey_InternalConsistency(string title, string expectedKey)
    {
        var result = SeriesInfoParser.NormalizeKey(title);

        Assert.Equal(expectedKey, result);
    }

    [Theory]
    [InlineData("Sezon 1 • Bölüm 1", "Sezon 1 • Bölüm 1")]
    [InlineData("Sezon 5", "Sezon 5")]
    [InlineData("", "")]
    public void GetSeriesInfoText_FormatsCorrectly(string expected, string _)
    {
        // Just verify that known season/episode combos produce correct text
        Assert.Equal("Sezon 1 • Bölüm 1", SeriesInfoParser.GetSeriesInfoText(1, 1));
        Assert.Equal("Sezon 5", SeriesInfoParser.GetSeriesInfoText(5, 0));
        Assert.Equal("", SeriesInfoParser.GetSeriesInfoText(0, 0));
    }

    [Fact]
    public void CleanSeriesName_Null_ReturnsFallback()
    {
        Assert.Equal("Bilinmeyen Dizi", SeriesInfoParser.CleanSeriesName(null));
        Assert.Equal("Bilinmeyen Dizi", SeriesInfoParser.CleanSeriesName(""));
        Assert.Equal("Bilinmeyen Dizi", SeriesInfoParser.CleanSeriesName("   "));
    }

    [Fact]
    public void NormalizeKey_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SeriesInfoParser.NormalizeKey(null));
        Assert.Equal(string.Empty, SeriesInfoParser.NormalizeKey(""));
        Assert.Equal(string.Empty, SeriesInfoParser.NormalizeKey("   "));
    }
}
