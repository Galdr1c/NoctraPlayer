using System.Text;
using System.Text.RegularExpressions;
using Noctra.Models;

namespace Noctra.Services;

internal static partial class SeriesProgressIdentity
{
    public static string NormalizeSeriesKey(string? seriesName)
    {
        return SeriesInfoParser.NormalizeKey(seriesName);
    }

    public static (int SeasonNumber, int EpisodeNumber) ResolveSeasonEpisode(Episode episode)
    {
        var seasonNumber = episode.Season?.SeasonNumber ?? 0;
        var episodeNumber = episode.EpisodeNumber;

        if (seasonNumber > 0 && episodeNumber > 0)
        {
            return (seasonNumber, episodeNumber);
        }

        var info = SeriesInfoParser.Parse(episode.Name);
        if (seasonNumber <= 0)
        {
            seasonNumber = info.Season;
        }

        if (episodeNumber <= 0)
        {
            episodeNumber = info.Episode;
        }

        return (
            Math.Max(1, seasonNumber),
            Math.Max(1, episodeNumber)
        );
    }

    public static (int SeasonNumber, int EpisodeNumber) ParseSeasonEpisode(string? title)
    {
        var info = SeriesInfoParser.Parse(title);
        return (info.Season, info.Episode);
    }

    public static string BuildEpisodeKey(int seasonNumber, int episodeNumber)
    {
        return $"s{Math.Max(1, seasonNumber):000}e{Math.Max(1, episodeNumber):0000}";
    }
}
