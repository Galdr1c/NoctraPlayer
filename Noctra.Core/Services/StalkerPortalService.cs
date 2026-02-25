using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Stalker Middleware Portal API servisi V2 (Yüksek Performans).
/// Bu sürüm tüm ağ gecikmelerini minimize etmek için paralel işleme ve erken token yakalama kullanır.
/// </summary>
public class StalkerPortalService : IStalkerPortalService
{
    private readonly HttpClient _httpClient;

    // Diagnostic logging helper
    private static void DiagnosticLog(string message)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logPath = Path.Combine(localAppData, "Noctra", "logs", "startup.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [StalkerService] {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line, System.Text.Encoding.UTF8);
        }
        catch { /* Diagnostic logging must not fail */ }
    }

    // Token cache — portal URL + MAC kombinasyonuna göre
    private static readonly ConcurrentDictionary<string, CachedTokenState> TokenCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TokenLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(10);

    // Stalker sunucularının olası endpoint path'leri (en yaygından başlayarak)
    private static readonly string[] KnownPortalPaths =
    [
        "/stalker_portal/server/load.php",
        "/server/load.php",
        "/c/",
        "/portal.php",
        "/stalker_portal/c/",
        "/stalker_portal/server/",
        "/server/"
    ];

    public StalkerPortalService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────────────────

    public async Task<bool> AuthenticateAsync(
        string portalUrl,
        string macAddress,
        CancellationToken cancellationToken = default)
    {
        var result = await ResolveEndpointOptimizedAsync(portalUrl, macAddress, cancellationToken);
        if (result.Endpoint == null) return false;

        var token = await GetOrCreateTokenAsync(result.NormalizedUrl, result.Endpoint, macAddress, result.InitialToken, cancellationToken);
        return !string.IsNullOrWhiteSpace(token);
    }

    public async Task<List<Channel>> GetChannelsAsync(
        string portalUrl,
        string macAddress,
        bool includeVod = true,
        CancellationToken cancellationToken = default)
    {
        DiagnosticLog($"GetChannelsAsync started for: {portalUrl} (MAC: {macAddress})");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Endpoint'i ve Token'ı paralel keşfet (Handshake probe içinde yapılır)
        DiagnosticLog("Step 1: Resolving endpoint and checking for early token...");
        var result = await ResolveEndpointOptimizedAsync(portalUrl, macAddress, cancellationToken);
        DiagnosticLog($"Step 1 Completed in {sw.ElapsedMilliseconds}ms. Winner: {result.Endpoint ?? "NONE"}");

        if (result.Endpoint == null)
        {
            DiagnosticLog("CRITICAL: Stalker Portal endpoint not found after probing all candidates.");
            throw new InvalidOperationException("Stalker Portal endpoint bulunamadı.");
        }

        // 2. Token al (Probe'dan gelen token varsa onu kullanır, yoksa handshake yapar)
        DiagnosticLog("Step 2: Getting or creating token...");
        var token = await GetOrCreateTokenAsync(result.NormalizedUrl, result.Endpoint, macAddress, result.InitialToken, cancellationToken);
        DiagnosticLog($"Step 2 Completed in {sw.ElapsedMilliseconds}ms. Token obtained: {!string.IsNullOrEmpty(token)}");

        if (string.IsNullOrWhiteSpace(token))
        {
            DiagnosticLog("CRITICAL: Stalker Portal authentication failed (No token).");
            throw new InvalidOperationException("Kimlik doğrulama başarısız.");
        }

        // 3. Kanalları yükle (Genre, Live ve VOD hepsi paralel)
        try
        {
            DiagnosticLog("Step 3: Loading channels (Parallel V2)...");
            var channels = await LoadChannelsWithTokenV2Async(result.Endpoint, token, macAddress, includeVod, cancellationToken);
            DiagnosticLog($"Step 3 Completed in {sw.ElapsedMilliseconds}ms. Total channels: {channels.Count}");
            return channels;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            DiagnosticLog("WARNING: Token expired during load. Retrying once with refreshed token...");
            // Token süresi dolmuşsa yenile (bir kez)
            InvalidateToken(result.NormalizedUrl, macAddress);
            var refreshedToken = await GetOrCreateTokenAsync(result.NormalizedUrl, result.Endpoint, macAddress, null, cancellationToken);
            if (string.IsNullOrWhiteSpace(refreshedToken))
            {
                DiagnosticLog("CRITICAL: Token refresh failed.");
                throw new InvalidOperationException("Token yenilenemedi.");
            }

            var channels = await LoadChannelsWithTokenV2Async(result.Endpoint, refreshedToken, macAddress, includeVod, cancellationToken);
            DiagnosticLog($"Retry Completed in {sw.ElapsedMilliseconds}ms. Total channels: {channels.Count}");
            return channels;
        }
        catch (Exception ex)
        {
            DiagnosticLog($"CRITICAL ERROR in Step 3: {ex.Message}");
            throw;
        }
        finally
        {
            DiagnosticLog($"GetChannelsAsync Total Time: {sw.ElapsedMilliseconds}ms");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ENDPOINT KEŞFİ (OPTIMIZED PARALLEL)
    // ─────────────────────────────────────────────────────────────

    private async Task<(string NormalizedUrl, string? Endpoint, string? InitialToken)> ResolveEndpointOptimizedAsync(
        string portalUrl,
        string macAddress,
        CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizePortalUrl(portalUrl);
        var candidates = KnownPortalPaths.Select(p => $"{normalizedUrl}{p}").ToList();
        
        if (!candidates.Contains(normalizedUrl)) candidates.Add(normalizedUrl);
        if (!candidates.Contains($"{normalizedUrl}/")) candidates.Add($"{normalizedUrl}/");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(8));

        DiagnosticLog($"ResolveEndpointOptimizedAsync: Probing {candidates.Count} candidates...");
        var tasks = candidates.Select(url => ProbeEndpointAsync(url, macAddress, cts));
        var results = await Task.WhenAll(tasks);
        var winner = results.FirstOrDefault(r => r != null);

        return (normalizedUrl, winner?.Url, winner?.Token);
    }

    private async Task<(string Url, string? Token)?> ProbeEndpointAsync(
        string url, 
        string macAddress, 
        CancellationTokenSource cts)
    {
        try
        {
            var query = BuildQueryString(new Dictionary<string, string>
            {
                ["action"] = "handshake",
                ["type"] = "stb",
                ["mac"] = macAddress
            });

            using var request = BuildGetRequest(url, query, macAddress, null);
            using var individualCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, individualCts.Token);

            using var response = await _httpClient.SendAsync(request, linkedCts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
            if (!json.Trim().StartsWith('{')) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("js", out var jsEl) || root.TryGetProperty("result", out _))
            {
                var token = GetString(jsEl.ValueKind != JsonValueKind.Undefined ? jsEl : root, "token");
                cts.Cancel(); // İlk bulan diğerlerini durdurur
                return (Url: url, Token: token);
            }
        }
        catch { }
        return null;
    }

    // ─────────────────────────────────────────────────────────────
    //  TOKEN VE PROFIL YÖNETİMİ
    // ─────────────────────────────────────────────────────────────

    private async Task<string> GetOrCreateTokenAsync(
        string normalizedPortalUrl,
        string endpoint,
        string macAddress,
        string? initialToken,
        CancellationToken cancellationToken)
    {
        var key = BuildTokenCacheKey(normalizedPortalUrl, macAddress);

        if (TokenCache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Token;
        }

        var tokenLock = TokenLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (TokenCache.TryGetValue(key, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
                return cached.Token;

            var token = initialToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                token = await HandshakeAsync(endpoint, macAddress, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                // get_profile FIRE-AND-FORGET: Kanal listesi ile paralel gitmesi için beklemiyoruz
                _ = Task.Run(() => GetProfileAsync(endpoint, token, macAddress, CancellationToken.None));
                
                TokenCache[key] = new CachedTokenState(token, DateTimeOffset.UtcNow.Add(TokenTtl));
            }

            return token ?? string.Empty;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private async Task<string> HandshakeAsync(string endpoint, string macAddress, CancellationToken cancellationToken)
    {
        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["action"] = "handshake",
            ["type"] = "stb",
            ["mac"] = macAddress
        });

        var js = await GetForJsAsync(endpoint, query, macAddress, null, cancellationToken);
        return GetString(js, "token") ?? string.Empty;
    }

    private async Task GetProfileAsync(string endpoint, string token, string macAddress, CancellationToken cancellationToken)
    {
        try
        {
            var query = BuildQueryString(new Dictionary<string, string>
            {
                ["action"] = "get_profile",
                ["type"] = "stb",
                ["token"] = token,
                ["hd"] = "1",
                ["stb_type"] = "MAG250"
            });
            await GetForJsAsync(endpoint, query, macAddress, token, cancellationToken);
        }
        catch { /* Non-fatal */ }
    }

    // ─────────────────────────────────────────────────────────────
    //  KANAL YÜKLEME V2 (MAX PARALELLISM)
    // ─────────────────────────────────────────────────────────────

    private async Task<List<Channel>> LoadChannelsWithTokenV2Async(
        string endpoint,
        string token,
        string macAddress,
        bool includeVod,
        CancellationToken cancellationToken)
    {
        DiagnosticLog($"LoadChannelsWithTokenV2Async: Starting parallel fetches (IncludeVod: {includeVod})");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Tüm veri çekme işlemlerini paralel başlat
        var liveGenreTask = GetGenreMapAsync(endpoint, token, macAddress, "itv", cancellationToken);
        var liveItemsTask = GetOrderedListAllPagesAsync(endpoint, token, macAddress, "itv", cancellationToken);
        
        var vodGenreTask = includeVod 
            ? GetGenreMapAsync(endpoint, token, macAddress, "vod", cancellationToken) 
            : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
            
        var vodItemsTask = includeVod 
            ? GetOrderedListAllPagesAsync(endpoint, token, macAddress, "vod", cancellationToken) 
            : Task.FromResult(new List<StalkerListItem>());

        var seriesGenreTask = includeVod
            ? GetGenreMapAsync(endpoint, token, macAddress, "series", cancellationToken)
            : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        var seriesItemsTask = includeVod
            ? GetOrderedListAllPagesAsync(endpoint, token, macAddress, "series", cancellationToken)
            : Task.FromResult(new List<StalkerListItem>());

        await Task.WhenAll(liveGenreTask, liveItemsTask, vodGenreTask, vodItemsTask, seriesGenreTask, seriesItemsTask);
        DiagnosticLog($"LoadChannelsWithTokenV2Async: Parallel fetch completed in {sw.ElapsedMilliseconds}ms");

        var channels = new List<Channel>();
        channels.AddRange(BuildChannels(await liveItemsTask, endpoint, token, ChannelType.Live, await liveGenreTask));
        
        if (includeVod)
        {
            channels.AddRange(BuildChannels(await vodItemsTask, endpoint, token, ChannelType.VOD, await vodGenreTask));
            channels.AddRange(BuildChannels(await seriesItemsTask, endpoint, token, ChannelType.Series, await seriesGenreTask));
        }

        return channels;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetGenreMapAsync(
        string endpoint,
        string token,
        string macAddress,
        string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            var action = contentType == "itv" ? "get_genres" : "get_categories";
            var query = BuildQueryString(new Dictionary<string, string>
            {
                ["action"] = action,
                ["type"] = contentType,
                ["token"] = token
            });

            var js = await GetForJsAsync(endpoint, query, macAddress, token, cancellationToken);
            if (js.ValueKind != JsonValueKind.Array) return new Dictionary<string, string>();

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in js.EnumerateArray())
            {
                var id = GetString(element, "id") ?? GetString(element, "category_id") ?? GetString(element, "genre_id");
                var title = GetString(element, "title") ?? GetString(element, "name");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
                    map[id] = title;
            }
            DiagnosticLog($"GetGenreMapAsync ({contentType}): Loaded {map.Count} categories.");
            return map;
        }
        catch { return new Dictionary<string, string>(); }
    }

    private async Task<List<StalkerListItem>> GetOrderedListAllPagesAsync(
        string endpoint,
        string token,
        string macAddress,
        string listType,
        CancellationToken cancellationToken)
    {
        DiagnosticLog($"GetOrderedListAllPagesAsync ({listType}) started...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. İlk sayfayı çekip toplam sayfa sayısını bul
        var firstPage = await GetPageAsync(endpoint, token, macAddress, listType, 1, cancellationToken);
        DiagnosticLog($"GetOrderedList ({listType}) Page 1: {firstPage.Items.Count} items. Total: {firstPage.TotalItems}");

        if (firstPage.Items.Count == 0) return [];

        int totalPages = 1;
        if (firstPage.TotalItems.HasValue && firstPage.MaxPageItems.HasValue && firstPage.MaxPageItems.Value > 0)
        {
            totalPages = (int)Math.Ceiling(firstPage.TotalItems.Value / (double)firstPage.MaxPageItems.Value);
        }

        if (totalPages <= 1) return firstPage.Items;

        // FULL SYNC: Removed the page cap to fetch all content as requested.
        // For massive portals (e.g. 100k+ items), this will take time but provide complete data.
        DiagnosticLog($"[{listType}] Total pages to fetch: {totalPages}");

        // 2. Diğer tüm sayfaları paralel çek (Semaphore ile limitli)
        var allPages = new List<StalkerListItem>[totalPages];
        allPages[0] = firstPage.Items;

        int completedPages = 1;
        var semaphore = new SemaphoreSlim(15); // Higher concurrency for full sync
        var tasks = Enumerable.Range(2, totalPages - 1).Select(async p =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await GetPageAsync(endpoint, token, macAddress, listType, p, cancellationToken);
                allPages[p - 1] = result.Items;
                
                var done = Interlocked.Increment(ref completedPages);
                if (done % 50 == 0 || done == totalPages)
                {
                    DiagnosticLog($"[{listType}] Fetch Progress: {done}/{totalPages} pages ({(done * 100.0 / totalPages):F1}%)");
                }
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        var resultList = new List<StalkerListItem>(firstPage.TotalItems ?? totalPages * 14);
        foreach (var page in allPages) if (page != null) resultList.AddRange(page);
        
        return resultList;
    }

    private async Task<(List<StalkerListItem> Items, int? TotalItems, int? MaxPageItems)> GetPageAsync(
        string endpoint,
        string token,
        string macAddress,
        string listType,
        int page,
        CancellationToken cancellationToken)
    {
        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["action"] = "get_ordered_list",
            ["type"] = listType,
            ["sortby"] = listType == "itv" ? "number" : "added",
            ["p"] = page.ToString(),
            ["size"] = "100", // Try to request more items per page to reduce requests
            ["token"] = token,
            [listType == "itv" ? "genre" : "category"] = "*"
        });

        try
        {
            var js = await GetForJsAsync(endpoint, query, macAddress, token, cancellationToken);
            var items = new List<StalkerListItem>();
            
            if (js.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataEl.EnumerateArray())
                {
                    items.Add(new StalkerListItem
                    {
                        Name = GetString(item, "name"),
                        Cmd = GetString(item, "cmd"),
                        Logo = GetString(item, "logo"),
                        TvGenreId = GetString(item, "tv_genre_id") ?? GetString(item, "category_id") ?? GetString(item, "genre_id"),
                        CategoryName = GetString(item, "category_name") ?? GetString(item, "genre_name")
                    });
                }
            }
            return (items, GetInt(js, "total_items"), GetInt(js, "max_page_items"));
        }
        catch { return ([], null, null); }
    }

    private static List<Channel> BuildChannels(
        IReadOnlyCollection<StalkerListItem> items,
        string endpoint,
        string token,
        ChannelType channelType,
        IReadOnlyDictionary<string, string> genreMap)
    {
        if (items.Count == 0) return [];
        var baseUrl = ExtractBaseUrl(endpoint);
        var channels = new List<Channel>(items.Count);

        int logCount = 0;
        foreach (var item in items)
        {
            var streamUrl = NormalizeStreamCommand(item.Cmd, baseUrl, token);
            if (string.IsNullOrWhiteSpace(streamUrl)) continue;

            var group = genreMap.TryGetValue(item.TvGenreId ?? "", out var categoryName) ? categoryName : 
                    (!string.IsNullOrWhiteSpace(item.CategoryName) ? item.CategoryName : 
                    (channelType == ChannelType.Live ? "Live" : channelType == ChannelType.Series ? "Series" : "VOD"));

            if (logCount < 5)
            {
                DiagnosticLog($"[BuildChannels] Item: {item.Name} | ID: {item.TvGenreId} | Category: {group}");
                logCount++;
            }

            channels.Add(new Channel
            {
                Name = string.IsNullOrWhiteSpace(item.Name) ? "İsimsiz Kanal" : item.Name.Trim(),
                StreamUrl = streamUrl,
                LogoUrl = NormalizeLogoUrl(item.Logo, baseUrl),
                GroupTitle = group,
                Type = channelType
            });
        }
        return channels;
    }

    // ─────────────────────────────────────────────────────────────
    //  HTTP & URL YARDIMCILARI
    // ─────────────────────────────────────────────────────────────

    private async Task<JsonElement> GetForJsAsync(string endpoint, string query, string macAddress, string? token, CancellationToken ct)
    {
        using var request = BuildGetRequest(endpoint, query, macAddress, token);
        using var response = await _httpClient.SendAsync(request, ct);
        
        // NetworkRetry kaldırıldı, fail-fast tercih ediliyor.
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Stalker Request Failed: {response.StatusCode}");
        
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.TryGetProperty("js", out var js) ? js : doc.RootElement).Clone();
    }

    private static HttpRequestMessage BuildGetRequest(string endpoint, string query, string macAddress, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{query}");
        request.Headers.TryAddWithoutValidation("Cookie", $"mac={macAddress}");
        request.Headers.TryAddWithoutValidation("X-User-Agent", "Model: MAG250; Link: WiFi");
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return request;
    }

    private static string NormalizeStreamCommand(string? cmd, string baseUrl, string token)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return string.Empty;
        var trimmed = cmd.Trim();
        var idx = trimmed.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return trimmed[idx..].Trim();

        var cleaned = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^(ffrt\d*|auto|ch)\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        if (cleaned.StartsWith('/') && !string.IsNullOrWhiteSpace(baseUrl)) return $"{baseUrl}{cleaned}";
        return cleaned;
    }

    private static string? NormalizeLogoUrl(string? logo, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(logo)) return null;
        if (logo.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return logo;
        return string.IsNullOrWhiteSpace(baseUrl) ? logo : $"{baseUrl}{(logo.StartsWith('/') ? "" : "/")}{logo}";
    }

    private static string ExtractBaseUrl(string endpoint)
    {
        try { var uri = new Uri(endpoint); return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}"; }
        catch { return ""; }
    }

    private static string BuildQueryString(Dictionary<string, string> parameters)
        => string.Join("&", parameters.Select(kv => $"{HttpUtility.UrlEncode(kv.Key)}={HttpUtility.UrlEncode(kv.Value)}"));

    private static string NormalizePortalUrl(string url)
    {
        var n = url.Trim();
        if (!n.StartsWith("http", StringComparison.OrdinalIgnoreCase)) n = $"http://{n}";
        return n.TrimEnd('/');
    }

    private static string? GetString(JsonElement e, string p) => e.TryGetProperty(p, out var prop) ? prop.ValueKind switch { JsonValueKind.String => prop.GetString(), JsonValueKind.Number => prop.GetRawText(), _ => null } : null;
    private static int? GetInt(JsonElement e, string p) => int.TryParse(GetString(e, p), out var v) ? v : null;
    private static string BuildTokenCacheKey(string url, string mac) => $"{url}|{mac.ToUpperInvariant()}";
    private static void InvalidateToken(string url, string mac) => TokenCache.TryRemove(BuildTokenCacheKey(url, mac), out _);

    private sealed class StalkerListItem
    {
        public string? Name { get; set; }
        public string? Cmd { get; set; }
        public string? Logo { get; set; }
        public string? TvGenreId { get; set; }
        public string? CategoryName { get; set; }
    }
    private readonly record struct CachedTokenState(string Token, DateTimeOffset ExpiresAt);
}
