using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Noctra.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Noctra.Services;

public class XtreamCodesService : IXtreamCodesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly ILocalizationService _localizationService;
    private static readonly ConcurrentDictionary<string, CachedAuthState> AuthCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan SuccessAuthTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailedAuthTtl = TimeSpan.FromSeconds(30);
    private static DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;
    private static readonly object CleanupLock = new();

    public XtreamCodesService(HttpClient httpClient, ILocalizationService localizationService)
    {
        _httpClient = httpClient;
        _localizationService = localizationService;
    }

    public async Task<bool> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(baseUrl, username, password, action: null);
        var payload = await GetJsonAsync<XtreamAuthPayload>(url, cancellationToken);
        return string.Equals(payload?.UserInfo?.Status, "Active", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<XtreamCategory>> GetCategoriesAsync(
        string baseUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        var authenticated = await EnsureAuthenticatedAsync(normalizedBaseUrl, username, password, cancellationToken);
        if (!authenticated) return [];

        var liveCategoriesTask = GetJsonAsync<List<XtreamCategoryDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_live_categories"), cancellationToken);
        var vodCategoriesTask = GetJsonAsync<List<XtreamCategoryDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_vod_categories"), cancellationToken);
        var seriesCategoriesTask = GetJsonAsync<List<XtreamCategoryDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_series_categories"), cancellationToken);

        await Task.WhenAll(liveCategoriesTask, vodCategoriesTask, seriesCategoriesTask);

        var result = new List<XtreamCategory>();
        
        if (liveCategoriesTask.Result != null)
            result.AddRange(liveCategoriesTask.Result.Select(c => new XtreamCategory { Id = c.CategoryId ?? "", Name = c.CategoryName ?? "Live", Type = "live" }));
        
        if (vodCategoriesTask.Result != null)
            result.AddRange(vodCategoriesTask.Result.Select(c => new XtreamCategory { Id = c.CategoryId ?? "", Name = c.CategoryName ?? "vod", Type = "vod" }));
            
        if (seriesCategoriesTask.Result != null)
            result.AddRange(seriesCategoriesTask.Result.Select(c => new XtreamCategory { Id = c.CategoryId ?? "", Name = c.CategoryName ?? "series", Type = "series" }));

        return result;
    }

    public async Task GetChannelsProgressiveAsync(
        string baseUrl,
        string username,
        string password,
        bool includeVod,
        Func<List<XtreamCategory>, Action<string>, Task<List<XtreamCategory>>> onCategoriesDiscovered,
        Func<List<Channel>, string, Task> onCategoryLoaded,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        var authenticated = await EnsureAuthenticatedAsync(normalizedBaseUrl, username, password, cancellationToken);

        if (!authenticated)
        {
            throw new InvalidOperationException(_localizationService.GetString("Xtream.Error.AuthFailed"));
        }

        // 1. Kategorileri Çek
        var allCategories = await GetCategoriesAsync(normalizedBaseUrl, username, password, cancellationToken);
        
        string? prioritizedCategory = null;
        
        // Önemli: Discover Callback - UI'ın dolması için
        var categoriesToLoad = await onCategoriesDiscovered(allCategories, (catName) => 
        {
            prioritizedCategory = catName;
            System.Diagnostics.Debug.WriteLine($"[Xtream] Category prioritized: {catName}");
        });
        
        if (categoriesToLoad.Count == 0) return;

        // Map'ler
        var liveCategoryMap = BuildCategoryMapFromXtream(allCategories.Where(c => c.Type == "live"));
        var vodCategoryMap = BuildCategoryMapFromXtream(allCategories.Where(c => c.Type == "vod"));
        var seriesCategoryMap = BuildCategoryMapFromXtream(allCategories.Where(c => c.Type == "series"));

        // Helper: Kategorileri öncelik sırasına göre raporla
        async Task ReportGroupsAsync(IEnumerable<Channel> channels, string fallbackLabel, string contentType)
        {
             var groups = channels.GroupBy(c => c.GroupTitle ?? fallbackLabel).ToDictionary(g => g.Key, g => g.ToList());
             
             // Önceden bildirilen veya keşfedilen tüm grupları temizlemek için (Empty state fix)
             // dummy kanalların silinmesi için onCategoryLoaded Boş liste ile çağrılmalı.
             var reportedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

             foreach (var group in groups)
             {
                 await onCategoryLoaded(group.Value, group.Key);
                 reportedGroups.Add(group.Key);
             }

             // Eksik grupları da temizle (Eğer keşfedilmiş ama kanalı yoksa)
             // ÖNEMLİ: Sadece bu tipe (live/vod/series) ait kategorileri temizle!
             foreach (var cat in categoriesToLoad.Where(c => c.Type == contentType))
             {
                 if (!reportedGroups.Contains(cat.Name))
                 {
                     await onCategoryLoaded([], cat.Name);
                     reportedGroups.Add(cat.Name);
                 }
             }
        }

        // 2. İçerikleri Paralel Çek (3 Büyük Görev)
        var liveTask = Task.Run(async () =>
        {
            var streams = await GetJsonAsync<List<XtreamLiveStreamDto>>(
                BuildApiUrl(normalizedBaseUrl, username, password, "get_live_streams"), cancellationToken);
            
            var channels = MapLiveChannels(streams, normalizedBaseUrl, username, password, liveCategoryMap);
            await ReportGroupsAsync(channels, "Live", "live");
        }, cancellationToken);

        Task? vodTask = null;
        Task? seriesTask = null;

        if (includeVod)
        {
            vodTask = Task.Run(async () =>
            {
                var streams = await GetJsonAsync<List<XtreamVodStreamDto>>(
                    BuildApiUrl(normalizedBaseUrl, username, password, "get_vod_streams"), cancellationToken);
                
                var channels = MapVodChannels(streams, normalizedBaseUrl, username, password, vodCategoryMap);
                await ReportGroupsAsync(channels, "VOD", "vod");
            }, cancellationToken);

            seriesTask = Task.Run(async () =>
            {
                var seriesDtos = await GetJsonAsync<List<XtreamSeriesDto>>(
                    BuildApiUrl(normalizedBaseUrl, username, password, "get_series"), cancellationToken);
                
                if (seriesDtos == null) return;

                var seriesChannels = MapSeriesAsEntries(seriesDtos, seriesCategoryMap);
                await ReportGroupsAsync(seriesChannels, "Series", "series");
            }, cancellationToken);
        }

        await Task.WhenAll(new[] { liveTask, vodTask ?? Task.CompletedTask, seriesTask ?? Task.CompletedTask });
    }

    private static IReadOnlyDictionary<string, string> BuildCategoryMapFromXtream(IEnumerable<XtreamCategory> categories)
    {
        return categories.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<XtreamSeriesDetail?> GetSeriesInfoAsync(
        string baseUrl,
        string username,
        string password,
        long seriesId,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        var url = BuildApiUrl(normalizedBaseUrl, username, password,
            "get_series_info", ("series_id", seriesId.ToString()));


        try
        {
            var json = await GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var detail = new XtreamSeriesDetail();

            // info block
            if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                detail.Name = GetStringOrNull(info, "name");
                detail.Cover = GetStringOrNull(info, "cover");
                // High-res backdrop: try backdrop_path, cover_big, movie_image
                detail.BackdropUrl = GetStringOrNull(info, "backdrop_path")
                                  ?? GetStringOrNull(info, "cover_big")
                                  ?? GetStringOrNull(info, "movie_image");
                detail.Plot = GetStringOrNull(info, "plot");
                detail.Genre = GetStringOrNull(info, "genre");
                detail.Cast = GetStringOrNull(info, "cast");
                detail.Director = GetStringOrNull(info, "director");
                detail.Rating = ParseDouble(GetStringOrNull(info, "rating"));
                detail.ReleaseYear = ParseInt(GetStringOrNull(info, "releaseDate")
                                        ?.Split('-').FirstOrDefault());
                detail.ContentRating = GetStringOrNull(info, "age");
                var tmdbRaw = GetStringOrNull(info, "tmdb_id") ?? GetStringOrNull(info, "tmdb");
                if (int.TryParse(tmdbRaw, out var parsedTmdb) && parsedTmdb > 0)
                    detail.TmdbId = parsedTmdb;
            }

            // seasons block
            if (root.TryGetProperty("seasons", out var seasons) &&
                seasons.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in seasons.EnumerateArray())
                {
                    var sn = ParseInt(GetStringOrNull(s, "season_number")
                             ?? (s.TryGetProperty("season_number", out var snProp)
                                ? snProp.GetRawText() : null));
                    detail.Seasons.Add(new XtreamSeasonDetail
                    {
                        SeasonNumber = sn ?? 0,
                        Name = GetStringOrNull(s, "name"),
                        Cover = GetStringOrNull(s, "cover"),
                        AirDate = GetStringOrNull(s, "air_date")
                    });
                }
            }

            // episodes block — can be { "1": [...], "2": [...] } (Object) or [...] (Array)
            if (root.TryGetProperty("episodes", out var episodes))
            {
                if (episodes.ValueKind == JsonValueKind.Object)
                {
                    int count = 0;
                    foreach (var seasonProp in episodes.EnumerateObject())
                    {
                        if (seasonProp.Value.ValueKind != JsonValueKind.Array) continue;
                        detail.Episodes[seasonProp.Name] = ParseEpisodeArray(seasonProp.Value, seasonProp.Name, detail.Cover);
                        count++;
                    }
                }
                else if (episodes.ValueKind == JsonValueKind.Array)
                {
                    // Fallback for single-season series or servers that return a flat array
                    detail.Episodes["1"] = ParseEpisodeArray(episodes, "1", detail.Cover);
                }
                else
                {
                }
            }
            else
            {
            }

            return detail;
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    public string GetEpgUrl(string baseUrl, string username, string password)
    {
        return $"{NormalizeBaseUrl(baseUrl)}/xmltv.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";
    }

    public async Task<List<Channel>> GetChannelsAsync(
        string baseUrl,
        string username,
        string password,
        bool includeSeriesEpisodes = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        var authenticated = await EnsureAuthenticatedAsync(normalizedBaseUrl, username, password, cancellationToken);

        if (!authenticated)
        {
            throw new InvalidOperationException(_localizationService.GetString("Xtream.Error.AuthFailedDetail"));
        }

        var liveCategoriesTask = GetJsonAsync<List<XtreamCategoryDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_live_categories"), cancellationToken);
        var vodCategoriesTask = GetJsonAsync<List<XtreamCategoryDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_vod_categories"), cancellationToken);
        var seriesCategoriesTask = GetJsonAsync<List<XtreamCategoryDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_series_categories"), cancellationToken);

        var liveTask = GetJsonAsync<List<XtreamLiveStreamDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_live_streams"), cancellationToken);
        var vodTask = GetJsonAsync<List<XtreamVodStreamDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_vod_streams"), cancellationToken);
        var seriesTask = GetJsonAsync<List<XtreamSeriesDto>>(
            BuildApiUrl(normalizedBaseUrl, username, password, "get_series"), cancellationToken);

        await Task.WhenAll(liveCategoriesTask, vodCategoriesTask, seriesCategoriesTask, liveTask, vodTask, seriesTask);

        var liveCategoryMap = BuildCategoryMap(await liveCategoriesTask);
        var vodCategoryMap = BuildCategoryMap(await vodCategoriesTask);
        var seriesCategoryMap = BuildCategoryMap(await seriesCategoriesTask);

        var channels = new List<Channel>();
        channels.AddRange(MapLiveChannels(await liveTask, normalizedBaseUrl, username, password, liveCategoryMap));
        channels.AddRange(MapVodChannels(await vodTask, normalizedBaseUrl, username, password, vodCategoryMap));

        var series = await seriesTask ?? new List<XtreamSeriesDto>();
        if (includeSeriesEpisodes)
        {
            var episodeChannels = await FetchSeriesEpisodesAsync(
                series,
                normalizedBaseUrl,
                username,
                password,
                seriesCategoryMap,
                cancellationToken);
            channels.AddRange(episodeChannels);
        }
        else
        {
            channels.AddRange(MapSeriesAsEntries(series, seriesCategoryMap));
        }

        return channels;
    }

    private async Task<bool> EnsureAuthenticatedAsync(
        string normalizedBaseUrl,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildAuthCacheKey(normalizedBaseUrl, username, password);
        
        // Periyodik temizlik yap (TOCTOU-safe: staleness check inside lock)
        CleanupExpiredAuthsIfNeeded();

        var state = AuthCache.GetOrAdd(cacheKey, _ => new CachedAuthState());

        if (state.IsAuthenticated.HasValue && state.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return state.IsAuthenticated.Value;
        }

        await state.Lock.WaitAsync(cancellationToken);
        try
        {
            if (state.IsAuthenticated.HasValue && state.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return state.IsAuthenticated.Value;
            }

            var authenticated = await AuthenticateAsync(normalizedBaseUrl, username, password, cancellationToken);
            var ttl = authenticated ? SuccessAuthTtl : FailedAuthTtl;
            
            state.IsAuthenticated = authenticated;
            state.ExpiresAt = DateTimeOffset.UtcNow.Add(ttl);
            
            return authenticated;
        }
        finally
        {
            state.Lock.Release();
        }
    }

    /// <summary>
    /// TOCTOU-safe cleanup: staleness check AND timestamp update both happen inside the lock,
    /// eliminating the race where multiple threads read the stale timestamp before any updates it.
    /// </summary>
    private static void CleanupExpiredAuthsIfNeeded()
    {
        if (!Monitor.TryEnter(CleanupLock)) return;
        try
        {
            var now = DateTimeOffset.UtcNow;

            // Check staleness INSIDE the lock (fixes TOCTOU race)
            if (now - _lastCleanup < TimeSpan.FromMinutes(30))
            {
                return;
            }

            var expiredKeys = AuthCache
                .Where(kvp => kvp.Value.IsAuthenticated.HasValue && kvp.Value.ExpiresAt < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                if (AuthCache.TryRemove(key, out var removed))
                {
                    removed.Lock.Dispose();
                }
            }

            // Update timestamp INSIDE the lock (fixes TOCTOU)
            _lastCleanup = now;
        }
        finally
        {
            Monitor.Exit(CleanupLock);
        }
    }

    private List<Channel> MapLiveChannels(
        IEnumerable<XtreamLiveStreamDto>? streams,
        string baseUrl,
        string username,
        string password,
        IReadOnlyDictionary<string, string> categories)
    {
        if (streams == null) return new List<Channel>();

        return streams
            .Where(s => s.StreamId > 0)
            .Select(s => new Channel
            {
                Name = SafeName(s.Name, _localizationService.GetString("Xtream.Channel.DefaultLive")),
                StreamUrl = $"{baseUrl}/live/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{s.StreamId}.ts",
                LogoUrl = s.StreamIcon,
                GroupTitle = ResolveCategory(s.CategoryId, s.CategoryName, categories, "Live"),
                TvgId = s.EpgChannelId,
                Type = ChannelType.Live
            })
            .ToList();
    }

    private List<Channel> MapVodChannels(
        IEnumerable<XtreamVodStreamDto>? streams,
        string baseUrl,
        string username,
        string password,
        IReadOnlyDictionary<string, string> categories)
    {
        if (streams == null) return new List<Channel>();

        return streams
            .Where(s => s.StreamId > 0)
            .Select(s =>
            {
                var extension = string.IsNullOrWhiteSpace(s.ContainerExtension) ? "mp4" : s.ContainerExtension;

                int? tmdbId = null;
                if (!string.IsNullOrWhiteSpace(s.TmdbId) &&
                    int.TryParse(s.TmdbId, out var t) && t > 0)
                    tmdbId = t;

                var posterUrl = FirstNonEmpty(
                    s.StreamIcon,
                    s.Cover,
                    s.MovieImage,
                    s.CoverBig,
                    s.Poster,
                    s.PosterUrl,
                    s.Image,
                    s.ScreenshotUri,
                    s.ScreenshotUrl);

                var backdropUrl = FirstNonEmpty(s.BackdropPath, s.CoverBig, s.MovieImage);

                return new Channel
                {
                    Name          = SafeName(s.Name, "VOD"),
                    StreamUrl     = $"{baseUrl}/movie/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{s.StreamId}.{extension}",
                    LogoUrl       = posterUrl,
                    BackdropUrl   = backdropUrl,
                    GroupTitle    = ResolveCategory(s.CategoryId, s.CategoryName, categories, "VOD"),
                    Type          = ChannelType.VOD,
                    Plot          = s.Plot,
                    Director      = string.IsNullOrWhiteSpace(s.Director) ? null : s.Director,
                    Cast          = string.IsNullOrWhiteSpace(s.Cast) ? null : s.Cast,
                    ContentRating = string.IsNullOrWhiteSpace(s.Age) ? null : s.Age,
                    ReleaseYear   = ParseInt(s.Year),
                    Rating        = ParseDouble(s.Rating),
                    TmdbId        = tmdbId,
                };
            })
            .ToList();
    }

    private List<Channel> MapSeriesAsEntries(
        IEnumerable<XtreamSeriesDto>? series,
        IReadOnlyDictionary<string, string> categories)
    {
        if (series == null) return new List<Channel>();

        return series
            .Where(s => s.SeriesId > 0)
            .Select(s =>
            {
                var groupTitle = ResolveCategory(s.CategoryId, null, categories, "Series");

                return new Channel
                {
                    Name = SafeName(s.Name, _localizationService.GetString("Xtream.Channel.DefaultSeries")),
                    // ← ID'yi URL'e göm — lazy load için anahtar
                    StreamUrl = $"xtream-series://{s.SeriesId}",
                    LogoUrl = s.Cover,
                    GroupTitle = groupTitle,
                    Type = ChannelType.Series,
                    Plot = s.Plot,
                    ReleaseYear = ParseInt(s.Year),
                    Rating = ParseDouble(s.Rating)
                };
            })
            .ToList();
    }
    private async Task<List<Channel>> FetchSeriesEpisodesAsync(
        IReadOnlyCollection<XtreamSeriesDto> series,
        string baseUrl,
        string username,
        string password,
        IReadOnlyDictionary<string, string> categories,
        CancellationToken cancellationToken)
    {
        if (series.Count == 0)
        {
            return new List<Channel>();
        }

        var allChannels = new ConcurrentBag<Channel>();
        using var throttler = new SemaphoreSlim(6);

        var tasks = series
            .Where(s => s.SeriesId > 0)
            .Select(async s =>
            {
                await throttler.WaitAsync(cancellationToken);
                try
                {
                    var episodeChannels = await GetSeriesEpisodeChannelsAsync(
                        s,
                        baseUrl,
                        username,
                        password,
                        categories,
                        cancellationToken);

                    foreach (var channel in episodeChannels)
                    {
                        allChannels.Add(channel);
                    }
                }
                catch
                {
                    // Ignore single-series failures and keep partial result set.
                }
                finally
                {
                    throttler.Release();
                }
            });

        await Task.WhenAll(tasks);
        return allChannels.ToList();
    }

    private async Task<List<Channel>> GetSeriesEpisodeChannelsAsync(
        XtreamSeriesDto series,
        string baseUrl,
        string username,
        string password,
        IReadOnlyDictionary<string, string> categories,
        CancellationToken cancellationToken)
    {
        var url = BuildApiUrl(baseUrl, username, password, "get_series_info", ("series_id", series.SeriesId.ToString()));
        var json = await GetStringAsync(url, cancellationToken);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("episodes", out var episodesElement))
        {
            return new List<Channel>();
        }

        var channels = new List<Channel>();

        if (episodesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var seasonProperty in episodesElement.EnumerateObject())
            {
                if (seasonProperty.Value.ValueKind != JsonValueKind.Array) continue;
                channels.AddRange(MapEpisodeArray(
                    seasonProperty.Value,
                    series,
                    seasonProperty.Name,
                    baseUrl,
                    username,
                    password,
                    categories));
            }
        }
        else if (episodesElement.ValueKind == JsonValueKind.Array)
        {
            channels.AddRange(MapEpisodeArray(
                episodesElement,
                series,
                null,
                baseUrl,
                username,
                password,
                categories));
        }

        return channels;
    }

    private IEnumerable<Channel> MapEpisodeArray(
        JsonElement episodeArray,
        XtreamSeriesDto series,
        string? seasonKey,
        string baseUrl,
        string username,
        string password,
        IReadOnlyDictionary<string, string> categories)
    {
        var groupTitle = ResolveCategory(series.CategoryId, null, categories, "Series");

        foreach (var ep in episodeArray.EnumerateArray())
        {
            var id = ParseLong(GetStringOrNull(ep, "id") ?? GetStringOrNull(ep, "episode_id"));
            if (id is null or <= 0)
            {
                continue;
            }

            var episodeNum = ParseInt(GetStringOrNull(ep, "episode_num"));
            var episodeTitle = GetStringOrNull(ep, "title") ?? GetStringOrNull(ep, "name") ?? $"Episode {episodeNum ?? 0}";
            var extension = GetStringOrNull(ep, "container_extension") ?? "mp4";

            string? plot = null;
            if (ep.TryGetProperty("info", out var infoElement) && infoElement.ValueKind == JsonValueKind.Object)
            {
                plot = GetStringOrNull(infoElement, "plot");
            }

            var prefix = BuildSeriesPrefix(series.Name, seasonKey, episodeNum);
            yield return new Channel
            {
                Name = $"{prefix} {episodeTitle}".Trim(),
                StreamUrl = $"{baseUrl}/series/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{id.Value}.{extension}",
                LogoUrl = series.Cover,
                GroupTitle = groupTitle,
                Type = ChannelType.Series,
                Plot = plot ?? series.Plot,
                ReleaseYear = ParseInt(series.Year),
                Rating = ParseDouble(series.Rating)
            };
        }
    }

    private string BuildSeriesPrefix(string? seriesName, string? seasonKey, int? episodeNum)
    {
        var safeName = SafeName(seriesName, _localizationService.GetString("Xtream.Channel.DefaultSeries"));
        if (int.TryParse(seasonKey, out var season) && episodeNum.HasValue)
        {
            return $"{safeName} S{season:00}E{episodeNum.Value:00} -";
        }

        if (episodeNum.HasValue)
        {
            return $"{safeName} E{episodeNum.Value:00} -";
        }

        return $"{safeName} -";
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        var text = await GetStringAsync(url, cancellationToken);
        return JsonSerializer.Deserialize<T>(text, JsonOptions);
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        return await NetworkRetry.ExecuteAsync(async () =>
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }, cancellationToken: cancellationToken);
    }

    private static string BuildApiUrl(
        string baseUrl,
        string username,
        string password,
        string? action,
        params (string Key, string Value)[] additionalQuery)
    {
        var query = new List<string>
        {
            $"username={Uri.EscapeDataString(username)}",
            $"password={Uri.EscapeDataString(password)}"
        };

        if (!string.IsNullOrWhiteSpace(action))
        {
            query.Add($"action={Uri.EscapeDataString(action)}");
        }

        foreach (var (key, value) in additionalQuery)
        {
            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        return $"{NormalizeBaseUrl(baseUrl)}/player_api.php?{string.Join("&", query)}";
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var normalized = baseUrl?.Trim() ?? string.Empty;
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"http://{normalized}";
        }

        return normalized.TrimEnd('/');
    }

    /// <summary>
    /// Builds a cache key using SHA256 hash of the password instead of plaintext.
    /// This prevents the password from sitting in memory as a dictionary key
    /// where heap profilers or memory dumps could trivially extract it.
    /// </summary>
    private static string BuildAuthCacheKey(string normalizedBaseUrl, string username, string password)
    {
        var passwordHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty)));
        return $"{normalizedBaseUrl}|{username}|{passwordHash}";
    }

    private static IReadOnlyDictionary<string, string> BuildCategoryMap(IEnumerable<XtreamCategoryDto>? categories)
    {
        return categories?
            .Where(c => !string.IsNullOrWhiteSpace(c.CategoryId))
            .GroupBy(c => c.CategoryId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CategoryName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveCategory(
        string? categoryId,
        string? fallbackCategoryName,
        IReadOnlyDictionary<string, string> categories,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(categoryId) &&
            categories.TryGetValue(categoryId, out var resolved) &&
            !string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        if (!string.IsNullOrWhiteSpace(fallbackCategoryName))
        {
            return fallbackCategoryName;
        }

        return fallback;
    }

    private static string SafeName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return System.Net.WebUtility.UrlDecode(value).Trim();
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? ParseInt(string? input)
    {
        if (int.TryParse(input, out var v)) return v;
        return null;
    }

    private static long? ParseLong(string? input)
    {
        if (long.TryParse(input, out var v)) return v;
        return null;
    }

    private static double? ParseDouble(string? input)
    {
        if (double.TryParse(input, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }

        return null;
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private sealed class XtreamAuthPayload
    {
        [JsonPropertyName("user_info")]
        public XtreamUserInfo? UserInfo { get; set; }
    }

    private sealed class XtreamUserInfo
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class XtreamCategoryDto
    {
        [JsonPropertyName("category_id")]
        public string? CategoryId { get; set; }

        [JsonPropertyName("category_name")]
        public string? CategoryName { get; set; }
    }

    private sealed class XtreamLiveStreamDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("stream_id")]
        public long StreamId { get; set; }

        [JsonPropertyName("stream_icon")]
        public string? StreamIcon { get; set; }

        [JsonPropertyName("epg_channel_id")]
        public string? EpgChannelId { get; set; }

        [JsonPropertyName("category_id")]
        public string? CategoryId { get; set; }

        [JsonPropertyName("category_name")]
        public string? CategoryName { get; set; }
    }

    private sealed class XtreamVodStreamDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("stream_id")]
        public long StreamId { get; set; }

        [JsonPropertyName("stream_icon")]
        public string? StreamIcon { get; set; }

        [JsonPropertyName("cover")]
        public string? Cover { get; set; }

        [JsonPropertyName("movie_image")]
        public string? MovieImage { get; set; }

        [JsonPropertyName("cover_big")]
        public string? CoverBig { get; set; }

        [JsonPropertyName("poster")]
        public string? Poster { get; set; }

        [JsonPropertyName("poster_url")]
        public string? PosterUrl { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }

        [JsonPropertyName("screenshot_uri")]
        public string? ScreenshotUri { get; set; }

        [JsonPropertyName("screenshot_url")]
        public string? ScreenshotUrl { get; set; }

        [JsonPropertyName("category_id")]
        public string? CategoryId { get; set; }

        [JsonPropertyName("category_name")]
        public string? CategoryName { get; set; }

        [JsonPropertyName("container_extension")]
        public string? ContainerExtension { get; set; }

        [JsonPropertyName("rating")]
        public string? Rating { get; set; }

        [JsonPropertyName("year")]
        public string? Year { get; set; }

        [JsonPropertyName("plot")]
        public string? Plot { get; set; }

        [JsonPropertyName("tmdb_id")]
        public string? TmdbId { get; set; }

        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; set; }

        [JsonPropertyName("cast")]
        public string? Cast { get; set; }

        [JsonPropertyName("director")]
        public string? Director { get; set; }

        [JsonPropertyName("age")]
        public string? Age { get; set; }
    }

    private sealed class XtreamSeriesDto
    {
        [JsonPropertyName("series_id")]
        public long SeriesId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("cover")]
        public string? Cover { get; set; }

        [JsonPropertyName("plot")]
        public string? Plot { get; set; }

        [JsonPropertyName("rating")]
        public string? Rating { get; set; }

        [JsonPropertyName("year")]
        public string? Year { get; set; }

        [JsonPropertyName("category_id")]
        public string? CategoryId { get; set; }
    }

    private sealed class FlexibleStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.GetRawText();
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value);
        }
    }

    private List<XtreamEpisodeDetail> ParseEpisodeArray(JsonElement array, string seasonName, string? seriesCover = null)
    {
        var epList = new List<XtreamEpisodeDetail>();
        if (array.ValueKind != JsonValueKind.Array) return epList;

        foreach (var ep in array.EnumerateArray())
        {
            var epIdStr = GetStringOrNull(ep, "id") 
                       ?? GetStringOrNull(ep, "id") 
                       ?? GetStringOrNull(ep, "stream_id");
            var epId = ParseLong(epIdStr);
            if (epId is null or <= 0) continue;

            string? coverUrl = null;
            string? plot = null;
            double? duration = null;
            string? airDate = null;
            double? epRating = null;

            // Get cover URL from various possible fields
            coverUrl = GetStringOrNull(ep, "stream_icon")
                      ?? GetStringOrNull(ep, "icon")
                      ?? GetStringOrNull(ep, "cover");

            if (ep.TryGetProperty("info", out var epInfo) && epInfo.ValueKind == JsonValueKind.Object)
            {
                coverUrl ??= GetStringOrNull(epInfo, "movie_image")
                           ?? GetStringOrNull(epInfo, "cover")
                           ?? GetStringOrNull(epInfo, "screenshot_uri")
                           ?? GetStringOrNull(epInfo, "image");

                plot = GetStringOrNull(epInfo, "plot");
                airDate = GetStringOrNull(epInfo, "releasedate")
                           ?? GetStringOrNull(epInfo, "air_date");
                epRating = ParseDouble(GetStringOrNull(epInfo, "rating"));

                if (epInfo.TryGetProperty("duration_secs", out var ds) &&
                    ds.ValueKind == JsonValueKind.Number)
                    duration = ds.GetDouble();
            }

            // --- FALLBACK ---
            coverUrl ??= seriesCover;

            if (!int.TryParse(seasonName, out var fallbackSeason))
                fallbackSeason = 1;

            epList.Add(new XtreamEpisodeDetail
            {
                Id = epId.Value,
                EpisodeNum = ParseInt(GetStringOrNull(ep, "episode_num")) ?? 0,
                Title = GetStringOrNull(ep, "title"),
                ContainerExtension = GetStringOrNull(ep, "container_extension") ?? "mp4",
                Season = ParseInt(GetStringOrNull(ep, "season")) ?? fallbackSeason,
                Plot = plot,
                CoverUrl = coverUrl,
                DurationSecs = duration,
                AirDate = airDate,
                Rating = epRating
            });
        }
        return epList;
    }

    private sealed class CachedAuthState
    {
        public bool? IsAuthenticated { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public SemaphoreSlim Lock { get; } = new(1, 1);
    }
}
