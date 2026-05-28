using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// M3U/M3U8 dosyalarını parse eden servis
/// </summary>
public partial class M3UParser : IM3UParser
{
    private readonly HttpClient _httpClient;
    public string? LastDetectedEpgUrl { get; private set; }

    public M3UParser(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Channel>> ParseAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new List<Channel>();
        using var reader = new StringReader(content);
        return await ParseFromReaderAsync(reader);
    }

    public async Task<List<Channel>> ParseFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("M3U dosyası bulunamadı", filePath);
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);
        return await ParseFromReaderAsync(reader);
    }

    public async Task<List<Channel>> ParseFromUrlAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            return await NetworkRetry.ExecuteAsync(async () =>
            {
                var channelsResult = await DownloadAndParseInternalAsync(url, null, cts.Token);
                
                // If 0 channels and it's a 404 or just suspicious empty, try fallback UA
                if (channelsResult.Count == 0 && !string.IsNullOrEmpty(url))
                {
                    System.Diagnostics.Debug.WriteLine($"[M3UParser] 0 channels or error for {url}. Retrying with VLC User-Agent...");
                    channelsResult = await DownloadAndParseInternalAsync(url, "VLC/3.0.18", cts.Token);
                }

                return channelsResult;
            }, cancellationToken: cts.Token);
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException($"M3U indirme zaman aşımına uğradı: {url}");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"M3U URL'sine erişilemedi: {url}\n" +
                $"Sunucu yanıt vermedi veya URL yanlış.", ex);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Geçersiz M3U formatı: {url}\n" +
                $"Dosya içeriği M3U standardına uygun değil.", ex);
        }
    }

    private async Task<List<Channel>> DownloadAndParseInternalAsync(string url, string? overrideUserAgent, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(overrideUserAgent))
            {
                request.Headers.UserAgent.Clear();
                request.Headers.TryAddWithoutValidation("User-Agent", overrideUserAgent);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            
            // If we got 404, we don't throw yet if we have no override agent, so the retry logic can catch it.
            // But if we have an override agent, or it's another error, we throw.
            if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(overrideUserAgent) && response.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<Channel>(); // Triggers the retry in ParseFromUrlAsync
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            
            try
            {
                return await ParseFromReaderAsync(reader);
            }
            catch (Exception ex) when (IsNetworkIncompletionException(ex))
            {
                System.Diagnostics.Debug.WriteLine($"[M3UParser] Warning: Stream ended prematurely for {url}. Returning partial results. Error: {ex.Message}");
                return _lastPartialChannels ?? new List<Channel>();
            }
        }
        catch (HttpRequestException ex) when (string.IsNullOrEmpty(overrideUserAgent) && 
                                              (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadGateway))
        {
            // Trigger retry for 404 and 502
            return new List<Channel>();
        }
    }

    private async Task<List<Channel>> ParseFromReaderAsync(TextReader reader)
    {
        var channels = new List<Channel>();
        LastDetectedEpgUrl = null;

        string? firstLine = null;
        while ((firstLine = await reader.ReadLineAsync()) != null)
        {
            if (!string.IsNullOrWhiteSpace(firstLine))
                break;
        }

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return channels;
        }

        // Header check should be lenient. Some providers might skip it or have garbage before it.
        bool hasHeader = firstLine.Trim().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase);
        if (hasHeader)
        {
            var xTvgUrlMatch = XTvgUrlRegex().Match(firstLine);
            if (xTvgUrlMatch.Success)
            {
                LastDetectedEpgUrl = ExtractFirstEpgUrl(xTvgUrlMatch.Groups[1].Value);
            }
        }
        else if (!firstLine.Trim().StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine($"[M3UParser] Warning: Unexpected first line (no #EXTM3U and no #EXTINF): {firstLine}");
        }

        Channel? currentChannel = null;
        _lastPartialChannels = channels;
        string? line = hasHeader ? await reader.ReadLineAsync() : firstLine;
        
        while (line != null)
        {
            line = line.Trim();

            if (!string.IsNullOrEmpty(line))
            {
                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    currentChannel = ParseExtInf(line);
                }
                else if (line.StartsWith("#EXTGRP", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentChannel != null)
                    {
                        currentChannel.GroupTitle = line.Substring(8).Trim();
                    }
                }
                else if (!line.StartsWith("#") && currentChannel != null)
                {
                    currentChannel.StreamUrl = line;
                    currentChannel.Type = DetectChannelType(line, currentChannel.Name, currentChannel.GroupTitle);
                    ProcessGroupTitleAndNameFallback(currentChannel);
                    channels.Add(currentChannel);
                    currentChannel = null;
                }
            }

            line = await reader.ReadLineAsync();
        }

        return channels;
    }

    /// <summary>
    /// #EXTINF satırını parse eder
    /// </summary>
    private Channel ParseExtInf(string line)
    {
        var channel = new Channel();

        // tvg-id çıkar
        var tvgIdMatch = TvgIdRegex().Match(line);
        if (tvgIdMatch.Success)
            channel.TvgId = tvgIdMatch.Groups[1].Success ? tvgIdMatch.Groups[1].Value : tvgIdMatch.Groups[2].Value;

        // tvg-name çıkar
        var tvgNameMatch = TvgNameRegex().Match(line);
        if (tvgNameMatch.Success)
            channel.TvgName = tvgNameMatch.Groups[1].Success ? tvgNameMatch.Groups[1].Value : tvgNameMatch.Groups[2].Value;

        // tvg-logo çıkar
        var tvgLogoMatch = TvgLogoRegex().Match(line);
        if (tvgLogoMatch.Success)
            channel.LogoUrl = tvgLogoMatch.Groups[1].Success ? tvgLogoMatch.Groups[1].Value : tvgLogoMatch.Groups[2].Value;

        // tvg-country çıkar
        var tvgCountryMatch = TvgCountryRegex().Match(line);
        if (tvgCountryMatch.Success)
            channel.Country = (tvgCountryMatch.Groups[1].Success ? tvgCountryMatch.Groups[1].Value : tvgCountryMatch.Groups[2].Value).ToUpperInvariant();

        // group-title çıkar
        var groupMatch = GroupTitleRegex().Match(line);
        if (groupMatch.Success)
            channel.GroupTitle = groupMatch.Groups[1].Success ? groupMatch.Groups[1].Value : groupMatch.Groups[2].Value;

        // Kanal adını çıkar
        var lastCommaIndex = line.LastIndexOf(',');
        if (lastCommaIndex >= 0)
        {
            channel.Name = line.Substring(lastCommaIndex + 1).Trim();
        }
        else
        {
            // Virgül yoksa (standart dışı), öznitelikleri temizleyip kalanı almayı dene
            var lineWithoutAttrs = AttributesRegex().Replace(line, "");
            var nameMatch = ChannelNameRegex().Match(lineWithoutAttrs);
            if (nameMatch.Success)
                channel.Name = nameMatch.Groups[1].Value.Trim();
            else
                channel.Name = channel.TvgName ?? "Bilinmeyen Kanal";
        }

        return channel;
    }

    /// <summary>
    /// Kanal türünü URL'den tespit eder.
    /// .ts/.m3u/.m3u8 veya /ts//m3u//m3u8 içeriyorsa → Live
    /// Series pattern varsa → Series
    /// Diğer her şey → VOD
    /// </summary>
    private static ChannelType DetectChannelType(string url, string name, string? groupTitle)
    {
        var lowerUrl = url.ToLowerInvariant();

        if (lowerUrl.Contains("/serie/") || lowerUrl.Contains("/series/") || lowerUrl.Contains("/dizi/") || lowerUrl.Contains("/diziler/") || lowerUrl.Contains("/tv_show/") || lowerUrl.Contains("/tv_shows/") || lowerUrl.Contains("type=series"))
            return ChannelType.Series;

        if (lowerUrl.Contains("/movie/") || lowerUrl.Contains("/movies/") || lowerUrl.Contains("/film/") || lowerUrl.Contains("/filmler/") || lowerUrl.Contains("/vod/") || lowerUrl.Contains("type=movie") || lowerUrl.Contains("type=vod"))
            return ChannelType.VOD;

        if (IsLinearStreamUrl(lowerUrl))
            return ChannelType.Live;

        if (SeriesInfoParser.IsSeries(name))
            return ChannelType.Series;

        return ChannelType.VOD;
    }

    /// <summary>
    /// URL'nin lineer (canlı) yayın akışı olduğunu gösterir.
    /// Hem uzantı hem yol segmenti kontrol edilir: /ts, /m3u8 gibi
    /// path segment'leri proxy tabanlı IPTV sağlayıcılarında yaygındır.
    /// </summary>
    private static bool IsLinearStreamUrl(string lowerUrl)
    {
        if (lowerUrl.Contains("/movie/") ||
            lowerUrl.Contains("/movies/") ||
            lowerUrl.Contains("/vod/") ||
            lowerUrl.Contains("/film/") ||
            lowerUrl.Contains("/filmler/") ||
            lowerUrl.Contains("/serie/") ||
            lowerUrl.Contains("/series/") ||
            lowerUrl.Contains("/dizi/") ||
            lowerUrl.Contains("/diziler/") ||
            lowerUrl.Contains("/tv_show/") ||
            lowerUrl.Contains("/tv_shows/") ||
            lowerUrl.Contains("type=vod") ||
            lowerUrl.Contains("type=movie") ||
            lowerUrl.Contains("type=series"))
        {
            return false;
        }

        var path = lowerUrl;
        var q = path.IndexOf('?');
        if (q >= 0)
            path = path[..q];

        var lastSlash = path.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            var lastSegment = path[(lastSlash + 1)..];
            if (lastSegment.Length > 0 && lastSegment.All(char.IsDigit))
            {
                return true;
            }
        }

        return path.EndsWith(".m3u8") ||
               path.EndsWith("/m3u8") ||   // proxy path segment
               path.EndsWith(".ts") ||
               path.EndsWith("/ts") ||     // proxy path segment — en yaygın canlı TV göstergesi
               path.EndsWith(".m3u") ||
               path.EndsWith("/m3u") ||
               lowerUrl.Contains("format=m3u8") ||
               lowerUrl.Contains("extension=m3u8") ||
               lowerUrl.Contains("extension=ts");
    }


    // Regex pattern'ları (Lenient versions)
    [GeneratedRegex(@"tvg-id\s*=\s*(?:""([^""]*)""|([^""\s,]+))", RegexOptions.IgnoreCase)]
    private static partial Regex TvgIdRegex();

    [GeneratedRegex(@"tvg-name\s*=\s*(?:""([^""]*)""|([^""\s,]+))", RegexOptions.IgnoreCase)]
    private static partial Regex TvgNameRegex();

    [GeneratedRegex(@"tvg-logo\s*=\s*(?:""([^""]*)""|([^""\s,]+))", RegexOptions.IgnoreCase)]
    private static partial Regex TvgLogoRegex();

    [GeneratedRegex(@"tvg-country\s*=\s*(?:""([^""]*)""|([^""\s,]+))", RegexOptions.IgnoreCase)]
    private static partial Regex TvgCountryRegex();

    [GeneratedRegex(@"group-title\s*=\s*(?:""([^""]*)""|([^""\s,]+))", RegexOptions.IgnoreCase)]
    private static partial Regex GroupTitleRegex();

    [GeneratedRegex(@"[a-zA-Z0-9_-]+\s*=\s*""[^""]*""|[a-zA-Z0-9_-]+\s*=\s*[^""\s,]+", RegexOptions.IgnoreCase)]
    private static partial Regex AttributesRegex();

    [GeneratedRegex(@"(?:,|\s)\s*([^,].*)$")]
    private static partial Regex ChannelNameRegex();

    [GeneratedRegex(@"x-tvg-url=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex XTvgUrlRegex();

    private static string? ExtractFirstEpgUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var candidates = raw
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var candidate in candidates)
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.ToString();
            }
        }

        return null;
    }

    private bool IsNetworkIncompletionException(Exception ex)
    {
        // Check for specific exceptions that indicate premature end of stream
        var msg = ex.Message.ToLowerInvariant();
        return ex is HttpRequestException || 
               ex is IOException || 
               msg.Contains("prematurely") || 
               msg.Contains("ended") || 
               msg.Contains("closed");
    }

    private static readonly HashSet<string> KnownCountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        // 2-Letter Codes
        "TR", "EN", "UK", "US", "EU", "CA", "AU", "DE", "FR", "ES", "IT", "PT", "NL", "AL", "CL", "AR", "RU", "PL", "GR", "SE", "DK", "IN",
        "NO", "FI", "BE", "CH", "AT", "IE", "RO", "BG", "HU", "CZ", "SK", "HR", "SI", "RS", "BA", "MK", "ME", "UA", "BY", "MD", "BR", "MX",
        "CO", "PE", "VE", "EC", "GT", "CU", "BO", "DO", "HN", "PY", "SV", "CR", "UY", "PA", "NI", "PR", "ZA", "NG", "KE", "GH", "EG", "MA",
        "DZ", "TN", "LY", "SY", "IQ", "JO", "LB", "YE", "OM", "QA", "KW", "AE", "SA", "PK", "BD", "AF", "IR", "IL", "CN", "JP", "KR", "VN",
        "TH", "ID", "MY", "PH", "SG", "WO",

        // 3-Letter Codes
        "TUR", "ENG", "USA", "GBR", "CAN", "AUS", "GER", "FRA", "ESP", "ITA", "POR", "NLD", "ALB", "POL", "GRE", "SWE", "DNK", "NOR", "FIN",
        "BEL", "CHE", "AUT", "ROU", "BGR", "HUN", "CZE", "SVK", "HRV", "SRB", "UKR", "RUS", "ARA", "IND", "PAK", "BRA", "MEX", "ARG", "COL",
        "PER", "CHL", "VEN", "NGA", "ZAF"
    };

    private static readonly Dictionary<string, string> CountryNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "TURKEY", "TR" }, { "TÜRKİYE", "TR" }, { "TURKIYE", "TR" },
        { "GERMANY", "DE" }, { "DEUTSCH", "DE" }, { "DEUTSCHLAND", "DE" },
        { "FRANCE", "FR" }, { "FRENCH", "FR" },
        { "SPAIN", "ES" }, { "SPANISH", "ES" }, { "ESPANOL", "ES" }, { "ESPAÑA", "ES" },
        { "ITALY", "IT" }, { "ITALIAN", "IT" }, { "ITALIA", "IT" },
        { "PORTUGAL", "PT" }, { "PORTUGUESE", "PT" },
        { "NETHERLANDS", "NL" }, { "DUTCH", "NL" },
        { "ALBANIA", "AL" }, { "ALBANIAN", "AL" },
        { "RUSSIA", "RU" }, { "RUSSIAN", "RU" },
        { "POLAND", "PL" }, { "POLISH", "PL" },
        { "GREECE", "GR" }, { "GREEK", "GR" },
        { "SWEDEN", "SE" }, { "SWEDISH", "SE" },
        { "DENMARK", "DK" }, { "DANISH", "DK" },
        { "NORWAY", "NO" }, { "NORWEGIAN", "NO" },
        { "FINLAND", "FI" }, { "FINNISH", "FI" },
        { "ARABIC", "AR" }, { "ARAB", "AR" },
        { "ENGLISH", "EN" }, { "UNITED KINGDOM", "UK" }, { "UNITED STATES", "US" }
    };

    private static string? ExtractCountryCodeFromPrefix(string prefix)
    {
        var delimiters = new[] { ' ', '-', '_', '/', '|', '+', '&', '[', ']', '(', ')' };
        var parts = prefix.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            if (KnownCountryCodes.Contains(trimmedPart))
            {
                return trimmedPart.ToUpperInvariant();
            }
            if (CountryNameMap.TryGetValue(trimmedPart, out var code))
            {
                return code;
            }
        }
        return null;
    }

    private static void ProcessGroupTitleAndNameFallback(Channel channel)
    {
        if (!string.IsNullOrEmpty(channel.GroupTitle) && channel.GroupTitle != "undefined")
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(channel.Name))
        {
            return;
        }

        var name = channel.Name.Trim();

        // 1. Arabic character check
        if (ContainsArabic(name))
        {
            channel.GroupTitle = "AR";
            return;
        }

        // 2. Bracket-based prefix: [TR] Kanal D
        var bracketMatch = Regex.Match(name, @"^\[(?<group>[A-Za-z0-9_\-\s]+)\]\s*(?<rest>.+)$");
        if (bracketMatch.Success)
        {
            var rawGroup = bracketMatch.Groups["group"].Value.Trim();
            var rest = bracketMatch.Groups["rest"].Value.Trim();

            var countryCode = ExtractCountryCodeFromPrefix(rawGroup);
            channel.GroupTitle = countryCode ?? rawGroup;
            channel.Name = rest;
            return;
        }

        // 3. Delimiter-based prefix: TR: 50M2, DU-TR: 50M2, IN | Sport: Star Sports, TR; Hayat, NW: Aljazeera, R24: Vikings, AN-DU: Movie
        int delimIndex = -1;
        for (int i = 0; i < name.Length - 1; i++)
        {
            char c = name[i];
            if ((c == ':' || c == ';' || c == '|') && char.IsWhiteSpace(name[i + 1]))
            {
                delimIndex = i;
                break;
            }
        }

        if (delimIndex > 0)
        {
            var prefix = name[..delimIndex].Trim();
            var rest = name[(delimIndex + 1)..].Trim();

            var countryCode = ExtractCountryCodeFromPrefix(prefix);
            if (countryCode != null)
            {
                channel.GroupTitle = countryCode;
                channel.Name = rest;
                return;
            }
            
            // Otherwise, it must be fully uppercase alphanumeric, hyphens, spaces, pipes
            if (Regex.IsMatch(prefix, @"^[A-Z0-9_\-\s\|]+$"))
            {
                channel.GroupTitle = prefix;
                channel.Name = rest;
                return;
            }
        }

        // 4. Space-separated prefix: EN The Amateur, ENl Titan
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            var firstWord = words[0];
            string? matchedCode = null;
            if (KnownCountryCodes.Contains(firstWord))
            {
                matchedCode = firstWord.ToUpperInvariant();
            }
            else if (CountryNameMap.TryGetValue(firstWord, out var mapped))
            {
                matchedCode = mapped;
            }
            else if (firstWord.Length > 2 && firstWord.EndsWith('l'))
            {
                var codePart = firstWord[..^1];
                if (KnownCountryCodes.Contains(codePart))
                {
                    matchedCode = codePart.ToUpperInvariant();
                }
                else if (CountryNameMap.TryGetValue(codePart, out var mapped2))
                {
                    matchedCode = mapped2;
                }
            }

            if (matchedCode != null)
            {
                channel.GroupTitle = matchedCode;
                channel.Name = string.Join(" ", words.Skip(1));
                return;
            }
        }

        // 5. Default fallbacks if no prefix matched
        if (channel.Type == ChannelType.VOD)
        {
            channel.GroupTitle = "Others";
        }
        else if (channel.Type == ChannelType.Series)
        {
            channel.GroupTitle = "Others";
        }
        else
        {
            channel.GroupTitle = "Others";
        }
    }

    private static bool ContainsArabic(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Any(c => (c >= 0x0600 && c <= 0x06FF) || 
                             (c >= 0x0750 && c <= 0x077F) || 
                             (c >= 0x08A0 && c <= 0x08FF) || 
                             (c >= 0xFB50 && c <= 0xFDFF) || 
                             (c >= 0xFE70 && c <= 0xFEFF));
    }

    private List<Channel>? _lastPartialChannels;
}