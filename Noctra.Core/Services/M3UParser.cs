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
    /// Kanal türünü URL ve grup bilgisinden tespit eder
    /// </summary>
    private static ChannelType DetectChannelType(string url, string name, string? groupTitle)
    {
        var lowerUrl = url.ToLowerInvariant();
        var lowerGroup = groupTitle?.ToLowerInvariant() ?? "";
        var lowerName = name.ToLowerInvariant();

        // 1. URL Pattern Analizi (EN GÜÇLÜ SİNYAL)
        // Eğer URL açıkça /live/ veya pluto.tv içeriyorsa, bu bir canlı kanaldır (7/24 döngü olsa bile)
        if (lowerUrl.Contains("/live/") || lowerUrl.Contains("type=live") || lowerUrl.Contains("/radio/") || lowerUrl.Contains("pluto.tv"))
            return ChannelType.Live;

        if (lowerUrl.Contains("/series/") || lowerUrl.Contains("/tv_show/") || lowerUrl.Contains("type=series"))
            return ChannelType.Series;

        if (lowerUrl.Contains("/movie/") || lowerUrl.Contains("/vod/") || lowerUrl.Contains("type=vod") || lowerUrl.Contains("type=movie"))
            return ChannelType.VOD;

        // 2. KESİN CANLI / DÖNGÜ KONTROLÜ (Grup ve İsim bazlı)
        // Eğer grupta veya isimde 7/24, Canlı, Spor belirtileri varsa VOD/Series kontrollerinden ÖNCE ele alalım.
        if (SeriesInfoParser.IsLiveSeries(name) || SeriesInfoParser.IsLiveSeries(groupTitle) ||
            lowerGroup.Contains("spor") || lowerGroup.Contains("sport") || 
            lowerGroup.Contains("7/24") || lowerGroup.Contains("24/7") ||
            lowerName.Contains("7/24") || lowerName.Contains("24/7"))
        {
            return ChannelType.Live;
        }

        // 3. Grup Başlığı Analizi
        // Önce Radio (Canlı) kontrolü
        if (lowerGroup.Contains("radio") || lowerName.Contains(" radio"))
        {
            return ChannelType.Live;
        }

        // Önce Series (Dizi) kontrolü
        if (lowerGroup.Contains("series") || 
            lowerGroup.Contains("dizi") || 
            lowerGroup.Contains("tv show") || 
            lowerGroup.Contains("belgesel serisi") ||
            lowerGroup.EndsWith(" diz") || 
            lowerGroup.Contains(" diz ") ||
            lowerGroup.Contains("staffel") ||
            lowerGroup.Contains("saison"))
        {
            return ChannelType.Series;
        }

        // Sonra VOD (Film) kontrolü
        if (lowerGroup.Contains("movie") || 
            lowerGroup.Contains("film") || 
            lowerGroup.Contains("vod") || 
            lowerGroup.Contains("cinema") || 
            lowerGroup.Contains("sinema") ||
            lowerGroup.Contains("yerli film") ||
            lowerGroup.Contains("yabanci film") ||
            lowerGroup.Contains("netflix") ||
            lowerGroup.Contains("disney") ||
            lowerGroup.Contains("amazon") ||
            lowerGroup.Contains("hulu") ||
            lowerGroup.Contains("apple tv") ||
            lowerGroup.Contains("blutv") ||
            lowerGroup.Contains("gain") ||
            lowerGroup.Contains("exxen") ||
            lowerGroup.Contains("sinevizyon") ||
            lowerGroup.Contains("kino"))
        {
            return ChannelType.VOD;
        }

        // 4. Başlık ve İsim Analizi
        // Dizi: S01E01, 1x01, Sezon 1, Bölüm 1
        if (SeriesInfoParser.IsSeries(name) ||
            lowerName.Contains("bolum") ||
            lowerName.Contains("episode"))
        {
            return ChannelType.Series;
        }

        // Film: Yıl (1990-2030)
        if (VodPatternYear().IsMatch(name))
        {
            return ChannelType.VOD;
        }

        // 5. Uzantı ve Diğer Karakteristikler
        if (lowerUrl.EndsWith(".mp4") || lowerUrl.EndsWith(".mkv") || lowerUrl.EndsWith(".avi") || lowerUrl.EndsWith(".mov"))
        {
            return ChannelType.VOD;
        }

        if (lowerUrl.EndsWith(".m3u8") || lowerUrl.EndsWith(".ts"))
        {
            return ChannelType.Live;
        }

        // Varsayılan
        return ChannelType.Live;
    }
    [GeneratedRegex(@"(?:\b|\()((?:19|20)\d{2})(?:\b|\))", RegexOptions.IgnoreCase)]
    private static partial Regex VodPatternYear(); // (1990) veya 1990 gibi yılları yakalar

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

    private List<Channel>? _lastPartialChannels;
}


