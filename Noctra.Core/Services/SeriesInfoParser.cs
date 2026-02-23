using System.Text;
using System.Text.RegularExpressions;

namespace Noctra.Services;

public static partial class SeriesInfoParser
{
    public record SeriesInfo(string SeriesName, int Season, int Episode);

    public static SeriesInfo Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new SeriesInfo("Bilinmeyen Dizi", 1, 1);
        }

        var trimmedTitle = title.Trim();
        
        foreach (var regex in GetParsingRegexes())
        {
            var match = regex.Match(trimmedTitle);
            if (match.Success)
            {
                var rawName = match.Groups["name"].Value;
                var seriesName = CleanSeriesName(rawName);
                var season = ParseSafeInt(match.Groups["season"].Value, 1);
                var episode = ParseSafeInt(match.Groups["episode"].Value, 1);
                return new SeriesInfo(seriesName, season, episode);
            }
        }

        var seasonOnly = SeasonOnlyRegex().Match(trimmedTitle);
        if (seasonOnly.Success)
        {
            var rawName = seasonOnly.Groups["name"].Value;
            var seriesName = CleanSeriesName(rawName);
            var season = ParseSafeInt(seasonOnly.Groups["season"].Value, 1);
            return new SeriesInfo(seriesName, season, 1);
        }

        var fallbackName = CleanSeriesName(EpisodeTokenRegex().Replace(trimmedTitle, " "));
        return new SeriesInfo(fallbackName, 1, 1);
    }

    public static bool IsSeries(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        return SxeRegex().IsMatch(title) || 
               XRegex().IsMatch(title) || 
               TurkishRegex().IsMatch(title) || 
               EnglishRegex().IsMatch(title) ||
               SeriesPatternHyphen().IsMatch(title) ||
               SeasonOnlyRegex().IsMatch(title);
    }

    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        
        var normalized = value.Trim().ToLowerInvariant();
        normalized = StripIptvPrefixes(normalized);
        normalized = EpisodeTokenRegex().Replace(normalized, " ");
        
        // Strip years, languages, and quality tags for a pure series key
        normalized = YearTokenRegex().Replace(normalized, " ");
        normalized = LanguageTokenRegex().Replace(normalized, " ");
        normalized = NoiseTokenRegex().Replace(normalized, " ");
        
        // Remove any brackets/parens/symbols left over
        normalized = SymbolsRegex().Replace(normalized, " ");

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

    public static string CleanSeriesName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Bilinmeyen Dizi";
        }

        var cleaned = value.Trim();
        cleaned = StripIptvPrefixes(cleaned);
        cleaned = EpisodeTokenRegex().Replace(cleaned, " ");
        cleaned = NoiseTokenRegex().Replace(cleaned, " ");
        cleaned = cleaned.Replace('_', ' ').Replace('.', ' ');
        cleaned = MultiSpaceRegex().Replace(cleaned, " ").Trim(' ', '-', '|', ':', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? "Bilinmeyen Dizi" : cleaned;
    }

    /// <summary>
    /// Strips IPTV-style prefixes like "TR | Kanal D | " or "EN." safely.
    /// CountryPrefixRegex handles short 2-3 letter codes with any delimiter.
    /// PipeTagRegex handles longer tags but ONLY with pipe delimiter (safe, unambiguous).
    /// </summary>
    private static string StripIptvPrefixes(string text)
    {
        // Phase 1: Strip country codes (safe with any delimiter including dots)
        text = CountryPrefixRegex().Replace(text, " ").Trim();

        // Phase 2: Strip pipe-delimited tags only (e.g. "Kanal D | ")
        if (text.Contains('|'))
        {
            bool changed;
            do
            {
                changed = false;
                var before = text;
                text = PipeTagRegex().Replace(text, " ").Trim();
                if (text != before) changed = true;
            } while (changed && text.Contains('|'));
        }

        return text;
    }

    public static string GetSeriesInfoText(int season, int episode)
    {
        if (season > 0 && episode > 0)
        {
            return $"Sezon {season} • Bölüm {episode}";
        }
        if (season > 0)
        {
            return $"Sezon {season}";
        }
        return string.Empty;
    }

    private static int ParseSafeInt(string? value, int fallback)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0) return parsed;
        return fallback;
    }

    private static IEnumerable<Regex> GetParsingRegexes()
    {
        yield return SxeRegex();
        yield return XRegex();
        yield return SeriesPatternHyphen();
        yield return TurkishRegex();
        yield return TurkishAltRegex();
        yield return TurkishEpisodeOnlyRegex();
        yield return EnglishRegex();
        yield return SpanishRegex();
        yield return PortugueseRegex();
        yield return FrenchRegex();
        yield return GermanRegex();
    }

    // Trailing \s*.*?bölüm vs.. is to consume garbage like " - 1. Bölüm" correctly.
    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss](?<season>\d{1,2})\s*[-._ ]*\s*[Ee](?<episode>\d{1,3})(?:\s*[-._ ]*\s*\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SxeRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?<season>\d{1,2})\s*[Xx]\s*(?<episode>\d{1,3})(?:\s*[-._ ]*\s*\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex XRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss](?<season>\d{1,2})\s*-\s*[Ee](?<episode>\d{1,3})(?:\s*[-._ ]*\s*\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPatternHyphen();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss]ezon\s*(?<season>\d{1,2}).*?[Bb](?:o|ö)l(?:u|ü)m\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?<season>\d{1,2})\.?\s*[Ss]ezon.*?(?<episode>\d{1,3})\.?\s*[Bb](?:o|ö)l(?:u|ü)m\b", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishAltRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?<episode>\d{1,3})\.?\s*[Bb](?:o|ö)l(?:u|ü)m\b", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishEpisodeOnlyRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss]eason\s*(?<season>\d{1,2}).*?[Ee]pisode\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?:[Tt]emporada|[Tt]emp)\s*(?<season>\d{1,2}).*?(?:[Ee]pisodio|[Cc]ap(?:i|í)tulo|[Ee]p)\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?:[Tt]emporada|[Tt]emp)\s*(?<season>\d{1,2}).*?(?:[Ee]pis(?:o|ó)dio|[Ee]p)\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PortugueseRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss]aison\s*(?<season>\d{1,2}).*?(?:[Ee](?:pisode|épisode)|[Ee]p)\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FrenchRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss]taffel\s*(?<season>\d{1,2}).*?[Ff]olge\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex GermanRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?:[Ss]eason|[Ss]ezon|[Tt]emporada|[Ss]aison|[Ss]taffel)\s*(?<season>\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonOnlyRegex();

    [GeneratedRegex(@"\b(?:[Ss]\d{1,2}\s*[Ee]\d{1,3}|\d{1,2}\s*[Xx]\s*\d{1,3}|\d{1,2}\.?\s*[Ss]ezon.*?[\d]{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Ss]ezon\s*\d{1,2}\s*[Bb](?:o|ö)l(?:u|ü)m\s*\d{1,3}|[Ss]eason\s*\d{1,2}\s*[Ee]pisode\s*\d{1,3}|[Tt]emporada\s*\d{1,2}\s*(?:[Ee]pisodio|epis(?:o|ó)dio|cap(?:i|í)tulo)\s*\d{1,3}|[Ss]aison\s*\d{1,2}\s*(?:[Ee]pisode|épisode)\s*\d{1,3}|[Ss]taffel\s*\d{1,2}\s*[Ff]olge\s*\d{1,3}|[Ee]p(?:isode)?\s*\d{1,3}|\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Bb](?:o|ö)l(?:u|ü)m\s*\d{1,3}|[Ff]olge\s*\d{1,3}|[Cc]ap(?:i|í)tulo\s*\d{1,3}|[Ss]eason\s*\d{1,2}|[Ss]ezon\s*\d{1,2}|[Tt]emporada\s*\d{1,2}|[Ss]aison\s*\d{1,2}|[Ss]taffel\s*\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeTokenRegex();

    [GeneratedRegex(@"\b(?:4k|2160p|1080p|720p|480p|x264|x265|h264|h265|hevc|webrip|webdl|web-dl|bluray|brrip|bdrip|hdrip|camrip|hdcam|telesync|ts|remux|vip|vod|fhd|uhd|hd|sd|8k)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseTokenRegex();

    [GeneratedRegex(@"\b(?:tr|en|fr|de|ru|ar)\s+(?:dual|multi|altyaz(?:ı|i)l(?:ı|i)|altyaz(?:ı|i)|dublaj|sub|subbed|dubbed|dub)\b|\b(?:dual|multi|altyaz(?:ı|i)l(?:ı|i)|altyaz(?:ı|i)|dublaj|sub|subbed|dubbed|dub|tr-en)\b|\[(?:tr|en|fr|de|ru|ar)\]|\((?:tr|en|fr|de|ru|ar)\)", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageTokenRegex();

    [GeneratedRegex(@"[\[\]\(\)\{\}\-_\.\:]")]
    private static partial Regex SymbolsRegex();

    [GeneratedRegex(@"^\s*(?:[a-z]{2,3}\s*[|:\.\-]\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex CountryPrefixRegex();

    [GeneratedRegex(@"^\s*(?:[^|]+?\s*\|\s*)", RegexOptions.IgnoreCase)]
    private static partial Regex PipeTagRegex();

    [GeneratedRegex(@"\b(?:19\d{2}|20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearTokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
