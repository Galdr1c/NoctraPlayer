using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

/// <summary>
/// M3U/M3U8 dosyalarını parse eden servis
/// </summary>
public partial class M3UParser : IM3UParser
{
    private readonly HttpClient _httpClient;

    public M3UParser(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30); // Global timeout
    }

    public async Task<List<Channel>> ParseAsync(string content)
    {
        var channels = new List<Channel>();
        var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0) return channels;

        // M3U header kontrolü
        var firstLine = lines[0].Trim();
        if (!firstLine.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Geçersiz M3U formatı: #EXTM3U header bulunamadı");
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

        return channels;
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
            var content = await _httpClient.GetStringAsync(url, cts.Token);
            
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
        var lowerUrl = url.ToLower();
        var lowerGroup = groupTitle?.ToLower() ?? "";
        var lowerName = name.ToLower();

        // Series tespiti (Regex ile S01E01 veya 1x01 ara)
        if (SeriesPattern1().IsMatch(name) || 
            SeriesPattern2().IsMatch(name) ||
            lowerUrl.Contains("/series/") ||
            lowerGroup.Contains("series") ||
            lowerGroup.Contains("dizi"))
        {
            return ChannelType.Series;
        }

        // VOD tespiti
        if (lowerUrl.Contains("/movie/") || 
            lowerUrl.Contains("/vod/") ||
            lowerGroup.Contains("movie") ||
            lowerGroup.Contains("film") ||
            lowerUrl.EndsWith(".mp4") ||
            lowerUrl.EndsWith(".mkv") ||
            lowerUrl.EndsWith(".avi"))
        {
            return ChannelType.VOD;
        }

        // Varsayılan olarak Live
        return ChannelType.Live;
    }

    [GeneratedRegex(@"S(\d{1,2})E(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPattern1();

    [GeneratedRegex(@"(\d{1,2})x(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPattern2();

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
}
