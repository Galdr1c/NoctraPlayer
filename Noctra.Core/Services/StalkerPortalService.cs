using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class StalkerPortalService : IStalkerPortalService
{
    private readonly HttpClient _httpClient;

    public StalkerPortalService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> AuthenticateAsync(string portalUrl, string macAddress, CancellationToken cancellationToken = default)
    {
        var token = await HandshakeAsync(portalUrl, macAddress, cancellationToken);
        return !string.IsNullOrWhiteSpace(token);
    }

    public async Task<List<Channel>> GetChannelsAsync(
        string portalUrl,
        string macAddress,
        bool includeVod = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedPortalUrl = NormalizePortalUrl(portalUrl);
        var endpoint = BuildLoadEndpoint(normalizedPortalUrl);
        var token = await HandshakeAsync(normalizedPortalUrl, macAddress, cancellationToken);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Stalker Portal handshake basarisiz. URL veya MAC adresini kontrol edin.");
        }

        var liveGenreMap = await GetGenreMapAsync(endpoint, token, "itv", cancellationToken);
        var vodGenreMap = await GetGenreMapAsync(endpoint, token, "vod", cancellationToken);

        var channels = new List<Channel>();

        var liveItems = await GetOrderedListAllPagesAsync(endpoint, token, "itv", cancellationToken);
        var liveChannels = await BuildChannelsAsync(
            liveItems,
            endpoint,
            token,
            ChannelType.Live,
            liveGenreMap,
            cancellationToken);
        channels.AddRange(liveChannels);

        if (includeVod)
        {
            var vodItems = await GetOrderedListAllPagesAsync(endpoint, token, "vod", cancellationToken);
            var vodChannels = await BuildChannelsAsync(
                vodItems,
                endpoint,
                token,
                ChannelType.VOD,
                vodGenreMap,
                cancellationToken);
            channels.AddRange(vodChannels);
        }

        return channels;
    }

    private async Task<List<Channel>> BuildChannelsAsync(
        IReadOnlyCollection<StalkerListItem> items,
        string endpoint,
        string token,
        ChannelType channelType,
        IReadOnlyDictionary<string, string> genreMap,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return new List<Channel>();
        }

        var channels = new ConcurrentBag<Channel>();
        using var throttler = new SemaphoreSlim(8);

        var tasks = items.Select(async item =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var cmd = await CreateLinkAsync(endpoint, token, item.Cmd, cancellationToken);
                var streamUrl = NormalizeStreamCommand(cmd);
                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    return;
                }

                channels.Add(new Channel
                {
                    Name = string.IsNullOrWhiteSpace(item.Name) ? "Isimsiz Kanal" : item.Name.Trim(),
                    StreamUrl = streamUrl,
                    LogoUrl = item.Logo,
                    GroupTitle = ResolveGenre(item.TvGenreId, genreMap, channelType),
                    Type = channelType
                });
            }
            catch
            {
                // Individual stream errors should not fail the whole import.
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);
        return channels.ToList();
    }

    private async Task<string> HandshakeAsync(string portalUrl, string macAddress, CancellationToken cancellationToken)
    {
        var endpoint = BuildLoadEndpoint(portalUrl);
        var body = new Dictionary<string, object?>
        {
            ["action"] = "handshake",
            ["type"] = "stb",
            ["token"] = string.Empty,
            ["mac"] = macAddress
        };

        var js = await PostForJsAsync(endpoint, body, token: null, cancellationToken);
        return GetString(js, "token") ?? string.Empty;
    }

    private async Task<string> CreateLinkAsync(string endpoint, string token, string? cmd, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["action"] = "create_link",
            ["cmd"] = cmd ?? string.Empty,
            ["series"] = string.Empty,
            ["forced_storage"] = string.Empty,
            ["disable_ad"] = "0",
            ["token"] = token
        };

        var js = await PostForJsAsync(endpoint, body, token, cancellationToken);
        return GetString(js, "cmd") ?? string.Empty;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetGenreMapAsync(
        string endpoint,
        string token,
        string contentType,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["action"] = "get_genres",
            ["type"] = contentType,
            ["token"] = token
        };

        var js = await PostForJsAsync(endpoint, body, token, cancellationToken);
        if (js.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string>();
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in js.EnumerateArray())
        {
            var id = GetString(element, "id");
            var title = GetString(element, "title") ?? GetString(element, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            map[id] = title;
        }

        return map;
    }

    private async Task<List<StalkerListItem>> GetOrderedListAllPagesAsync(
        string endpoint,
        string token,
        string listType,
        CancellationToken cancellationToken)
    {
        var items = new List<StalkerListItem>();
        var page = 1;
        int? totalItems = null;
        int? maxPageItems = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var body = new Dictionary<string, object?>
            {
                ["action"] = "get_ordered_list",
                ["type"] = listType,
                ["sortby"] = listType == "itv" ? "number" : "added",
                ["genre"] = listType == "itv" ? "*" : null,
                ["category"] = listType == "vod" ? "*" : null,
                ["p"] = page,
                ["token"] = token
            };

            var js = await PostForJsAsync(endpoint, body, token, cancellationToken);
            totalItems ??= GetInt(js, "total_items");
            maxPageItems ??= GetInt(js, "max_page_items");

            var dataArray = js.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array
                ? dataElement
                : default;

            if (dataArray.ValueKind != JsonValueKind.Array || dataArray.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var item in dataArray.EnumerateArray())
            {
                items.Add(new StalkerListItem
                {
                    Name = GetString(item, "name"),
                    Cmd = GetString(item, "cmd"),
                    Logo = GetString(item, "logo"),
                    TvGenreId = GetString(item, "tv_genre_id")
                });
            }

            if (totalItems.HasValue && maxPageItems.HasValue && maxPageItems.Value > 0)
            {
                var totalPages = (int)Math.Ceiling(totalItems.Value / (double)maxPageItems.Value);
                if (page >= totalPages)
                {
                    break;
                }
            }
            else
            {
                if (dataArray.GetArrayLength() < 14)
                {
                    break;
                }
            }

            page++;
        }

        return items;
    }

    private async Task<JsonElement> PostForJsAsync(
        string endpoint,
        Dictionary<string, object?> body,
        string? token,
        CancellationToken cancellationToken)
    {
        var json = await NetworkRetry.ExecuteAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(body)
            };

            request.Headers.TryAddWithoutValidation("X-User-Agent", "Model: MAG250; Link: WiFi");
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }, cancellationToken: cancellationToken);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("js", out var js))
        {
            throw new InvalidOperationException("Stalker API yaniti beklenen formatta degil (js alani bulunamadi).");
        }

        return js.Clone();
    }

    private static string BuildLoadEndpoint(string portalUrl)
    {
        var baseUrl = NormalizePortalUrl(portalUrl);
        if (baseUrl.Contains("/stalker_portal", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUrl.TrimEnd('/')}/server/load.php";
        }

        return $"{baseUrl}/stalker_portal/server/load.php";
    }

    private static string NormalizePortalUrl(string portalUrl)
    {
        var normalized = portalUrl.Trim();
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"http://{normalized}";
        }

        return normalized.TrimEnd('/');
    }

    private static string? GetString(JsonElement element, string propertyName)
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

    private static int? GetInt(JsonElement element, string propertyName)
    {
        var raw = GetString(element, propertyName);
        return int.TryParse(raw, out var value) ? value : null;
    }

    private static string NormalizeStreamCommand(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd))
        {
            return string.Empty;
        }

        var trimmed = cmd.Trim();
        var httpIndex = trimmed.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (httpIndex < 0)
        {
            httpIndex = trimmed.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        }

        if (httpIndex >= 0)
        {
            return trimmed[httpIndex..].Trim();
        }

        return trimmed.Replace("ffrt", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static string ResolveGenre(string? genreId, IReadOnlyDictionary<string, string> genreMap, ChannelType type)
    {
        if (!string.IsNullOrWhiteSpace(genreId) && genreMap.TryGetValue(genreId, out var genreName))
        {
            return genreName;
        }

        return type == ChannelType.Live ? "Live" : "VOD";
    }

    private sealed class StalkerListItem
    {
        public string? Name { get; set; }
        public string? Cmd { get; set; }
        public string? Logo { get; set; }
        public string? TvGenreId { get; set; }
    }
}

