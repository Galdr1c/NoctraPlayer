using System.IO;
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
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var reader = new StreamReader(stream);
                
                try
                {
                    return await ParseFromReaderAsync(reader);
                }
                catch (Exception ex) when (IsNetworkIncompletionException(ex))
                {
                    // If stream ends prematurely, return what we got so far instead of failing
                    System.Diagnostics.Debug.WriteLine($"[M3UParser] Warning: Stream ended prematurely for {url}. Returning partial results. Error: {ex.Message}");
                    // IMPORTANT: We need a way to get the partially built channels list.
                    // Let's modify ParseFromReaderAsync to fill a list passed as argument.
                    return _lastPartialChannels ?? new List<Channel>();
                }
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
            channel.TvgId = tvgIdMatch.Groups[1].Value;

        // tvg-name çıkar
        var tvgNameMatch = TvgNameRegex().Match(line);
        if (tvgNameMatch.Success)
            channel.TvgName = tvgNameMatch.Groups[1].Value;

        // tvg-logo çıkar
        var tvgLogoMatch = TvgLogoRegex().Match(line);
        if (tvgLogoMatch.Success)
            channel.LogoUrl = tvgLogoMatch.Groups[1].Value;

        // group-title çıkar
        var groupMatch = GroupTitleRegex().Match(line);
        if (groupMatch.Success)
            channel.GroupTitle = groupMatch.Groups[1].Value;

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

        // 1. URL Pattern Analizi (Kesin Belirteçler)
        if (lowerUrl.Contains("/series/") || lowerUrl.Contains("/tv_show/") || lowerUrl.Contains("type=series"))
            return ChannelType.Series;

        if (lowerUrl.Contains("/movie/") || lowerUrl.Contains("/vod/") || lowerUrl.Contains("type=vod") || lowerUrl.Contains("type=movie"))
            return ChannelType.VOD;

        if (lowerUrl.Contains("/live/") || lowerUrl.Contains("type=live"))
            return ChannelType.Live;

        // 2. Başlık ve İsim Analizi (Regex + Keywords)
        // Dizi: S01E01, 1x01, Sezon 1, Bölüm 1
        if (SeriesInfoParser.IsSeries(name) || 
            lowerName.Contains("bolum") ||
            lowerName.Contains("episode"))
        {
            return ChannelType.Series;
        }

        // Film: Yıl (1990-2030), Çözünürlük ve Kaynak (BluRay vs)
        // Not: Live TV kanallarında da bazen 1080p yazabilir, o yüzden diğer sinyallerle birleştirmek gerekebilir.
        // Ancak genellikle VOD isimlendirmesi "Film Adı (2023) 1080p" şeklindedir.
        if (VodPatternYear().IsMatch(name) && 
            (lowerName.Contains("bluray") || lowerName.Contains("web-dl") || lowerName.Contains("webrip") || lowerName.Contains("dvdrip")))
        {
            return ChannelType.VOD;
        }

        // 3. Grup Başlığı Analizi (En Güçlü İkinci Sinyal)
        if (lowerGroup.Contains("series") || 
            lowerGroup.Contains("dizi") || 
            lowerGroup.Contains("tv show") || 
            lowerGroup.Contains("belgesel serisi")) // Belgesel serileri de dizi mantığında olabilir
        {
            return ChannelType.Series;
        }

        if (lowerGroup.Contains("movie") || 
            lowerGroup.Contains("film") || 
            lowerGroup.Contains("vod") || 
            lowerGroup.Contains("cinema") || 
            lowerGroup.Contains("sinema") ||
            lowerGroup.Contains("yerli film") ||
            lowerGroup.Contains("yabanci film"))
        {
            return ChannelType.VOD;
        }

        if (lowerGroup.Contains("live") || 
            lowerGroup.Contains("canli") || 
            lowerGroup.Contains("tv") ||
            lowerGroup.Contains("ulusal") ||
            lowerGroup.Contains("spor") ||
            lowerGroup.Contains("belgesel")) // Tekil belgesel kanalları Live kabul edilir
        {
            return ChannelType.Live;
        }

        // 4. Uzantı ve Diğer Karakteristikler (Son Çare)
        if (lowerUrl.EndsWith(".mp4") || lowerUrl.EndsWith(".mkv") || lowerUrl.EndsWith(".avi") || lowerUrl.EndsWith(".mov"))
        {
            // Uzantı video dosyası ise ve Live/Noctra sinyali yoksa VOD varsay
            return ChannelType.VOD;
        }
        
        if (lowerUrl.EndsWith(".m3u8") || lowerUrl.EndsWith(".ts"))
        {
            // Genellikle stream, ama VOD da olabilir. Varsayılan Live.
            return ChannelType.Live;
        }

        // Varsayılan
        return ChannelType.Live;
    }

    [GeneratedRegex(@"\((19|20)\d{2}\)", RegexOptions.IgnoreCase)]
    private static partial Regex VodPatternYear(); // (1990) - (2099) arası yıllar

    // Regex pattern'ları (Lenient versions)
    [GeneratedRegex(@"tvg-id\s*=\s*""?([^""\s,]*)""?", RegexOptions.IgnoreCase)]
    private static partial Regex TvgIdRegex();

    [GeneratedRegex(@"tvg-name\s*=\s*""?([^""\s,]*)""?", RegexOptions.IgnoreCase)]
    private static partial Regex TvgNameRegex();

    [GeneratedRegex(@"tvg-logo\s*=\s*""?([^""\s,]*)""?", RegexOptions.IgnoreCase)]
    private static partial Regex TvgLogoRegex();

    [GeneratedRegex(@"group-title\s*=\s*""?([^""]*)""?", RegexOptions.IgnoreCase)]
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


