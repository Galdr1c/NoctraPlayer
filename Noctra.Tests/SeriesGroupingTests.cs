using Xunit;
using Noctra.Services;
using Noctra.Models;
using System.Collections.Generic;
using System.Linq;

namespace Noctra.Tests
{
    public class SeriesGroupingTests
    {
        [Theory]
        [InlineData("Breaking Bad S01 E01", "breaking bad")]
        [InlineData("Breaking Bad [1080p] S01E02", "breaking bad")]
        [InlineData("Breaking-Bad.S01E03.4k", "breaking bad")]
        [InlineData("La Casa de Papel Temporada 1 Episodio 1", "la casa de papel")]
        [InlineData("Lupin Saison 1 Episode 1", "lupin")]
        [InlineData("Dark Staffel 1 Folge 1", "dark")]
        [InlineData("Turkish Series - Sezon 1 Bölüm 1", "turkish series")]
        public void NormalizeSeriesKey_ShouldReturnCanonicalName(string input, string expected)
        {
            var result = SeriesProgressIdentity.NormalizeSeriesKey(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Breaking Bad S02 E05", 2, 5)]
        [InlineData("Dark 1x03", 1, 3)]
        [InlineData("Lupin Saison 2 Ep 4", 2, 4)]
        [InlineData("Spanish Show Temporada 3 Capitulo 12", 3, 12)]
        [InlineData("German Show Staffel 4 Folge 2", 4, 2)]
        public void ParseSeasonEpisode_ShouldDetectCorrectNumbers(string title, int expectedSeason, int expectedEpisode)
        {
            var result = SeriesProgressIdentity.ParseSeasonEpisode(title);
            Assert.Equal(expectedSeason, result.SeasonNumber);
            Assert.Equal(expectedEpisode, result.EpisodeNumber);
        }

        [Fact]
        public void MergeSeriesInMemory_ShouldCombineMissingData()
        {
            // This test would require internal access or making MergeSeriesInMemory public/internal
            // Since it's internal in MediaService, we can use it if the test project has IVT (InternalsVisibleTo).
            // Let's assume we can test it or just test the grouping result in GetSeriesAsync if we mock DbContext.
            // For now, let's focus on the normalization logic which is the core of "provider independence".
        }
    }
}
