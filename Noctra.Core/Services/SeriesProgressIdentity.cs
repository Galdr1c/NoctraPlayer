using System.Text;
using System.Text.RegularExpressions;
using Noctra.Models;

namespace Noctra.Services;

internal static partial class SeriesProgressIdentity
{
    public static string NormalizeSeriesKey(string? seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            return string.Empty;
        }

        var normalized = seriesName.Trim().ToLowerInvariant();
        normalized = CountryPrefixRegex().Replace(normalized, " ");
        normalized = EpisodeTokenRegex().Replace(normalized, " ");
        normalized = NoiseTokenRegex().Replace(normalized, " ");
        normalized = YearTokenRegex().Replace(normalized, " ");

        var buffer = new StringBuilder(normalized.Length);
        var previousSpace = false;
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer.Append(c);
                previousSpace = false;
                continue;
            }

            if (!previousSpace)
            {
                buffer.Append(' ');
                previousSpace = true;
            }
        }

        return MultiSpaceRegex().Replace(buffer.ToString(), " ").Trim();
    }

    public static (int SeasonNumber, int EpisodeNumber) ResolveSeasonEpisode(Episode episode)
    {
        var seasonNumber = episode.Season?.SeasonNumber ?? 0;
        var episodeNumber = episode.EpisodeNumber;

        if (seasonNumber > 0 && episodeNumber > 0)
        {
            return (seasonNumber, episodeNumber);
        }

        var parsed = ParseSeasonEpisode(episode.Name);
        if (seasonNumber <= 0)
        {
            seasonNumber = parsed.SeasonNumber;
        }

        if (episodeNumber <= 0)
        {
            episodeNumber = parsed.EpisodeNumber;
        }

        return (
            Math.Max(1, seasonNumber),
            Math.Max(1, episodeNumber)
        );
    }

    public static (int SeasonNumber, int EpisodeNumber) ParseSeasonEpisode(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (1, 1);
        }

        foreach (var regex in new[]
                 {
                     SxeRegex(),
                     XFormatRegex(),
                     TurkishRegex(),
                     EnglishRegex(),
                     SpanishRegex(),
                     PortugueseRegex(),
                     FrenchRegex(),
                     GermanRegex()
                 })
        {
            var match = regex.Match(title);
            if (!match.Success)
            {
                continue;
            }

            var season = ToPositiveInt(match.Groups["season"].Value, 1);
            var episode = ToPositiveInt(match.Groups["episode"].Value, 1);
            return (season, episode);
        }

        return (1, 1);
    }

    public static string BuildEpisodeKey(int seasonNumber, int episodeNumber)
    {
        return $"s{Math.Max(1, seasonNumber):000}e{Math.Max(1, episodeNumber):0000}";
    }

    private static int ToPositiveInt(string? raw, int fallback)
    {
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    [GeneratedRegex(@"[Ss](?<season>\d{1,2})\s*[Ee](?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex SxeRegex();

    [GeneratedRegex(@"(?<season>\d{1,2})\s*[Xx]\s*(?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex XFormatRegex();

    [GeneratedRegex(@"[Ss]ezon\s*(?<season>\d{1,2}).*?[Bb](?:o|\u00f6)l(?:u|\u00fc)m\s*(?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishRegex();

    [GeneratedRegex(@"[Ss]eason\s*(?<season>\d{1,2}).*?[Ee]pisode\s*(?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishRegex();

    [GeneratedRegex(@"(?:[Tt]emporada|[Tt]emp)\s*(?<season>\d{1,2}).*?(?:[Ee]pisodio|[Cc]ap(?:i|\u00ed)tulo|[Ee]p)\s*(?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishRegex();

    [GeneratedRegex(@"(?:[Tt]emporada|[Tt]emp)\s*(?<season>\d{1,2}).*?(?:[Ee]pis(?:o|\u00f3)dio|[Ee]p)\s*(?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex PortugueseRegex();

    [GeneratedRegex(@"[Ss]aison\s*(?<season>\d{1,2}).*?(?:[Ee](?:pisode|\u00e9pisode)|[Ee]p)\s*(?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex FrenchRegex();

    [GeneratedRegex(@"[Ss]taffel\s*(?<season>\d{1,2}).*?[Ff]olge\s*(?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex GermanRegex();

    [GeneratedRegex(@"\b(?:s\d{1,2}e\d{1,3}|\d{1,2}x\d{1,3}|sezon\s*\d{1,2}\s*b(?:o|\u00f6)l(?:u|\u00fc)m\s*\d{1,3}|season\s*\d{1,2}\s*episode\s*\d{1,3}|temporada\s*\d{1,2}\s*(?:episodio|epis(?:o|\u00f3)dio|cap(?:i|\u00ed)tulo)\s*\d{1,3}|saison\s*\d{1,2}\s*(?:episode|\u00e9pisode)\s*\d{1,3}|staffel\s*\d{1,2}\s*folge\s*\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeTokenRegex();

    [GeneratedRegex(@"\b(?:4k|2160p|1080p|720p|x264|x265|h264|h265|webrip|webdl|web-dl|bluray|dub|dublaj|altyazi|subtitle)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseTokenRegex();

    [GeneratedRegex(@"^\s*(?:[a-z]{2,3}\s*[\|\-:]\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex CountryPrefixRegex();

    [GeneratedRegex(@"\b(?:19\d{2}|20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearTokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
