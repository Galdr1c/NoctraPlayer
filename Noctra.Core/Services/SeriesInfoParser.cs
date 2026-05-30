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

        // If it's a live series channel, we don't want to assign it a fake episode number
        if (IsLiveSeries(trimmedTitle))
        {
            return new SeriesInfo(CleanSeriesName(trimmedTitle), 0, 0); 
        }
        
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

        // Phase 23: Better fallback for "1. Bölüm" if TurkishEpisodeOnlyRegex missed it for some reason
        var epMatch = Regex.Match(trimmedTitle, @"(?<ep>\d{1,3})\.?\s*[Bb](?:o|ö)l(?:u|ü)m", RegexOptions.IgnoreCase);
        if (epMatch.Success)
        {
            var epNum = ParseSafeInt(epMatch.Groups["ep"].Value, 1);
            var rawName = trimmedTitle[..epMatch.Index].Trim().TrimEnd('-', '.', '|', ':', '_', ' ').Trim();
            return new SeriesInfo(CleanSeriesName(rawName), 1, epNum);
        }

        var fallbackName = CleanSeriesName(EpisodeTokenRegex().Replace(trimmedTitle, " "));
        return new SeriesInfo(fallbackName, 0, 0);
    }

    public static bool IsSeries(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (IsLiveSeries(title)) return false;

        // Hem sezon hem bölüm birlikte olmalı
        return SxeRegex().IsMatch(title) ||       // S01E01
               XRegex().IsMatch(title) ||          // 1x01
               SeriesPatternHyphen().IsMatch(title) || // S01-E01
               TurkishRegex().IsMatch(title) ||    // Sezon 1 Bölüm 2
               TurkishAltRegex().IsMatch(title) || // 1. Sezon 2. Bölüm
               TurkishEpisodeOnlyRegex().IsMatch(title) || // 5. Bölüm
               EnglishRegex().IsMatch(title) ||    // Season 1 Episode 2
               SpanishRegex().IsMatch(title) ||
               PortugueseRegex().IsMatch(title) ||
               FrenchRegex().IsMatch(title) ||
               GermanRegex().IsMatch(title);
    }

    public static bool IsLiveSeries(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        if (LiveSportsRegex().IsMatch(title))
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

        if (LiveKeywordRegex().IsMatch(title) && !EpisodeTokenRegex().IsMatch(title))
        {
            return true;
        }

        return false;
    }

    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        
        var normalized = value.Trim().ToLowerInvariant()
            .Replace('ş', 's').Replace('ç', 'c').Replace('ğ', 'g')
            .Replace('ü', 'u').Replace('ö', 'o').Replace('ı', 'i').Replace('İ', 'i');
            
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
    /// Falls back to "en-US" if no language is detected.
    /// </summary>
    public static string ExtractLanguageCode(string? titleOrCategory)
    {
        if (string.IsNullOrWhiteSpace(titleOrCategory)) return "en-US";

        var trimmed = titleOrCategory.Trim().ToUpperInvariant();
        
        // 0. Check for explicit Full Country / Global Group names first
        if (trimmed.Contains("TURKEY") || trimmed.Contains("TÜRKİYE") || trimmed.Contains("TURKIYE")) return "tr-TR";

        var words = trimmed.Split(new[] { ' ', '|', '/', ':', '[', ']', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w == "UK" || w == "USA" || w == "US" || w == "UNITED STATES" || w == "UNITED KINGDOM" || w == "CANADA" || w == "AUSTRALIA" || w == "NEW ZEALAND")) return "en-US";

        if (trimmed.Contains("FRANCE") || trimmed.Contains("FRENCH")) return "fr-FR";
        if (trimmed.Contains("GERMANY") || trimmed.Contains("GERMAN") || trimmed.Contains("AUSTRIA") || trimmed.Contains("SWITZERLAND")) return "de-DE";
        if (trimmed.Contains("SPAIN") || trimmed.Contains("SPANISH") || trimmed.Contains("MEXICO") || trimmed.Contains("ARGENTINA") || trimmed.Contains("CHILE") || trimmed.Contains("COLOMBIA") || trimmed.Contains("PERU")) return "es-ES";
        if (trimmed.Contains("ITALY") || trimmed.Contains("ITALIAN")) return "it-IT";
        if (trimmed.Contains("RUSSIA") || trimmed.Contains("RUSSIAN")) return "ru-RU";
        if (trimmed.Contains("PORTUGAL") || trimmed.Contains("PORTUGUESE") || trimmed.Contains("BRAZIL")) return "pt-PT";
        if (trimmed.Contains("NETHERLANDS") || trimmed.Contains("DUTCH") || trimmed.Contains("BELGIUM")) return "nl-NL";
        if (trimmed.Contains("ALBANIA") || trimmed.Contains("ALBANIAN")) return "sq-AL";
        if (trimmed.Contains("GREECE") || trimmed.Contains("GREEK")) return "el-GR";
        if (trimmed.Contains("SWEDEN") || trimmed.Contains("SWEDISH")) return "sv-SE";
        if (trimmed.Contains("DENMARK") || trimmed.Contains("DANISH")) return "da-DK";
        if (trimmed.Contains("ARABIC") || trimmed.Contains("SAUDI ARABIA") || trimmed.Contains("EGYPT") || trimmed.Contains("UAE")) return "ar-SA";

        // 1. Check for Turkish prefixes with various delimiters (TR/, TR|, TR-, [TR], vb.)
        if (trimmed.StartsWith("TR/") || 
            trimmed.StartsWith("TR|") || 
            trimmed.StartsWith("TR-") ||
            trimmed.StartsWith("TR ") ||
            trimmed.StartsWith("TR:") ||
            trimmed.Contains("[TR]") ||
            trimmed.Contains("(TR)") ||
            trimmed.Contains("|TR|"))
        {
            return "tr-TR";
        }

        // 2. Check for strong English/International indicators anywhere as tags
        if (trimmed.Contains("MULTI") || 
            trimmed.Contains("ENG") ||
            trimmed.Contains("EN-US") ||
            trimmed.StartsWith("EU ") || 
            trimmed.StartsWith("EU|") ||
            trimmed.StartsWith("EU/") ||
            trimmed.StartsWith("UK ") ||
            trimmed.StartsWith("UK:") ||
            trimmed.StartsWith("US ") ||
            trimmed.StartsWith("US:") ||
            trimmed.StartsWith("CA ") ||
            trimmed.StartsWith("CA:") ||
            trimmed.StartsWith("AU ") ||
            trimmed.StartsWith("AU:"))
        {
            return "en-US";
        }

        // 3. Check for other common country prefixes (Tolerant matching)
        if (trimmed.StartsWith("DE/") || trimmed.StartsWith("DE|") || trimmed.StartsWith("DE-") || trimmed.StartsWith("DE:") || trimmed.StartsWith("DE ")) return "de-DE";
        if (trimmed.StartsWith("FR/") || trimmed.StartsWith("FR|") || trimmed.StartsWith("FR-") || trimmed.StartsWith("FR:") || trimmed.StartsWith("FR ")) return "fr-FR";
        if (trimmed.StartsWith("ES/") || trimmed.StartsWith("ES|") || trimmed.StartsWith("ES-") || trimmed.StartsWith("ES:") || trimmed.StartsWith("ES ")) return "es-ES";
        if (trimmed.StartsWith("IT/") || trimmed.StartsWith("IT|") || trimmed.StartsWith("IT-") || trimmed.StartsWith("IT:") || trimmed.StartsWith("IT ")) return "it-IT";
        if (trimmed.StartsWith("PT/") || trimmed.StartsWith("PT|") || trimmed.StartsWith("PT-") || trimmed.StartsWith("PT:") || trimmed.StartsWith("PT ")) return "pt-PT";
        if (trimmed.StartsWith("NL/") || trimmed.StartsWith("NL|") || trimmed.StartsWith("NL-") || trimmed.StartsWith("NL:") || trimmed.StartsWith("NL ")) return "nl-NL";
        if (trimmed.StartsWith("AL/") || trimmed.StartsWith("AL|") || trimmed.StartsWith("AL-") || trimmed.StartsWith("AL:") || trimmed.StartsWith("AL ")) return "sq-AL";
        if (trimmed.StartsWith("CL/") || trimmed.StartsWith("CL|") || trimmed.StartsWith("CL-") || trimmed.StartsWith("CL:") || trimmed.StartsWith("CL ")) return "es-CL";
        if (trimmed.StartsWith("AR/") || trimmed.StartsWith("AR|") || trimmed.StartsWith("AR-") || trimmed.StartsWith("AR:") || trimmed.StartsWith("AR ")) return "ar-SA";
        if (trimmed.StartsWith("RU/") || trimmed.StartsWith("RU|") || trimmed.StartsWith("RU-") || trimmed.StartsWith("RU:") || trimmed.StartsWith("RU ")) return "ru-RU";

        // 4. Strict tag matching: [TR], (EN), |DE|, {FR}, vs.
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
                        case "EN": case "UK": case "US": case "EU": case "CA": case "AU": case "MULTI": return "en-US";
                        case "DE": return "de-DE";
                        case "FR": return "fr-FR";
                        case "ES": case "SP": case "CL": case "MX": return "es-ES";
                        case "IT": return "it-IT";
                        case "RU": return "ru-RU";
                        case "AR": return "ar-SA";
                        case "NL": return "nl-NL";
                        case "PT": return "pt-PT";
                        case "PL": return "pl-PL";
                        case "GR": return "el-GR";
                        case "SE": return "sv-SE";
                        case "DK": return "da-DK";
                        case "AL": return "sq-AL";
                    }
                }
            }
        }

        // Fallback checks for common tags that might not be tightly wrapped 
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
        return "en-US";
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
        var seriesPrefix = !string.IsNullOrWhiteSpace(seriesName) ? seriesName : "Bilinmeyen Dizi";
        
        if (string.IsNullOrWhiteSpace(title))
        {
            return $"{seriesPrefix} - Ep {episodeNumber}";
        }

        var subtitle = ExtractEpisodeSubtitle(title, seriesName, episodeNumber);

        if (string.IsNullOrWhiteSpace(subtitle))
        {
            return $"{seriesPrefix} - Ep {episodeNumber}";
        }

        return $"{seriesPrefix} - {subtitle}";
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
            // Only deduplicate if word is relatively long (>4 chars) 
            // This allows title patterns like "Bang Bang Baby", "Bye Bye", "No No"
            bool isDuplicate = uniqueWords.Count > 0 && 
                               string.Equals(uniqueWords[^1], word, StringComparison.OrdinalIgnoreCase) &&
                               word.Length > 4;

            if (uniqueWords.Count == 0 || !isDuplicate)
            {
                uniqueWords.Add(word);
            }
        }

        // Fix non-symmetric repeats (e.g. "Bad Breaking Bad" -> "Breaking Bad")
        if (uniqueWords.Count >= 3)
        {
            for (int size = 1; size <= uniqueWords.Count / 2; size++)
            {
                for (int start = 0; start <= uniqueWords.Count - (size * 2); start++)
                {
                    bool isMatch = true;
                    for (int i = 0; i < size; i++)
                    {
                        if (!string.Equals(uniqueWords[start + i], uniqueWords[start + size + i], StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = false;
                            break;
                        }
                    }

                    if (isMatch)
                    {
                        // Only deduplicate single words if they are long (>4)
                        // Short words like "Bang Bang", "Bye Bye" are often legitimate
                        if (size == 1 && uniqueWords[start].Length <= 4)
                        {
                            continue;
                        }

                        uniqueWords.RemoveRange(start + size, size);
                        // Reset search after removal
                        size = 0; 
                        break; 
                    }
                }
            }
        }
        else if (uniqueWords.Count % 2 == 0) // Legacy check for exact halves just in case
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
            int maxIterations = 10;
            do
            {
                changed = false;
                var before = text;
                text = PipeTagRegex().Replace(text, " ").Trim();
                if (text != before) changed = true;
            } while (changed && maxIterations-- > 0 && (text.Contains('|') || text.Contains('>') || text.Contains('»')));
        }

        return text;
    }

    public static string GetSeriesInfoText(int season, int episode)
    {
        if (season > 0 && episode > 0)
        {
            return $"S{season} E{episode}";
        }
        if (season > 0)
        {
            return $"Season {season}";
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

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)\b(?<season>\d{1,2})[Xx](?<episode>\d{1,3})(?:\s*[-._ ]*\s*\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m)?\b", RegexOptions.IgnoreCase)]
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

    [GeneratedRegex(@"\b(?:[Ss]\d{1,2}\s*[Ee]\d{1,3}|\d{1,2}[Xx]\d{1,3}|\d{1,2}\.?\s*[Ss]ezon.*?[\d]{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Ss]ezon\s*\d{1,2}\s*[Bb](?:o|ö)l(?:u|ü)m\s*\d{1,3}|[Ss]eason\s*\d{1,2}\s*[Ee]pisode\s*\d{1,3}|[Tt]emporada\s*\d{1,2}\s*(?:[Ee]pisodio|epis(?:o|ó)dio|cap(?:i|í)tulo)\s*\d{1,3}|[Ss]aison\s*\d{1,2}\s*(?:[Ee]pisode|épisode)\s*\d{1,3}|[Ss]taffel\s*\d{1,2}\s*[Ff]olge\s*\d{1,3}|[Ee]p(?:isode)?\s*\d{1,3}|\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Bb](?:o|ö)l(?:u|ü)m\s*\d{1,3}|[Ff]olge\s*\d{1,3}|[Cc]ap(?:i|í)tulo\s*\d{1,3}|[Ss]eason\s*\d{1,2}|[Ss]ezon\s*\d{1,2}|[Tt]emporada\s*\d{1,2}|[Ss]aison\s*\d{1,2}|[Ss]taffel\s*\d{1,2}|[Ss]\s*\d{1,2}|[Ss]\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeTokenRegex();

    [GeneratedRegex(@"\b(?:4k|2160p|1080p|720p|480p|x264|x265|h264|h265|hevc|webrip|webdl|web-dl|bluray|brrip|bdrip|hdrip|camrip|hdcam|telesync|ts|remux|vip|vod|fhd|uhd|hd|sd|8k)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseTokenRegex();

    [GeneratedRegex(@"\b(?:tr|en|fr|de|ru|ar)\s+(?:dual|multi|altyaz(?:ı|i)l(?:ı|i)|altyaz(?:ı|i)|dublaj|sub|subbed|dubbed|dub)\b|\b(?:dual|multi|altyaz(?:ı|i)l(?:ı|i)|altyaz(?:ı|i)|dublaj|sub|subbed|dubbed|dub|tr-en)\b|\[(?:tr|en|fr|de|ru|ar)\]|\((?:tr|en|fr|de|ru|ar)\)", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageTokenRegex();

    [GeneratedRegex(@"[\[\]\(\)\{\}\-_\.\:]")]
    private static partial Regex SymbolsRegex();

    // Phase 1: Country prefixes like "TR | ", "EN.", "DE:" and bracketed tags like "(FR-)", "(S|UK)", "[VIP]", "|TR|", "{HD}"
    [GeneratedRegex(@"^\s*(?:(?:[\[\(\{|]\s*(?:TR|EN|UK|US|EU|CA|AU|DE|FR|ES|IT|PT|NL|AL|CL|AR|RU|PL|GR|SE|DK|IN|NO|FI|BE|CH|AT|IE|RO|BG|HU|CZ|SK|HR|SI|RS|BA|MK|ME|UA|BY|MD|BR|MX|CO|PE|VE|EC|GT|CU|BO|DO|HN|PY|SV|CR|UY|PA|NI|PR|ZA|NG|KE|GH|EG|MA|DZ|TN|LY|SY|IQ|JO|LB|YE|OM|QA|KW|AE|SA|PK|BD|AF|IR|IL|CN|JP|KR|VN|TH|ID|MY|PH|SG|TUR|ENG|USA|GBR|CAN|AUS|GER|FRA|ESP|ITA|POR|NLD|ALB|POL|GRE|SWE|DNK|NOR|FIN|BEL|CHE|AUT|ROU|BGR|HUN|CZE|SVK|HRV|SRB|UKR|RUS|ARA|IND|PAK|BRA|MEX|ARG|COL|PER|CHL|VEN|NGA|ZAF|VIP|HD|FHD|UHD|4K|S\|[A-Z]{2})\s*[\-\|:>\.]?\s*[\]\)\}\|]\s*)+|(?:(?:TR|EN|UK|US|EU|CA|AU|DE|FR|ES|IT|PT|NL|AL|CL|AR|RU|PL|GR|SE|DK|IN|NO|FI|BE|CH|AT|IE|RO|BG|HU|CZ|SK|HR|SI|RS|BA|MK|ME|UA|BY|MD|BR|MX|CO|PE|VE|EC|GT|CU|BO|DO|HN|PY|SV|CR|UY|PA|NI|PR|ZA|NG|KE|GH|EG|MA|DZ|TN|LY|SY|IQ|JO|LB|YE|OM|QA|KW|AE|SA|PK|BD|AF|IR|IL|CN|JP|KR|VN|TH|ID|MY|PH|SG|TUR|ENG|USA|GBR|CAN|AUS|GER|FRA|ESP|ITA|POR|NLD|ALB|POL|GRE|SWE|DNK|NOR|FIN|BEL|CHE|AUT|ROU|BGR|HUN|CZE|SVK|HRV|SRB|UKR|RUS|ARA|IND|PAK|BRA|MEX|ARG|COL|PER|CHL|VEN|NGA|ZAF)\s*[\-\|:>\.]\s*)+)+", RegexOptions.IgnoreCase)]
    private static partial Regex CountryPrefixRegex();

    [GeneratedRegex(@"^\s*(?:[^|]+?\s*\|\s*)", RegexOptions.IgnoreCase)]
    private static partial Regex PipeTagRegex();

    [GeneratedRegex(@"\b(DIZIAX|PREMIUM|VIP|HD|FHD|UHD|4K)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ProviderPrefixRegex();

    [GeneratedRegex(@"\b(?:19\d{2}|20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearTokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\b(beIN|be\*IN|be-IN|SPOR|EUROSPORT|TIVIBU|EXXENSPOR)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LiveSportsRegex();

    [GeneratedRegex(@"\b(CANLI|LIVE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LiveKeywordRegex();
}
