using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Carries Stalker category/genre data.
/// This is an in-memory transport object and is not persisted directly.
/// </summary>
public class StalkerCategory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "itv" | "vod" | "series"
    public int? Count { get; set; }
}

public interface IStalkerPortalService
{
    /// <summary>
    /// Authenticates a Stalker portal session with the supplied MAC address.
    /// </summary>
    Task<bool> AuthenticateAsync(
        string portalUrl,
        string macAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy API kept for backward compatibility.
    /// Loads every channel in one pass and can be slow for very large accounts.
    /// Prefer GetChannelsProgressiveAsync for new code.
    /// </summary>
    Task<List<Channel>> GetChannelsAsync(
        string portalUrl,
        string macAddress,
        bool includeVod = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the category list quickly so the UI can open before all content is loaded.
    /// </summary>
    Task<List<StalkerCategory>> GetCategoriesAsync(
        string portalUrl,
        string macAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads channels for a specific category. Used by lazy loading when a category is opened.
    /// </summary>
    Task<List<Channel>> GetChannelsByCategoryAsync(
        string portalUrl,
        string macAddress,
        string categoryId,
        string categoryType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads content progressively by category.
    /// onCategoriesDiscovered is called after categories are available so the UI can open immediately.
    /// The Action&lt;string&gt; parameter prioritizes a category selected by the user.
    /// The returned list should contain the categories that still need loading.
    /// onCategoryLoaded is called after each category completes.
    /// </summary>
    Task GetChannelsProgressiveAsync(
        string portalUrl,
        string macAddress,
        bool includeVod,
        Func<List<StalkerCategory>, Action<string>, Task<List<StalkerCategory>>> onCategoriesDiscovered,
        Func<List<Channel>, StalkerCategory, Task> onCategoryLoaded,
        IProgress<StalkerLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a likely XMLTV/EPG URL for a Stalker portal.
    /// </summary>
    string GetEpgUrl(string portalUrl);

    /// <summary>
    /// Loads seasons, episodes, and metadata for a Stalker series.
    /// </summary>
    Task<StalkerSeriesInfo?> GetSeriesInfoAsync(
        string portalUrl,
        string macAddress,
        string seriesId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a playable manifest/video URL for a Stalker VOD item or series episode.
    /// </summary>
    Task<string?> CreateLinkAsync(
        string portalUrl,
        string macAddress,
        string type,
        string cmd,
        string episodeNum = "0",
        CancellationToken cancellationToken = default);
}

public class StalkerSeriesInfo
{
    public List<StalkerSeasonInfo> Seasons { get; set; } = [];

    public string? Description { get; set; }
    public string? Director { get; set; }
    public string? Actors { get; set; }
    public string? Year { get; set; }
    public string? TmdbId { get; set; }
    public string? RatingImdb { get; set; }
    public string? Age { get; set; }
    public string? CoverUrl { get; set; }
    public string? GenresStr { get; set; }
}

public class StalkerSeasonInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Cmd { get; set; } = string.Empty;
    public List<StalkerEpisodeInfo> Episodes { get; set; } = [];
}

public class StalkerEpisodeInfo
{
    public int EpisodeNumber { get; set; }
    public string? Name { get; set; }
    public string? Cmd { get; set; }
    public string? Description { get; set; }
    public string? Pic { get; set; }
    public string? Duration { get; set; }
    public string? Added { get; set; }
}

/// <summary>
/// Progress data for progressive Stalker category loading.
/// </summary>
public class StalkerLoadProgress
{
    public int LoadedChannels { get; set; }
    public int? TotalChannels { get; set; }
    public int LoadedCategories { get; set; }
    public int TotalCategories { get; set; }
    public string CurrentCategory { get; set; } = string.Empty;
}
