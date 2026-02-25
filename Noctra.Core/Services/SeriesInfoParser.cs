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
                var seasonStr = match.Groups["season"].Value;
                var episodeStr = match.Groups["episode"].Value;

                var season = ParseSafeInt(seasonStr, 1);
                var episode = ParseSafeInt(episodeStr, 1);
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

        // If it's a live series channel, we don't want to assign it a fake episode number
        if (IsLiveSeries(trimmedTitle))
        {
            return new SeriesInfo(CleanSeriesName(trimmedTitle), 0, 0); 
        }

        // Phase 23: Better fallback for "1. Bölüm" if TurkishEpisodeOnlyRegex missed it for some reason
        var epMatch = Regex.Match(trimmedTitle, @"(?<ep>\d{1,3})\.?\s*[Bb](?:o|ö)l(?:u|ü)m", RegexOptions.IgnoreCase);
        if (epMatch.Success)
        {
            var epNum = ParseSafeInt(epMatch.Groups["ep"].Value, 1);
            var rawName = trimmedTitle[..epMatch.Index].Trim().TrimEnd('-', '.', '|', ':', '_', ' ').Trim();
            return new SeriesInfo(CleanSeriesName(rawName), 1, epNum);
        }

        var fallbackName = CleanSeriesName(EpisodeTokenRegex().Replace(trimmedTitle, " "));
        return new SeriesInfo(fallbackName, 1, 1);
    }

    public static bool IsSeries(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        // If it looks like a 24/7 or Live channel, it's not a standard episodic series
        if (IsLiveSeries(title)) return false;

        return SxeRegex().IsMatch(title) || 
               XRegex().IsMatch(title) || 
               TurkishRegex().IsMatch(title) || 
               TurkishAltRegex().IsMatch(title) ||
               TurkishEpisodeOnlyRegex().IsMatch(title) ||
               EnglishRegex().IsMatch(title) ||
               SeriesPatternHyphen().IsMatch(title) ||
               SeasonOnlyRegex().IsMatch(title) ||
               title.Contains("Saison", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Staffel", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Temporada", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLiveSeries(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        // Heavily restricted LiveSeries detection to prevent content loss.
        // Only 24/7 or CANLI/LIVE keywords without any episode indicators are safe.
        if (title.Contains("7/24", StringComparison.OrdinalIgnoreCase) || 
            title.Contains("24/7", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((title.Contains("CANLI", StringComparison.OrdinalIgnoreCase) || 
             title.Contains("LIVE", StringComparison.OrdinalIgnoreCase)) &&
            !EpisodeTokenRegex().IsMatch(title))
        {
            return true;
        }

        return false;
    }

    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        
        var normalized = value.Trim().ToLowerInvariant();
        normalized = StripIptvPrefixes(normalized);
        normalized = EpisodeTokenRegex().Replace(normalized, " ");
        
        // Strip languages and quality tags for a pure series key
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

        // Decode URL encoded characters (e.g. %3 -> #)
        var cleaned = System.Net.WebUtility.UrlDecode(value).Trim();
        
        cleaned = StripIptvPrefixes(cleaned);
        cleaned = EpisodeTokenRegex().Replace(cleaned, " ");
        
        // We no longer strip years or general noise aggressively here because user wants to keep parentheses content
        // But we still want to clean up excessive symbols and underscores
        cleaned = cleaned.Replace('_', ' ');
        
        // Remove known noise but WITHOUT word boundaries if they are next to symbols we want to keep
        // Actually, let's keep it simple: keep the noise cleaning but ensure it doesn't break the structure
        cleaned = NoiseTokenRegex().Replace(cleaned, " ");
        
        cleaned = MultiSpaceRegex().Replace(cleaned, " ").Trim(' ', '-', '|', ':', '.');

        if (string.IsNullOrWhiteSpace(cleaned)) return "Bilinmeyen Dizi";

        return Deduplicate(cleaned);
    }

    public static string CleanEpisodeTitle(string? title, string? seriesName, int episodeNumber)
    {
        var label = GetEpisodeLabel(title);
        if (string.IsNullOrWhiteSpace(title))
        {
            var baseName = !string.IsNullOrWhiteSpace(seriesName) ? seriesName : "Bilinmeyen Dizi";
            return $"{baseName} - {episodeNumber}. {label}";
        }

        var subtitle = ExtractEpisodeSubtitle(title, seriesName, episodeNumber);
        var seriesPrefix = !string.IsNullOrWhiteSpace(seriesName) ? seriesName : "Bilinmeyen Dizi";

        if (string.IsNullOrWhiteSpace(subtitle))
        {
            return $"{seriesPrefix} - {episodeNumber}. {label}";
        }

        return $"{seriesPrefix} - {episodeNumber}. {label} - {subtitle}";
    }

    private static string GetEpisodeLabel(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Bölüm";

        if (title.Contains("Episodio", StringComparison.OrdinalIgnoreCase)) return "Episodio";
        if (title.Contains("Episode", StringComparison.OrdinalIgnoreCase)) return "Episode";
        if (title.Contains("Capitulo", StringComparison.OrdinalIgnoreCase) || title.Contains("Capítulo", StringComparison.OrdinalIgnoreCase)) return "Capitulo";
        if (title.Contains("Folge", StringComparison.OrdinalIgnoreCase)) return "Folge";
        if (title.Contains("Bölüm", StringComparison.OrdinalIgnoreCase) || title.Contains("Bolum", StringComparison.OrdinalIgnoreCase)) return "Bölüm";
        
        // Default to Bölüm for Turkish UI consistency, but can be smarter
        return "Bölüm";
    }

    private static string ExtractEpisodeSubtitle(string title, string? seriesName, int episodeNumber)
    {
        var cleaned = title.Trim();
        cleaned = StripIptvPrefixes(cleaned);

        // Remove series name if it appears in the episode title
        if (!string.IsNullOrWhiteSpace(seriesName))
        {
            // Try removing the exact series name with various separators
            var escapedSeries = Regex.Escape(seriesName);
            
            // 1. Remove at start/end (more aggressive, no word boundaries needed for start/end)
            cleaned = Regex.Replace(cleaned, $@"^\s*{escapedSeries}\s*[:\-._ ]*", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, $@"[:\-._ ]*\s*{escapedSeries}\s*$", " ", RegexOptions.IgnoreCase);
            
            // 2. Remove in middle (use non-word-boundary approach since name can end in symbols like ')')
            cleaned = Regex.Replace(cleaned, $@"(?:^|[\s\-\|\.\:\(\)\[\]]){escapedSeries}(?:[\s\-\|\.\:\(\)\[\]]|$)", " ", RegexOptions.IgnoreCase);
            
            // 3. Fallback: If series name had a year that we stripped, try matching the year-stripped version too
            var yearStripped = YearTokenRegex().Replace(seriesName, "").Trim();
            if (yearStripped != seriesName && !string.IsNullOrWhiteSpace(yearStripped))
            {
                var escapedYearStripped = Regex.Escape(yearStripped);
                cleaned = Regex.Replace(cleaned, $@"^\s*{escapedYearStripped}\s*[:\-._ ]*", " ", RegexOptions.IgnoreCase);
                cleaned = Regex.Replace(cleaned, $@"(?:^|[\s\-\|\.\:\(\)\[\]]){escapedYearStripped}(?:[\s\-\|\.\:\(\)\[\]]|$)", " ", RegexOptions.IgnoreCase);
            }
        }


        // cleaned = EpisodeTokenRegex().Replace(cleaned, " "); // Phase 22: Keep S01E01 tokens for better context

        // Remove specific "X. Bölüm" or "Bölüm X" if it matches episodeNumber
        var epPattern = $@"\b{episodeNumber}\.?\s*[Bb](?:o|ö)l(?:u|ü)m\b|\b[Bb](?:o|ö)l(?:u|ü)m\s*{episodeNumber}\b";
        cleaned = Regex.Replace(cleaned, epPattern, " ", RegexOptions.IgnoreCase);

        cleaned = YearTokenRegex().Replace(cleaned, " ");
        cleaned = LanguageTokenRegex().Replace(cleaned, " ");
        cleaned = NoiseTokenRegex().Replace(cleaned, " ");
        cleaned = cleaned.Replace('_', ' ').Replace('.', ' ');
        cleaned = MultiSpaceRegex().Replace(cleaned, " ").Trim(' ', '-', '|', ':', '.');

        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;

        return Deduplicate(cleaned);
    }

    private static string Deduplicate(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1) return text;

        var uniqueWords = new List<string>();
        foreach (var word in words)
        {
            if (uniqueWords.Count == 0 || !string.Equals(uniqueWords[^1], word, StringComparison.OrdinalIgnoreCase))
            {
                uniqueWords.Add(word);
            }
        }

        // Check for larger patterns like "A B A B"
        if (uniqueWords.Count % 2 == 0)
        {
            int half = uniqueWords.Count / 2;
            bool isRepeat = true;
            for (int i = 0; i < half; i++)
            {
                if (!string.Equals(uniqueWords[i], uniqueWords[i + half], StringComparison.OrdinalIgnoreCase))
                {
                    isRepeat = false;
                    break;
                }
            }
            if (isRepeat)
            {
                uniqueWords = uniqueWords.Take(half).ToList();
            }
        }

        return string.Join(" ", uniqueWords);
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

        // Phase 2: Strip known provider names that aren't part of the series name
        text = ProviderPrefixRegex().Replace(text, " ").Trim();

        // Phase 3: Strip pipe-delimited tags only (e.g. "Kanal D | ")
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

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?:[Ss]eason|[Ss]ezon|[Tt]emporada|[Ss]aison|[Ss]taffel|[Ss])\s*(?<season>\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonOnlyRegex();

    [GeneratedRegex(@"\b(?:[Ss]\d{1,2}\s*[Ee]\d{1,3}|\d{1,2}\s*[Xx]\s*\d{1,3}|\d{1,2}\.?\s*[Ss]ezon.*?[\d]{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Ss]ezon\s*\d{1,2}\s*[Bb](?:o|ö)l(?:u|ü)m\s*\d{1,3}|[Ss]eason\s*\d{1,2}\s*[Ee]pisode\s*\d{1,3}|[Tt]emporada\s*\d{1,2}\s*(?:[Ee]pisodio|epis(?:o|ó)dio|cap(?:i|í)tulo)\s*\d{1,3}|[Ss]aison\s*\d{1,2}\s*(?:[Ee]pisode|épisode)\s*\d{1,3}|[Ss]taffel\s*\d{1,2}\s*[Ff]olge\s*\d{1,3}|[Ee]p(?:isode)?\s*\d{1,3}|\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Bb](?:o|ö)l(?:u|ü)m\s*\d{1,3}|[Ff]olge\s*\d{1,3}|[Cc]ap(?:i|í)tulo\s*\d{1,3}|[Ss]eason\s*\d{1,2}|[Ss]ezon\s*\d{1,2}|[Tt]emporada\s*\d{1,2}|[Ss]aison\s*\d{1,2}|[Ss]taffel\s*\d{1,2}|[Ss]\s*\d{1,2}|[Ss]\d{1,2})\b", RegexOptions.IgnoreCase)]
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

    [GeneratedRegex(@"\b(DIZIAX|NETFLIX|AMAZON|PRIME|DISNEY|APPLE|EXXEN|GAIN|BLUTV|TOD|VOD|PREMIUM|VIP|HD|FHD|UHD|4K)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ProviderPrefixRegex();

    [GeneratedRegex(@"\b(?:19\d{2}|20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearTokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
