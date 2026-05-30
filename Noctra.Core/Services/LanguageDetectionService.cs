namespace Noctra.Services;

/// <summary>
/// Kanal adlarından ülke/dil tespiti yapar
/// </summary>
public class LanguageDetectionService
{
    private static readonly Dictionary<string, string> CountryCodeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TR"] = "TR",
        ["TUR"] = "TR",
        ["TURKIYE"] = "TR",
        ["TURKEY"] = "TR",
        ["GB"] = "GB",
        ["UK"] = "GB",
        ["EN"] = "GB",
        ["ENGLAND"] = "GB",
        ["US"] = "US",
        ["USA"] = "US",
        ["DE"] = "DE",
        ["GER"] = "DE",
        ["GERMANY"] = "DE",
        ["DEUTSCHLAND"] = "DE",
        ["FR"] = "FR",
        ["FRA"] = "FR",
        ["FRANCE"] = "FR",
        ["IT"] = "IT",
        ["ITA"] = "IT",
        ["ITALY"] = "IT",
        ["ITALIA"] = "IT",
        ["ES"] = "ES",
        ["ESP"] = "ES",
        ["SPAIN"] = "ES",
        ["ESPANA"] = "ES",
        ["MX"] = "MX",
        ["MEX"] = "MX",
        ["MEXICO"] = "MX",
        ["AR"] = "AR",
        ["ARG"] = "AR",
        ["ARGENTINA"] = "AR",
        ["BR"] = "BR",
        ["BRA"] = "BR",
        ["BRAZIL"] = "BR",
        ["PORTUGAL"] = "PT",
        ["PT"] = "PT",
        ["NL"] = "NL",
        ["NLD"] = "NL",
        ["NETHERLANDS"] = "NL",
        ["RU"] = "RU",
        ["RUS"] = "RU",
        ["RUSSIA"] = "RU",
        ["AL"] = "AL",
        ["ALB"] = "AL",
        ["ALBANIA"] = "AL",
        ["GE"] = "GE",
        ["GEO"] = "GE",
        ["GEORGIA"] = "GE",
        ["GR"] = "GR",
        ["GRE"] = "GR",
        ["GREECE"] = "GR",
        ["HU"] = "HU",
        ["HUN"] = "HU",
        ["HUNGARY"] = "HU",
        ["HK"] = "HK",
        ["HONG KONG"] = "HK",
        ["SE"] = "SE",
        ["SWE"] = "SE",
        ["SWEDEN"] = "SE",
        ["CH"] = "CH",
        ["CHE"] = "CH",
        ["SWITZERLAND"] = "CH"
    };

    /// <summary>
    /// Ülke bazlı kanal kalıpları (öncelik sırasına göre)
    /// </summary>
    private static readonly Dictionary<string, string[]> CountryPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TR"] = new[]
        {
            "TRT", "Kanal D", "ATV", "Star TV", "Show TV", "TV8", "FOX TV", "Haber", "NTV",
            "CNN Türk", "A Haber", "TLC", "beyaz", "teve2", "TV360", "Habertürk", "S Sport",
            "TJK", "TGRT", "Ulusal", "Ülke TV", "KRT", "24 TV", "Semerkand", "Diyanet",
            "Bloomberg HT", "İhlas", "DMAX", "Euro D", "Kanal 7", "NOW TV", "Kral", "PowerTürk",
            "Number1", "BeinSport TR", "Spor Smart", "tabii", "Exxen", "Gain", "TV100",
            "Salon1", "Salon2", "Sinema", "TİVİBU"
        },
        ["GB"] = new[]
        {
            "BBC", "ITV", "Channel 4", "Channel 5", "Sky", "BT Sport", "Dave",
            "E4", "Film4", "More4", "5Star", "5USA", "Yesterday", "Drama",
            "CBeebies", "CBBC", "S4C", "UTV", "STV", "Quest"
        },
        ["US"] = new[]
        {
            "CNN", "ESPN", "HBO", "NBC", "CBS", "ABC", "FOX News", "MSNBC",
            "TNT", "TBS", "FX", "AMC", "Showtime", "Starz", "Bravo", "USA Network",
            "Syfy", "Discovery", "History", "Lifetime", "Hallmark", "BET",
            "Comedy Central", "MTV", "Nickelodeon", "Cartoon Network", "Disney",
            "National Geographic", "Animal Planet", "Food Network", "HGTV"
        },
        ["DE"] = new[]
        {
            "ARD", "ZDF", "RTL", "ProSieben", "SAT.1", "VOX", "DMAX DE",
            "Kabel Eins", "RTL II", "Super RTL", "ARTE", "Phoenix", "3sat",
            "N-TV", "WELT", "Sport1", "Sky DE", "Eurosport DE"
        },
        ["FR"] = new[]
        {
            "TF1", "France 2", "France 3", "France 5", "M6", "Canal+",
            "ARTE FR", "BFM TV", "CNews", "LCI", "TMC", "W9", "NRJ",
            "Gulli", "Planète"
        },
        ["IT"] = new[]
        {
            "Rai", "Canale 5", "Italia 1", "Rete 4", "La7", "TV8 IT",
            "Mediaset", "Sky Italia", "Cielo"
        },
        ["ES"] = new[]
        {
            "TVE", "Antena 3", "Telecinco", "La Sexta", "Cuatro", "RTVE",
            "Movistar", "Canal Sur", "TV3 Cat"
        },
        ["NL"] = new[]
        {
            "NPO", "RTL NL", "SBS", "Veronica", "Net5", "Ziggo"
        },
        ["RU"] = new[]
        {
            "Первый", "Россия", "НТВ", "ТНТ", "СТС", "РЕН ТВ",
            "Пятый", "Матч ТВ", "Звезда", "МИР", "ТВ Центр"
        },
        ["AR"] = new[]
        {
            "Al Jazeera", "MBC", "OSN", "Abu Dhabi", "Dubai TV",
            "Rotana", "LBC", "Al Arabiya", "beIN AR"
        },
        ["AL"] = new[] { "Tring", "Top Channel", "Klan", "Vizion", "RTSH" },
        ["GE"] = new[] { "1TV", "2TV", "Imedi", "Rustavi", "Mtavari", "Postv" },
        ["GR"] = new[] { "ERT", "Mega Channel", "Ant1", "Star Channel", "Alpha TV", "Skai TV", "Open TV" },
        ["HU"] = new[] { "M1", "M2", "M4", "M5", "Duna", "RTL Klub", "TV2", "Hír TV", "ATV" },
        ["HK"] = new[] { "RTHK", "TVB", "ViuTV", "HOY TV" },
        ["SE"] = new[] { "SVT", "TV4", "Kanal 5", "Kanal 9", "Kanal 11", "Kunskapskanalen" }
    };

    /// <summary>
    /// Tek bir kanal adından ülke kodunu tespit eder
    /// </summary>
    /// <param name="channelName">Kanal adı</param>
    /// <returns>ISO 3166-1 alpha-2 ülke kodu (varsayılan: "US")</returns>
    public string DetectCountryFromName(string? channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName)) return "US";

        // 1) Priority: Try tokens first (prefix markers like TR |, [DE], etc.)
        var tokens = Tokenize(channelName);
        foreach (var token in tokens)
        {
            if (CountryCodeAliases.TryGetValue(token, out var country))
            {
                return country;
            }
        }

        // 2) Fallback to patterns
        foreach (var (country, patterns) in CountryPatterns)
        {
            foreach (var pattern in patterns)
            {
                if (channelName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return country;
                }
            }
        }

        return "US"; // Default fallback
    }

    /// <summary>
    /// Kanal listesinden en baskın ülkeyi tespit eder
    /// </summary>
    /// <param name="channelNames">Kanal adları listesi</param>
    /// <returns>ISO 3166-1 alpha-2 ülke kodu (varsayılan: "US")</returns>
    public string DetectCountry(IEnumerable<Noctra.Models.Channel> channels)
    {
        if (channels == null || !channels.Any())
            return "US";

        var countries = DetectCountries(channels);
        return countries.Count > 0 ? countries[0].CountryCode : "US";
    }

    /// <summary>
    /// Birden fazla ülke tespiti yapar (multi-country playlists)
    /// </summary>
    public List<(string CountryCode, int ChannelCount, double Percentage)> DetectCountries(IEnumerable<Noctra.Models.Channel> channels)
    {
        var channelList = channels.ToList();
        if (channelList.Count == 0)
            return new List<(string, int, double)> { ("US", 0, 100) };

        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in channelList)
        {
            if (channel == null) continue;

            // 0) Priority: If channel already has Country metadata (e.g. from tvg-country)
            if (!string.IsNullOrWhiteSpace(channel.Country) && CountryCodeAliases.TryGetValue(channel.Country, out var countryFromMeta))
            {
                scores[countryFromMeta] = scores.GetValueOrDefault(countryFromMeta) + 1;
                continue;
            }

            var name = channel.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;

            // 1) Try tokens first (prefix markers like TR |, [DE], etc.)
            var tokens = Tokenize(name);
            bool foundViaToken = false;
            foreach (var token in tokens)
            {
                if (CountryCodeAliases.TryGetValue(token, out var country))
                {
                    scores[country] = scores.GetValueOrDefault(country) + 1;
                    foundViaToken = true;
                    break; 
                }
            }

            if (foundViaToken) continue;

            // 2) Fallback to patterns
            foreach (var (country, patterns) in CountryPatterns)
            {
                bool foundPattern = false;
                foreach (var pattern in patterns)
                {
                    if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        scores[country] = scores.GetValueOrDefault(country) + 1;
                        foundPattern = true;
                        break;
                    }
                }
                if (foundPattern) break;
            }
        }

        var total = Math.Max(1, scores.Values.Sum());

        return scores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value, Math.Round((double)kv.Value / total * 100, 1)))
            .ToList();
    }

    private static Dictionary<string, int> DetectCountryCodeScores(IEnumerable<string> names)
    {
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var tokens = Tokenize(name);
            foreach (var token in tokens)
            {
                if (CountryCodeAliases.TryGetValue(token, out var country))
                {
                    scores[country] = scores.GetValueOrDefault(country) + 1;
                }
            }
        }

        return scores;
    }

    private static IEnumerable<string> Tokenize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Array.Empty<string>();

        // Normalize circled letters (e.g. ⓣⓥ -> tv)
        var normalized = new System.Text.StringBuilder();
        foreach (var ch in name)
        {
            if (ch >= '\u24B6' && ch <= '\u24CF') // Uppercase A-Z
                normalized.Append((char)(ch - '\u24B6' + 'A'));
            else if (ch >= '\u24D0' && ch <= '\u24E9') // Lowercase a-z
                normalized.Append((char)(ch - '\u24D0' + 'a'));
            else
                normalized.Append(ch);
        }
        var processedName = normalized.ToString();

        // Find country codes directly inside common brackets/pipes like |TR| or [TR]
        var matches = System.Text.RegularExpressions.Regex.Matches(processedName, @"[\|\[\(\{]([a-zA-Z]{2,3})[\|\]\)\}]");
        if (matches.Count > 0)
        {
            var results = new List<string>();
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var token = match.Groups[1].Value.ToUpperInvariant();
                    if (CountryCodeAliases.ContainsKey(token))
                    {
                        results.Add(token);
                    }
                }
            }

            if (results.Count > 0)
            {
                return results;
            }
        }

        var chars = processedName.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray();
        return new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToUpperInvariant());
    }
}


