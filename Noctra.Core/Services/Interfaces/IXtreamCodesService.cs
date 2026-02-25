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
}

