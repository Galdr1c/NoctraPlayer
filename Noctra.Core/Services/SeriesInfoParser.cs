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
               title.Contains("Saison ", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Staffel ", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Temporada ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLiveSeries(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        // Common sports and live channel indicators that should never be treated as series
        // even if they contain numbers or suffixes that look like episode indicators.
        // Also handling obfuscated versions like "be*IN" or "be-IN"
        if (title.Contains("beIN", StringComparison.OrdinalIgnoreCase) || 
            title.Contains("be*IN", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("be-IN", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("SPOR", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("EUROSPORT", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("TIVIBU", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("EXXENSPOR", StringComparison.OrdinalIgnoreCase))
        {
            // Exception: If it explicitly has S01E01 style patterns, it might be a sports documentary series
            if (!SxeRegex().IsMatch(title) && !XRegex().IsMatch(title))
            {
                return true;
            }
        }

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

    [GeneratedRegex(@"[\|\[\(\{]([a-zA-Z]{2,5})[\|\]\)\}]")]
    private static partial Regex StrictLanguageCodeRegex();

    /// <summary>
    /// Analyzes the string (category or channel name) to detect language prefixes/tags 
    /// and returns a TMDB-compatible ISO language code (e.g. "tr-TR", "en-US").
    /// Falls back to "tr-TR" if no language is detected.
    /// </summary>
    public static string ExtractLanguageCode(string? titleOrCategory)
    {
        if (string.IsNullOrWhiteSpace(titleOrCategory)) return "tr-TR";

        var trimmed = titleOrCategory.Trim().ToUpperInvariant();
        
        // 1. Check for strong English/International indicators anywhere as tags
        if (trimmed.Contains("MULTI") || 
            trimmed.Contains("ENG") ||
            trimmed.Contains("EN-US") ||
            trimmed.StartsWith("EU ") || 
            trimmed.StartsWith("EU|") ||
            trimmed.StartsWith("EU/"))
        {
            return "en-US";
        }

        // 2. Check for Turkish prefixes with various delimiters (TR/, TR|, TR-, [TR], etc.)
        if (trimmed.StartsWith("TR/") || 
            trimmed.StartsWith("TR|") || 
            trimmed.StartsWith("TR-") ||
            trimmed.StartsWith("TR ") ||
            trimmed.Contains("[TR]") ||
            trimmed.Contains("(TR)") ||
            trimmed.Contains("|TR|"))
        {
            return "tr-TR";
        }

        // 3. Check for other common country prefixes
        if (trimmed.StartsWith("DE/") || trimmed.StartsWith("DE|") || trimmed.StartsWith("DE-")) return "de-DE";
        if (trimmed.StartsWith("FR/") || trimmed.StartsWith("FR|") || trimmed.StartsWith("FR-")) return "fr-FR";

        // 4. Strict tag matching: [TR], (EN), |DE|, {FR}, etc.
        var matches = StrictLanguageCodeRegex().Matches(trimmed);
        if (matches.Count > 0)
        {
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var code = match.Groups[1].Value.ToUpperInvariant();
                    switch (code)
                    {
                        case "TR": return "tr-TR";
                        case "EN": case "UK": case "US": case "EU": case "MULTI": return "en-US";
                        case "DE": return "de-DE";
                        case "FR": return "fr-FR";
                        case "ES": case "SP": return "es-ES";
                        case "IT": return "it-IT";
                        case "RU": return "ru-RU";
                        case "AR": return "ar-SA";
                        case "NL": return "nl-NL";
                        case "PT": return "pt-PT";
                        case "PL": return "pl-PL";
                        case "GR": return "el-GR";
                        case "SE": return "sv-SE";
                        case "DK": return "sv-SE"; // Sometimes DK/SE grouped, ideally da-DK
                        case "DA": return "da-DK";
                    }
                }
            }
        }

        // Fallback checks for common tags that might not be tightly wrapped 
        // e.g., "TR Dual", "EN Sub", which our LanguageTokenRegex handles.
        var langMatch = LanguageTokenRegex().Match(titleOrCategory);
        if (langMatch.Success)
        {
            var tag = langMatch.Value.ToUpperInvariant();
            if (tag.Contains("TR")) return "tr-TR";
            if (tag.Contains("EN")) return "en-US";
            if (tag.Contains("DE")) return "de-DE";
            if (tag.Contains("FR")) return "fr-FR";
            if (tag.Contains("RU")) return "ru-RU";
            if (tag.Contains("AR")) return "ar-SA";
        }

        // Default
        return "tr-TR";
    }

    /// <summary>
    /// Detects common streaming platforms from category or series names.
    /// </summary>
    public static string? ExtractPlatform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLower().Replace('ı', 'i').Replace('İ', 'i');

        if (lower.Contains("netflix")) return "Netflix";
        if (lower.Contains("hbo")) return "HBO";
        if (lower.Contains("disney")) return "Disney+";
        if (lower.Contains("amazon") || lower.Contains("prime")) return "Amazon";
        if (lower.Contains("apple")) return "Apple TV+";
        if (lower.Contains("paramount")) return "Paramount+";
        if (lower.Contains("hulu")) return "Hulu";
        if (lower.Contains("exxen")) return "Exxen";
        if (lower.Contains("blutv")) return "BluTV";
        if (lower.Contains("gain")) return "GAİN";
        if (lower.Contains("tod")) return "TOD";
        if (lower.Contains("tv plus") || lower.Contains("tv+") || lower.Contains("turkcell")) return "TV+";
        
        return null;
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
        
        // Remove known noise but WITHOUT word boundaries if they are next to symbols we want to keep
        cleaned = cleaned.Replace('_', ' ');
        cleaned = NoiseTokenRegex().Replace(cleaned, " ");
        
        cleaned = MultiSpaceRegex().Replace(cleaned, " ").Trim(' ', '-', '|', ':', '.', '>', '»');

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
        cleaned = MultiSpaceRegex().Replace(cleaned, " ").Trim(' ', '-', '|', ':', '.', '>', '»');

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
    public static string StripIptvPrefixes(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Phase 1: Strip country codes (safe with any delimiter including dots and slashes)
        text = CountryPrefixRegex().Replace(text, " ").Trim();

        // Phase 2: Strip known provider names that aren't part of the series name
        text = ProviderPrefixRegex().Replace(text, " ").Trim();

        // Phase 3: Strip pipe or angle bracket delimited tags only (e.g. "Kanal D | ", "TR/DIZI > ")
        if (text.Contains('|') || text.Contains('>') || text.Contains('»'))
        {
            bool changed;
            do
            {
                changed = false;
                var before = text;
                text = PipeTagRegex().Replace(text, " ").Trim();
                if (text != before) changed = true;
            } while (changed && (text.Contains('|') || text.Contains('>') || text.Contains('»')));
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
    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss](?<season>\d{1,2})\s*[-._ ]*\s*[Ee](?<episode>\d{1,3})(?:\s*[-._ ]*\s*\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SxeRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b(?<season>\d{1,2})\s*[Xx]\s*(?<episode>\d{1,3})(?:\s*[-._ ]*\s*\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex XRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss](?<season>\d{1,2})\s*-\s*[Ee](?<episode>\d{1,3})(?:\s*[-._ ]*\s*\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPatternHyphen();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss]ezon\s*(?<season>\d{1,2}).*?[Bb](?:o|ö)l(?:u|ü)m\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b(?<season>\d{1,2})\.?\s*[Ss]ezon.*?(?<episode>\d{1,3})\.?\s*[Bb](?:o|ö)l(?:u|ü)m\b", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishAltRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b(?<episode>\d{1,3})\.?\s*[Bb](?:o|ö)l(?:u|ü)m\b", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishEpisodeOnlyRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss]eason\s*(?<season>\d{1,2}).*?[Ee]pisode\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b(?:[Tt]emporada|[Tt]emp)\s*(?<season>\d{1,2}).*?(?:[Ee]pisodio|[Cc]ap(?:i|í)tulo|[Ee]p)\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b(?:[Tt]emporada|[Tt]emp)\s*(?<season>\d{1,2}).*?(?:[Ee]pis(?:o|ó)dio|[Ee]p)\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PortugueseRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss]aison\s*(?<season>\d{1,2}).*?(?:[Ee](?:pisode|épisode)|[Ee]p)\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FrenchRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss]taffel\s*(?<season>\d{1,2}).*?[Ff]olge\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex GermanRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b(?:[Ss]eason|[Ss]ezon|[Tt]emporada|[Ss]aison|[Ss]taffel|[Ss])\s*(?<season>\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonOnlyRegex();

    [GeneratedRegex(@"\b(?:[Ss]\d{1,2}\s*[Ee]\d{1,3}|\d{1,2}\s*[Xx]\s*\d{1,3}|\d{1,2}\.?\s*[Ss]ezon.*?[\d]{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Ss]ezon\s*\d{1,2}\s*[Bb](?:o|ö)l(?:u|ü)m\s*\d{1,3}|[Ss]eason\s*\d{1,2}\s*[Ee]pisode\s*\d{1,3}|[Tt]emporada\s*\d{1,2}\s*(?:[Ee]pisodio|epis(?:o|ó)dio|cap(?:i|í)tulo)\s*\d{1,3}|[Ss]aison\s*\d{1,2}\s*(?:[Ee]pisode|épisode)\s*\d{1,3}|[Ss]taffel\s*\d{1,2}\s*[Ff]olge\s*\d{1,3}|[Ee]p(?:isode)?\s*\d{1,3}|\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Bb](?:o|ö)l(?:u|ü)m\s*\d{1,3}|[Ff]olge\s*\d{1,3}|[Cc]ap(?:i|í)tulo\s*\d{1,3}|[Ss]eason\s*\d{1,2}|[Ss]ezon\s*\d{1,2}|[Tt]emporada\s*\d{1,2}|[Ss]aison\s*\d{1,2}|[Ss]taffel\s*\d{1,2}|[Ss]\s*\d{1,2}|[Ss]\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeTokenRegex();

    [GeneratedRegex(@"\b(?:4k|2160p|1080p|720p|480p|x264|x265|h264|h265|hevc|webrip|webdl|web-dl|bluray|brrip|bdrip|hdrip|camrip|hdcam|telesync|ts|remux|vip|vod|fhd|uhd|hd|sd|8k)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseTokenRegex();

    [GeneratedRegex(@"\b(?:tr|en|fr|de|ru|ar)\s+(?:dual|multi|altyaz(?:ı|i)l(?:ı|i)|altyaz(?:ı|i)|dublaj|sub|subbed|dubbed|dub)\b|\b(?:dual|multi|altyaz(?:ı|i)l(?:ı|i)|altyaz(?:ı|i)|dublaj|sub|subbed|dubbed|dub|tr-en)\b|\[(?:tr|en|fr|de|ru|ar)\]|\((?:tr|en|fr|de|ru|ar)\)", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageTokenRegex();

    [GeneratedRegex(@"[\[\]\(\)\{\}\-_\.\:]")]
    private static partial Regex SymbolsRegex();

    // Phase 1: Country prefixes like "TR | ", "EN.", "DE:" - but NOT "TR/DIZI"
    [GeneratedRegex(@"^\s*(?:[a-z]{2,3}\s*[|:\.]\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex CountryPrefixRegex();

    [GeneratedRegex(@"^\s*(?:[^|]+?\s*\|\s*)", RegexOptions.IgnoreCase)]
    private static partial Regex PipeTagRegex();

    [GeneratedRegex(@"\b(DIZIAX|PREMIUM|VIP|HD|FHD|UHD|4K)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ProviderPrefixRegex();

    [GeneratedRegex(@"\b(?:19\d{2}|20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearTokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
