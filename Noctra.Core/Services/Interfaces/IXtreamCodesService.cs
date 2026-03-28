using Noctra.Models;

namespace Noctra.Services.Interfaces;

public class XtreamCategory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "live" | "vod" | "series"
}

public interface IXtreamCodesService
{
    Task<bool> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default);

    Task<List<Channel>> GetChannelsAsync(
        string baseUrl,
        string username,
        string password,
        bool includeSeriesEpisodes = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// YENİ — Xtream kategori listesini hızlıca döndürür.
    /// </summary>
    Task<List<XtreamCategory>> GetCategoriesAsync(
        string baseUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// YENİ — Xtream aşamalı yükleme (Live, VOD ve Series ayrı ayrı yüklenir).
    /// </summary>
    Task GetChannelsProgressiveAsync(
        string baseUrl,
        string username,
        string password,
        bool includeVod,
        Func<List<XtreamCategory>, Action<string>, Task<List<XtreamCategory>>> onCategoriesDiscovered,
        Func<List<Channel>, string, Task> onCategoryLoaded,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tek bir dizinin tüm sezon ve bölümlerini çeker (get_series_info).
    /// Kullanıcı diziyi açtığında lazy load için çağrılır.
    /// </summary>
    Task<XtreamSeriesDetail?> GetSeriesInfoAsync(
        string baseUrl,
        string username,
        string password,
        long seriesId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xtream sunucusunun standart XMLTV (EPG) URL'sini döndürür.
    /// </summary>
    string GetEpgUrl(string baseUrl, string username, string password);
}

/// <summary>
/// get_series_info API yanıtının sade modeli
/// </summary>
public class XtreamSeriesDetail
{
    public string? Name { get; set; }
    public string? Cover { get; set; }
    public string? Plot { get; set; }
    public double? Rating { get; set; }
    public int? ReleaseYear { get; set; }
    public string? Genre { get; set; }
    public string? Cast { get; set; }
    public string? Director { get; set; }
    public List<XtreamSeasonDetail> Seasons { get; set; } = new();
    // episodes: { "1": [...], "2": [...] }
    public Dictionary<string, List<XtreamEpisodeDetail>> Episodes { get; set; } = new();
}

public class XtreamSeasonDetail
{
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }
    public string? Cover { get; set; }
    public string? AirDate { get; set; }
}

public class XtreamEpisodeDetail
{
    public long Id { get; set; }
    public int EpisodeNum { get; set; }
    public string? Title { get; set; }
    public string? ContainerExtension { get; set; }
    public int Season { get; set; }
    public string? Plot { get; set; }
    public string? CoverUrl { get; set; }
    public double? DurationSecs { get; set; }
    public string? AirDate { get; set; }
    public double? Rating { get; set; }
}

