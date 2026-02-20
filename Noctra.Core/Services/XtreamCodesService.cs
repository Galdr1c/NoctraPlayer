using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noctra.Models;
using Noctra.Services.Interfaces;
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
    private static readonly ConcurrentDictionary<string, CachedAuthState> AuthCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan SuccessAuthTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailedAuthTtl = TimeSpan.FromSeconds(30);
    private static DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;
    private static readonly object CleanupLock = new();

    public XtreamCodesService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(baseUrl, username, password, action: null);
        var payload = await GetJsonAsync<XtreamAuthPayload>(url, cancellationToken);
        return string.Equals(payload?.UserInfo?.Status, "Active", StringComparison.OrdinalIgnoreCase);
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
            throw new InvalidOperationException("Xtream kimlik dogrulamasi basarisiz. Kullanici adi/sifre veya sunucu bilgisi hatali olabilir.");
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
        
        // Periyodik temizlik yap (her 30 dakikada bir)
        if (DateTimeOffset.UtcNow - _lastCleanup > TimeSpan.FromMinutes(30))
        {
            CleanupExpiredAuths();
        }

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

    private static void CleanupExpiredAuths()
    {
        if (!Monitor.TryEnter(CleanupLock)) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var expiredKeys = AuthCache
                .Where(kvp => kvp.Value.IsAuthenticated.HasValue && kvp.Value.ExpiresAt < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                if (AuthCache.TryRemove(key, out var state))
                {
                    state.Lock.Dispose();
                }
            }
            _lastCleanup = now;
        }
        finally
        {
            Monitor.Exit(CleanupLock);
        }
    }

    private static List<Channel> MapLiveChannels(
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
                Name = SafeName(s.Name, "Canli Kanal"),
                StreamUrl = $"{baseUrl}/live/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{s.StreamId}.ts",
                LogoUrl = s.StreamIcon,
                GroupTitle = ResolveCategory(s.CategoryId, s.CategoryName, categories, "Live"),
                TvgId = s.EpgChannelId,
                Type = ChannelType.Live
            })
            .ToList();
    }

    private static List<Channel> MapVodChannels(
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
                return new Channel
                {
                    Name = SafeName(s.Name, "VOD"),
                    StreamUrl = $"{baseUrl}/movie/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{s.StreamId}.{extension}",
                    LogoUrl = s.StreamIcon,
                    GroupTitle = ResolveCategory(s.CategoryId, s.CategoryName, categories, "VOD"),
                    Type = ChannelType.VOD,
                    Plot = s.Plot,
                    ReleaseYear = ParseInt(s.Year),
                    Rating = ParseDouble(s.Rating)
                };
            })
            .ToList();
    }

    private static List<Channel> MapSeriesAsEntries(
        IEnumerable<XtreamSeriesDto>? series,
        IReadOnlyDictionary<string, string> categories)
    {
        if (series == null) return new List<Channel>();

        return series
            .Where(s => s.SeriesId > 0)
            .Select(s => new Channel
            {
                Name = SafeName(s.Name, "Dizi"),
                StreamUrl = string.Empty,
                LogoUrl = s.Cover,
                GroupTitle = ResolveCategory(s.CategoryId, null, categories, "Series"),
                Type = ChannelType.Series,
                Plot = s.Plot,
                ReleaseYear = ParseInt(s.Year),
                Rating = ParseDouble(s.Rating)
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

    private static IEnumerable<Channel> MapEpisodeArray(
        JsonElement episodeArray,
        XtreamSeriesDto series,
        string? seasonKey,
        string baseUrl,
        string username,
        string password,
        IReadOnlyDictionary<string, string> categories)
    {
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
                GroupTitle = ResolveCategory(series.CategoryId, null, categories, "Series"),
                Type = ChannelType.Series,
                Plot = plot ?? series.Plot,
                ReleaseYear = ParseInt(series.Year),
                Rating = ParseDouble(series.Rating)
            };
        }
    }

    private static string BuildSeriesPrefix(string? seriesName, string? seasonKey, int? episodeNum)
    {
        var safeName = SafeName(seriesName, "Dizi");
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

    private static string BuildAuthCacheKey(string normalizedBaseUrl, string username, string password)
        => $"{normalizedBaseUrl}|{username}|{password}";

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
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

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
        if (!element.TryGetProperty(propertyName, out var prop))
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

    private sealed class CachedAuthState
    {
        public bool? IsAuthenticated { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public SemaphoreSlim Lock { get; } = new(1, 1);
    }
}
