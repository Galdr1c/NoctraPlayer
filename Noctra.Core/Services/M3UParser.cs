using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<Channel> ParseFromFileStreamAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("M3U file was not found.", filePath);
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);

        await foreach (var channel in ParseFromReaderStreamAsync(reader, cancellationToken))
        {
            yield return channel;
        }
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

    public async IAsyncEnumerable<Channel> ParseFromUrlStreamAsync(
        string url,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

        var yielded = 0;
        await foreach (var channel in DownloadAndParseStreamInternalAsync(
                           url,
                           overrideUserAgent: null,
                           allowNotFound: true,
                           timeoutCts.Token))
        {
            yielded++;
            yield return channel;
        }

        if (yielded > 0 || string.IsNullOrWhiteSpace(url))
        {
            yield break;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[M3UParser] 0 channels or 404 for {url}. Retrying with VLC User-Agent...");

        await foreach (var channel in DownloadAndParseStreamInternalAsync(
                           url,
                           "VLC/3.0.18",
                           allowNotFound: false,
                           timeoutCts.Token))
        {
            yield return channel;
        }
    }

    private async IAsyncEnumerable<Channel> DownloadAndParseStreamInternalAsync(
        string url,
        string? overrideUserAgent,
        bool allowNotFound,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(overrideUserAgent))
        {
            request.Headers.UserAgent.Clear();
            request.Headers.TryAddWithoutValidation("User-Agent", overrideUserAgent);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            yield break;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        await foreach (var channel in ParseFromReaderStreamAsync(reader, cancellationToken))
        {
            yield return channel;
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
        _lastPartialChannels = channels;

        await foreach (var channel in ParseFromReaderStreamAsync(reader))
        {
            channels.Add(channel);
        }

        return channels;
    }

    private async IAsyncEnumerable<Channel> ParseFromReaderStreamAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastDetectedEpgUrl = null;

        string? firstLine = null;
        while ((firstLine = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (!string.IsNullOrWhiteSpace(firstLine))
                break;
        }

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            yield break;
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
        var pendingPlaybackHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line = hasHeader ? await reader.ReadLineAsync(cancellationToken) : firstLine;
        
        while (line != null)
        {
            line = line.Trim();

            if (!string.IsNullOrEmpty(line))
            {
                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    currentChannel = ParseExtInf(line);
                    pendingPlaybackHeaders.Clear();
                }
                else if (currentChannel != null && TryCollectPlaybackHeader(line, pendingPlaybackHeaders))
                {
                    // Header line belongs to the current #EXTINF item. It will be encoded into the StreamUrl
                    // as Kodi-style inline options so mobile and desktop player services can apply it later.
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
                    currentChannel.StreamUrl = AppendPlaybackHeaders(line, pendingPlaybackHeaders);
                    currentChannel.Type = DetectChannelType(line, currentChannel.Name, currentChannel.GroupTitle);
                    ProcessGroupTitleAndNameFallback(currentChannel);
                    yield return currentChannel;
                    currentChannel = null;
                    pendingPlaybackHeaders.Clear();
                }
            }

            line = await reader.ReadLineAsync(cancellationToken);
        }
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

    private static bool TryCollectPlaybackHeader(string line, Dictionary<string, string> headers)
    {
        if (TryCollectExtVlcOptHeader(line, headers))
        {
            return true;
        }

        if (TryCollectKodiPropertyHeader(line, headers))
        {
            return true;
        }

        return false;
    }

    private static bool TryCollectExtVlcOptHeader(string line, Dictionary<string, string> headers)
    {
        const string prefix = "#EXTVLCOPT:";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var option = line[prefix.Length..].Trim();
        var equalsIndex = option.IndexOf('=');
        if (equalsIndex <= 0 || equalsIndex >= option.Length - 1)
        {
            return true;
        }

        var optionName = option[..equalsIndex].Trim();
        var value = option[(equalsIndex + 1)..].Trim().Trim('"');
        AddNormalizedPlaybackHeader(headers, optionName, value);
        return true;
    }

    private static bool TryCollectKodiPropertyHeader(string line, Dictionary<string, string> headers)
    {
        const string prefix = "#KODIPROP:";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var property = line[prefix.Length..].Trim();
        var equalsIndex = property.IndexOf('=');
        if (equalsIndex <= 0 || equalsIndex >= property.Length - 1)
        {
            return true;
        }

        var propertyName = property[..equalsIndex].Trim();
        var value = property[(equalsIndex + 1)..].Trim().Trim('"');

        if (propertyName.Equals("inputstream.adaptive.stream_headers", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Equals("inputstream.ffmpegdirect.stream_headers", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var part in value.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var partEqualsIndex = part.IndexOf('=');
                if (partEqualsIndex <= 0 || partEqualsIndex >= part.Length - 1)
                {
                    continue;
                }

                var headerName = WebUtility.UrlDecode(part[..partEqualsIndex]).Trim();
                var headerValue = WebUtility.UrlDecode(part[(partEqualsIndex + 1)..]).Trim();
                AddNormalizedPlaybackHeader(headers, headerName, headerValue);
            }
        }

        return true;
    }

    private static void AddNormalizedPlaybackHeader(Dictionary<string, string> headers, string rawName, string value)
    {
        if (string.IsNullOrWhiteSpace(rawName) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var headerName = rawName.Trim().ToLowerInvariant() switch
        {
            "ua" => "User-Agent",
            "useragent" => "User-Agent",
            "user-agent" => "User-Agent",
            "http-user-agent" => "User-Agent",
            "referer" => "Referer",
            "referrer" => "Referer",
            "http-referrer" => "Referer",
            "http-referer" => "Referer",
            "origin" => "Origin",
            "http-origin" => "Origin",
            "cookie" => "Cookie",
            "http-cookie" => "Cookie",
            "authorization" => "Authorization",
            "x-user-agent" => "X-User-Agent",
            _ => rawName.Trim()
        };

        headers[headerName] = value.Trim();
    }

    private static string AppendPlaybackHeaders(string streamUrl, Dictionary<string, string> headers)
    {
        if (headers.Count == 0)
        {
            return streamUrl;
        }

        var encodedHeaders = string.Join("&", headers.Select(header =>
            $"{WebUtility.UrlEncode(header.Key)}={WebUtility.UrlEncode(header.Value)}"));

        if (string.IsNullOrWhiteSpace(encodedHeaders))
        {
            return streamUrl;
        }

        return streamUrl.Contains('|', StringComparison.Ordinal)
            ? $"{streamUrl}&{encodedHeaders}"
            : $"{streamUrl}|{encodedHeaders}";
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
        var isAdult = GroupTitleSuggestsAdult(groupTitle);
        var looksLikeSeries = SeriesInfoParser.IsSeries(name) && !LooksLikeBareNumericChannelName(name);

        if (lowerUrl.Contains("/serie/") || lowerUrl.Contains("/series/") || lowerUrl.Contains("/dizi/") || lowerUrl.Contains("/diziler/") || lowerUrl.Contains("/tv_show/") || lowerUrl.Contains("/tv_shows/") || lowerUrl.Contains("type=series"))
            return ChannelType.Series;

        if (lowerUrl.Contains("/movie/") || lowerUrl.Contains("/movies/") || lowerUrl.Contains("/film/") || lowerUrl.Contains("/filmler/") || lowerUrl.Contains("/vod/") || lowerUrl.Contains("type=movie") || lowerUrl.Contains("type=vod"))
            return ChannelType.VOD;

        if (lowerUrl.Contains("/live/") || lowerUrl.Contains("/livetv/") || lowerUrl.Contains("/tv/") || lowerUrl.Contains("type=live"))
            return ChannelType.Live;

        if (HasStrongLinearStreamSignal(lowerUrl))
            return ChannelType.Live;

        if (GroupTitleSuggestsVod(groupTitle) && !HasStrongLinearStreamSignal(lowerUrl))
            return ChannelType.VOD;

        if (!isAdult && (looksLikeSeries || (GroupTitleSuggestsSeries(groupTitle) && LooksLikeEpisodicName(name))))
            return ChannelType.Series;

        if (IsLinearStreamUrl(lowerUrl))
            return ChannelType.Live;

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

    private static bool HasStrongLinearStreamSignal(string lowerUrl)
    {
        var path = lowerUrl;
        var q = path.IndexOf('?');
        if (q >= 0)
            path = path[..q];

        return path.EndsWith(".m3u8") ||
               path.EndsWith("/m3u8") ||
               path.EndsWith(".ts") ||
               path.EndsWith("/ts") ||
               path.EndsWith(".m3u") ||
               path.EndsWith("/m3u") ||
               lowerUrl.Contains("format=m3u8") ||
               lowerUrl.Contains("extension=m3u8") ||
               lowerUrl.Contains("extension=ts");
    }

    private static bool GroupTitleSuggestsVod(string? groupTitle)
    {
        if (string.IsNullOrWhiteSpace(groupTitle))
        {
            return false;
        }

        var normalized = groupTitle.Trim().ToLowerInvariant();
        return normalized.Contains("movie") ||
               normalized.Contains("movies") ||
               normalized.Contains("film") ||
               normalized.Contains("filmes") ||
               normalized.Contains("cinema") ||
               normalized.Contains("animação") ||
               normalized.Contains("animacao");
    }

    private static bool GroupTitleSuggestsSeries(string? groupTitle)
    {
        if (string.IsNullOrWhiteSpace(groupTitle))
        {
            return false;
        }

        var normalized = groupTitle.Trim().ToLowerInvariant();
        return normalized.Contains("series") ||
               normalized.Contains("serie") ||
               normalized.Contains("dizi") ||
               normalized.Contains("drama") ||
               normalized.Contains("dramas");
    }

    private static bool GroupTitleSuggestsAdult(string? groupTitle)
        => AdultCategoryClassifier.IsAdultCategory(groupTitle);

    private static bool LooksLikeEpisodicName(string name)
    {
        return Regex.IsMatch(name, @"\b(?:episode|ep|part)\s*\d{1,3}\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(name, @"\bseason\s*\d{1,2}.*?\b(?:episode|ep|part)\s*\d{1,3}\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(name, @"\blast\s+episode\b", RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeBareNumericChannelName(string name)
    {
        var candidate = ExtractNameAfterKnownPrefix(name) ?? name;
        return Regex.IsMatch(candidate.Trim(), @"^\d{1,3}\s*x\s*\d{1,3}$", RegexOptions.IgnoreCase);
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
        "TR", "EN", "UK", "GB", "US", "EU", "CA", "AU", "DE", "FR", "ES", "IT", "PT", "NL", "AL", "CL", "AR", "RU", "PL", "GR", "SE", "DK", "IN",
        "NO", "FI", "BE", "CH", "AT", "IE", "RO", "BG", "HU", "CZ", "SK", "HR", "SI", "RS", "BA", "MK", "ME", "UA", "BY", "MD", "BR", "MX",
        "CO", "PE", "VE", "EC", "GT", "CU", "BO", "DO", "HN", "PY", "SV", "CR", "UY", "PA", "NI", "PR", "ZA", "NG", "KE", "GH", "EG", "MA",
        "DZ", "TN", "LY", "SY", "IQ", "JO", "LB", "YE", "OM", "QA", "KW", "AE", "SA", "PK", "BD", "AF", "IR", "IL", "CN", "JP", "KR", "VN",
        "TH", "ID", "MY", "PH", "SG", "WO",

        // 3-Letter Codes
        "TUR", "ENG", "USA", "GBR", "RSA", "CAN", "AUS", "GER", "FRA", "ESP", "ITA", "POR", "NLD", "ALB", "POL", "GRE", "SWE", "DNK", "NOR", "FIN",
        "BEL", "CHE", "AUT", "ROU", "BGR", "HUN", "CZE", "SVK", "HRV", "SRB", "UKR", "RUS", "ARA", "IND", "PAK", "BRA", "MEX", "ARG", "COL",
        "PER", "CHL", "VEN", "NGA", "ZAF"
    };

    private static readonly Dictionary<string, string> CountryNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "TURKEY", "TR" }, { "TÜRKİYE", "TR" }, { "TURKIYE", "TR" },
        { "GERMANY", "DE" }, { "DEUTSCH", "DE" }, { "DEUTSCHLAND", "DE" },
        { "FRANCE", "FR" }, { "FRENCH", "FR" },
        { "SPAIN", "ES" }, { "SPANISH", "ES" }, { "ESPANOL", "ES" }, { "ESPAÑA", "ES" }, { "SP", "ES" },
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
        { "ENGLISH", "EN" }, { "UNITED KINGDOM", "GB" }, { "UNITED STATES", "US" }
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

    private static string? ExtractNameAfterKnownPrefix(string name)
    {
        var delimiterIndex = name.IndexOf(':');
        if (delimiterIndex <= 0 || delimiterIndex >= name.Length - 1)
        {
            return null;
        }

        var prefix = name[..delimiterIndex].Trim();
        if (ExtractCountryCodeFromPrefix(prefix) != null || Regex.IsMatch(prefix, @"^[A-Z0-9_\-\s\|]+$"))
        {
            return name[(delimiterIndex + 1)..].Trim();
        }

        return null;
    }

    private static bool IsQualityOnlyPrefix(string prefix)
    {
        return Regex.IsMatch(prefix.Trim(), @"^(?:4k|uhd|2160p|1080p|720p|576p|480p|fhd|hd|sd|hevc|raw|h265|h\.?265|x265)$", RegexOptions.IgnoreCase);
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

        // 1. Pipe country prefix: |GB| Sky Sports, |GB Free Sports
        var pipeCountryMatch = Regex.Match(name, @"^\|(?<country>[A-Za-z]{2,3})(?:\||\s+)(?<rest>.+)$");
        if (pipeCountryMatch.Success)
        {
            var rawCountry = pipeCountryMatch.Groups["country"].Value.Trim();
            var rest = pipeCountryMatch.Groups["rest"].Value.Trim();
            var countryCode = ExtractCountryCodeFromPrefix(rawCountry);
            if (countryCode != null && !string.IsNullOrWhiteSpace(rest))
            {
                channel.Country = countryCode;
                channel.GroupTitle = BuildSmartGroupTitle(channel.Type, rest, countryCode);
                return;
            }
        }

        // 2. Bracket-based prefix: [TR] Kanal D
        var bracketMatch = Regex.Match(name, @"^\[(?<group>[A-Za-z0-9_\-\s]+)\]\s*(?<rest>.+)$");
        if (bracketMatch.Success)
        {
            var rawGroup = bracketMatch.Groups["group"].Value.Trim();
            var rest = bracketMatch.Groups["rest"].Value.Trim();

            var countryCode = ExtractCountryCodeFromPrefix(rawGroup);
            if (countryCode != null)
            {
                channel.Country = countryCode;
                channel.GroupTitle = BuildSmartGroupTitle(channel.Type, rest, countryCode);
                return;
            }
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

            if (IsQualityOnlyPrefix(prefix))
            {
                channel.GroupTitle = BuildSmartGroupTitle(channel.Type, rest, null);
                return;
            }

            var countryCode = ExtractCountryCodeFromPrefix(prefix);
            if (countryCode != null)
            {
                channel.GroupTitle = BuildSmartGroupTitle(channel.Type, rest, countryCode);
                channel.Country = countryCode;
                return;
            }
            
            // Unknown prefixes are often show names, brand names, or provider markers
            // (AN:, KD:, R24:, Sky Sports:). They are not stable playlist groups.
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
                var rest = string.Join(" ", words.Skip(1));
                channel.GroupTitle = BuildSmartGroupTitle(channel.Type, rest, matchedCode);
                channel.Country = matchedCode;
                return;
            }
        }

        if (ContainsArabic(name) && channel.Type != ChannelType.Series)
        {
            channel.GroupTitle = "AR";
            return;
        }

        channel.GroupTitle = BuildSmartGroupTitle(channel.Type, name, null);
    }

    private static string BuildSmartGroupTitle(ChannelType type, string name, string? countryCode)
    {
        var typePrefix = type switch
        {
            ChannelType.Series => "Series",
            ChannelType.VOD => "Movies",
            _ => "Live"
        };

        if (type == ChannelType.Series)
            return "Series / Others";

        if (type == ChannelType.Live)
        {
            if (!string.IsNullOrWhiteSpace(countryCode))
            {
                return $"Live / {countryCode}";
            }
        }

        return $"{typePrefix} / Others";
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
