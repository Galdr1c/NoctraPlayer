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
        
        // Safely set User-Agent to avoid issues with shared HttpClient instances
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }
        
        _httpClient.Timeout = TimeSpan.FromSeconds(30); // Global timeout
    }

    public Task<List<Channel>> ParseAsync(string content)
    {
        var channels = new List<Channel>();
        var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        LastDetectedEpgUrl = null;

        if (lines.Length == 0) return Task.FromResult(channels);

        // M3U header kontrolü
        var firstLine = lines[0].Trim();
        if (!firstLine.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Geçersiz M3U formatı: #EXTM3U header bulunamadı");
        }

        var xTvgUrlMatch = XTvgUrlRegex().Match(firstLine);
        if (xTvgUrlMatch.Success)
        {
            LastDetectedEpgUrl = ExtractFirstEpgUrl(xTvgUrlMatch.Groups[1].Value);
        }

        Channel? currentChannel = null;

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrEmpty(line)) continue;

            // #EXTINF satırı - kanal bilgileri
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                currentChannel = ParseExtInf(line);
            }
            // URL satırı
            else if (!line.StartsWith("#") && currentChannel != null)
            {
                currentChannel.StreamUrl = line;
                currentChannel.Type = DetectChannelType(line, currentChannel.Name, currentChannel.GroupTitle);
                channels.Add(currentChannel);
                currentChannel = null;
            }
        }

        return Task.FromResult(channels);
    }

    public async Task<List<Channel>> ParseFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("M3U dosyası bulunamadı", filePath);
        }

        var content = await File.ReadAllTextAsync(filePath);
        return await ParseAsync(content);
    }

    public async Task<List<Channel>> ParseFromUrlAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var content = await NetworkRetry.ExecuteAsync(
                () => _httpClient.GetStringAsync(url, cts.Token),
                cancellationToken: cts.Token);
            
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("M3U dosyası boş.");
            
            return await ParseAsync(content);
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

        // Kanal adını çıkar (son virgülden sonrası)
        var nameMatch = ChannelNameRegex().Match(line);
        if (nameMatch.Success)
            channel.Name = nameMatch.Groups[1].Value.Trim();
        else
            channel.Name = channel.TvgName ?? "Bilinmeyen Kanal";

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
        if (SeriesPattern1().IsMatch(name) || 
            SeriesPattern2().IsMatch(name) ||
            SeriesPattern3().IsMatch(name) || 
            lowerName.Contains("sezon") || 
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

    [GeneratedRegex(@"S(\d{1,2})E(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPattern1();

    [GeneratedRegex(@"(\d{1,2})x(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPattern2();
    
    [GeneratedRegex(@"S(\d{1,2})\s*-\s*E(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPattern3(); // S01 - E01 formatı

    [GeneratedRegex(@"\((19|20)\d{2}\)", RegexOptions.IgnoreCase)]
    private static partial Regex VodPatternYear(); // (1990) - (2099) arası yıllar

    // Regex pattern'ları
    [GeneratedRegex(@"tvg-id=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex TvgIdRegex();

    [GeneratedRegex(@"tvg-name=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex TvgNameRegex();

    [GeneratedRegex(@"tvg-logo=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex TvgLogoRegex();

    [GeneratedRegex(@"group-title=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex GroupTitleRegex();

    [GeneratedRegex(@",\s*(.+)$")]
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
}


