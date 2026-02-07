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
                currentChannel.Type = DetectChannelType(line, currentChannel.GroupTitle);
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
            return await ParseAsync(content);
        }
        catch (TaskCanceledException ex)
        {
            throw new TimeoutException($"M3U indirme işlemi zaman aşımına uğradı (30sn): {url}", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"M3U URL'sine erişilemedi: {url}", ex);
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
    private static ChannelType DetectChannelType(string url, string? groupTitle)
    {
        var lowerUrl = url.ToLower();
        var lowerGroup = groupTitle?.ToLower() ?? "";

        // VOD tespiti
        if (lowerUrl.Contains("/movie/") || 
            lowerUrl.Contains("/vod/") ||
            lowerGroup.Contains("movie") ||
            lowerGroup.Contains("film") ||
            lowerUrl.EndsWith(".mp4") ||
            lowerUrl.EndsWith(".mkv"))
        {
            return ChannelType.VOD;
        }

        // Series tespiti
        if (lowerUrl.Contains("/series/") ||
            lowerGroup.Contains("series") ||
            lowerGroup.Contains("dizi"))
        {
            return ChannelType.Series;
        }

        // Varsayılan olarak Live
        return ChannelType.Live;
    }

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
