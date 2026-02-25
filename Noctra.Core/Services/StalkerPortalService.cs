using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Stalker Middleware Portal API servisi.
/// Gerçek Stalker protokolü: GET istekleri + Cookie tabanlı MAC kimlik doğrulaması.
/// </summary>
public class StalkerPortalService : IStalkerPortalService
{
    private readonly HttpClient _httpClient;

    // Token cache — portal URL + MAC kombinasyonuna göre
    private static readonly ConcurrentDictionary<string, CachedTokenState> TokenCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TokenLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(10);

    // Stalker sunucularının olası endpoint path'leri (öncelik sırasıyla)
    private static readonly string[] KnownPortalPaths =
    [
        "/stalker_portal/server/load.php",
        "/server/load.php",
        "/c/",
        "/portal.php",
        "/stalker_portal/c/",
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
        var (normalizedUrl, endpoint, initialToken) = await ResolveEndpointAsync(portalUrl, macAddress, cancellationToken);
        if (endpoint == null) return false;

        var token = await GetOrCreateTokenAsync(normalizedUrl, endpoint, macAddress, initialToken, cancellationToken);
        return !string.IsNullOrWhiteSpace(token);
    }

    public async Task<List<Channel>> GetChannelsAsync(
        string portalUrl,
        string macAddress,
        bool includeVod = true,
        CancellationToken cancellationToken = default)
    {
        // 1. Endpoint'i bul
        var (normalizedUrl, endpoint, initialToken) = await ResolveEndpointAsync(portalUrl, macAddress, cancellationToken);
        if (endpoint == null)
        {
            throw new InvalidOperationException(
                "Stalker Portal endpoint bulunamadı. Sunucu URL'sini kontrol edin.");
        }

        // 2. Token al
        var token = await GetOrCreateTokenAsync(normalizedUrl, endpoint, macAddress, initialToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Stalker Portal handshake başarısız. MAC adresini kontrol edin.");
        }

        // 3. Kanalları yükle
        try
        {
            return await LoadChannelsWithTokenAsync(endpoint, token, macAddress, includeVod, cancellationToken);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Token süresi dolmuşsa yenile ve tekrar dene
            InvalidateToken(normalizedUrl, macAddress);
            var refreshedToken = await GetOrCreateTokenAsync(normalizedUrl, endpoint, macAddress, null, cancellationToken);
            if (string.IsNullOrWhiteSpace(refreshedToken))
                throw new InvalidOperationException("Stalker token yenilenemedi.");

            return await LoadChannelsWithTokenAsync(endpoint, refreshedToken, macAddress, includeVod, cancellationToken);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ENDPOINT KEŞFİ
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sunucunun gerçek endpoint path'ini keşfeder.
    /// Birden fazla bilinen path'i sırayla dener.
    /// </summary>
    private async Task<(string NormalizedUrl, string? Endpoint, string? InitialToken)> ResolveEndpointAsync(
        string portalUrl,
        string macAddress,
        CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizePortalUrl(portalUrl);

        // Bilinen path'leri sırayla dene (NormalizedUrl trailingslash'siz olduğu için path'ler / ile başlamalı)
        var pathsToTry = new List<string>(KnownPortalPaths);
        if (!pathsToTry.Contains("")) pathsToTry.Add(""); // Orijinal URL'yi de dene
        if (!pathsToTry.Contains("/")) pathsToTry.Add("/");

        // Bilinen path'leri sırayla dene
        foreach (var path in pathsToTry)
        {
            var candidate = $"{normalizedUrl}{path}";
            try
            {
                var testBody = BuildQueryString(new Dictionary<string, string>
                {
                    ["action"] = "handshake",
                    ["type"] = "stb",
                    ["token"] = "",
                    ["mac"] = macAddress
                });

                using var request = BuildGetRequest(candidate, testBody, macAddress, token: null);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5)); // Hızlı kontrol

                using var response = await _httpClient.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var trimmed = json.Trim();

                if (trimmed.StartsWith('{') && 
                    (json.Contains("\"js\"", StringComparison.OrdinalIgnoreCase) || 
                     json.Contains("\"result\"", StringComparison.OrdinalIgnoreCase)))
                {
                    System.Diagnostics.Debug.WriteLine($"[Stalker] Found endpoint: {candidate}");
                    
                    using var doc = JsonDocument.Parse(json);
                    var jsToken = GetString(doc.RootElement.TryGetProperty("js", out var jsEl) ? jsEl : doc.RootElement, "token");

                    return (normalizedUrl, candidate, jsToken);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Stalker] Endpoint probe failed for {candidate}: {ex.Message}");
            }
        }

        return (normalizedUrl, null, null);
    }

    // ─────────────────────────────────────────────────────────────
    //  TOKEN YÖNETİMİ
    // ─────────────────────────────────────────────────────────────

    private async Task<string> GetOrCreateTokenAsync(
        string normalizedPortalUrl,
        string endpoint,
        string macAddress,
        string? initialToken,
        CancellationToken cancellationToken)
    {
        var key = BuildTokenCacheKey(normalizedPortalUrl, macAddress);

        if (TokenCache.TryGetValue(key, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow &&
            !string.IsNullOrWhiteSpace(cached.Token))
        {
            return cached.Token;
        }

        var tokenLock = TokenLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check locking
            if (TokenCache.TryGetValue(key, out cached) &&
                cached.ExpiresAt > DateTimeOffset.UtcNow &&
                !string.IsNullOrWhiteSpace(cached.Token))
            {
                return cached.Token;
            }

            var token = initialToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                token = await HandshakeAsync(endpoint, macAddress, cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(token))
            {
                // get_profile — oturumu sunucuda başlatmak için zorunlu
                await GetProfileAsync(endpoint, token, macAddress, cancellationToken);
                TokenCache[key] = new CachedTokenState(token, DateTimeOffset.UtcNow.Add(TokenTtl));
            }

            return token;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private static string BuildTokenCacheKey(string normalizedPortalUrl, string macAddress)
        => $"{normalizedPortalUrl}|{macAddress.Trim().ToUpperInvariant()}";

    private static void InvalidateToken(string normalizedPortalUrl, string macAddress)
        => TokenCache.TryRemove(BuildTokenCacheKey(normalizedPortalUrl, macAddress), out _);

    // ─────────────────────────────────────────────────────────────
    //  STALKER API ÇAĞRILARI
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Handshake — token alır.
    /// </summary>
    private async Task<string> HandshakeAsync(
        string endpoint,
        string macAddress,
        CancellationToken cancellationToken)
    {
        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["action"] = "handshake",
            ["type"] = "stb",
            ["token"] = "",
            ["mac"] = macAddress
        });

        var js = await GetForJsAsync(endpoint, query, macAddress, token: null, cancellationToken);
        return GetString(js, "token") ?? string.Empty;
    }

    /// <summary>
    /// get_profile — handshake sonrası STB oturumunu sunucuda başlatır.
    /// Bu adım atlanırsa çoğu sunucu içerik listesi döndürmez.
    /// </summary>
    private async Task GetProfileAsync(
        string endpoint,
        string token,
        string macAddress,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = BuildQueryString(new Dictionary<string, string>
            {
                ["action"] = "get_profile",
                ["type"] = "stb",
                ["token"] = token,
                ["mac"] = macAddress,
                ["hd"] = "1",
                ["num_banks"] = "1",
                ["sn"] = "00000000000000",
                ["stb_type"] = "MAG250",
                ["image_version"] = "218"
            });

            await GetForJsAsync(endpoint, query, macAddress, token, cancellationToken);
        }
        catch (Exception ex)
        {
            // get_profile bazı sunucularda yoktur — hata olsa da devam et
            System.Diagnostics.Debug.WriteLine($"[Stalker] get_profile failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Tür genrelerini (kategori listesi) çeker.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> GetGenreMapAsync(
        string endpoint,
        string token,
        string macAddress,
        string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = BuildQueryString(new Dictionary<string, string>
            {
                ["action"] = "get_genres",
                ["type"] = contentType,
                ["token"] = token
            });

            var js = await GetForJsAsync(endpoint, query, macAddress, token, cancellationToken);
            if (js.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, string>();

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in js.EnumerateArray())
            {
                var id = GetString(element, "id");
                var title = GetString(element, "title") ?? GetString(element, "name");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
                    map[id] = title;
            }

            return map;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Stalker] GetGenreMap failed for {contentType}: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Tüm sayfaları okuyarak kanal listesini çeker.
    /// </summary>
    private async Task<List<StalkerListItem>> GetOrderedListAllPagesAsync(
        string endpoint,
        string token,
        string macAddress,
        string listType,
        CancellationToken cancellationToken)
    {
        var firstPageResult = await GetPageAsync(endpoint, token, macAddress, listType, 1, cancellationToken);
        
        if (firstPageResult.Items.Count == 0)
            return new List<StalkerListItem>();

        int totalPages = 1;
        if (firstPageResult.TotalItems.HasValue && firstPageResult.MaxPageItems.HasValue && firstPageResult.MaxPageItems.Value > 0)
        {
            totalPages = (int)Math.Ceiling(firstPageResult.TotalItems.Value / (double)firstPageResult.MaxPageItems.Value);
        }

        if (totalPages <= 1)
            return firstPageResult.Items;

        // Sayfa sırasını korumak için dizi kullanıyoruz
        var allPages = new List<StalkerListItem>[totalPages];
        allPages[0] = firstPageResult.Items;

        var semaphore = new SemaphoreSlim(8);
        var tasks = new List<Task>();

        for (int p = 2; p <= totalPages; p++)
        {
            var pageNum = p;
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var result = await GetPageAsync(endpoint, token, macAddress, listType, pageNum, cancellationToken);
                    allPages[pageNum - 1] = result.Items;
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);

        // Tüm sayfaları tek bir listede birleştir
        var resultList = new List<StalkerListItem>(firstPageResult.TotalItems ?? totalPages * 14);
        foreach (var pageItems in allPages)
        {
            if (pageItems != null)
                resultList.AddRange(pageItems);
        }

        System.Diagnostics.Debug.WriteLine($"[Stalker] Total {listType} items fetched in parallel: {resultList.Count}");
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
        var items = new List<StalkerListItem>();
        var queryParams = new Dictionary<string, string>
        {
            ["action"] = "get_ordered_list",
            ["type"] = listType,
            ["sortby"] = listType == "itv" ? "number" : "added",
            ["p"] = page.ToString(),
            ["token"] = token
        };

        if (listType == "itv")
            queryParams["genre"] = "*";
        else
            queryParams["category"] = "*";

        var query = BuildQueryString(queryParams);
        JsonElement js;

        try
        {
            js = await GetForJsAsync(endpoint, query, macAddress, token, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Stalker] GetOrderedList page {page} failed: {ex.Message}");
            return (items, null, null);
        }

        var totalItems = GetInt(js, "total_items");
        var maxPageItems = GetInt(js, "max_page_items");

        if (js.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataEl.EnumerateArray())
            {
                items.Add(new StalkerListItem
                {
                    Name = GetString(item, "name"),
                    Cmd = GetString(item, "cmd"),
                    Logo = GetString(item, "logo"),
                    TvGenreId = GetString(item, "tv_genre_id") ?? GetString(item, "category_id")
                });
            }
        }

        return (items, totalItems, maxPageItems);
    }

    // ─────────────────────────────────────────────────────────────
    //  KANAL OLUŞTURMA — create_link OLMADAN
    // ─────────────────────────────────────────────────────────────

    private async Task<List<Channel>> LoadChannelsWithTokenAsync(
        string endpoint,
        string token,
        string macAddress,
        bool includeVod,
        CancellationToken cancellationToken)
    {
        var liveGenreMap = await GetGenreMapAsync(endpoint, token, macAddress, "itv", cancellationToken);
        var vodGenreMap = includeVod
            ? await GetGenreMapAsync(endpoint, token, macAddress, "vod", cancellationToken)
            : new Dictionary<string, string>();

        var channels = new List<Channel>();

        // Canlı kanallar
        var liveItems = await GetOrderedListAllPagesAsync(endpoint, token, macAddress, "itv", cancellationToken);
        channels.AddRange(BuildChannels(liveItems, endpoint, token, ChannelType.Live, liveGenreMap));

        // VOD
        if (includeVod)
        {
            var vodItems = await GetOrderedListAllPagesAsync(endpoint, token, macAddress, "vod", cancellationToken);
            channels.AddRange(BuildChannels(vodItems, endpoint, token, ChannelType.VOD, vodGenreMap));
        }

        System.Diagnostics.Debug.WriteLine($"[Stalker] Total channels built: {channels.Count}");
        return channels;
    }

    /// <summary>
    /// Kanal listesi oluşturur.
    /// 
    /// ÖNEMLİ: create_link her kanal için çağrılmıyor.
    /// Bunun yerine cmd URL'si doğrudan NormalizeStreamCommand ile kullanılıyor.
    /// 
    /// Neden? 
    /// - Binlerce kanal için binlerce HTTP isteği = dakikalarca bekleme + rate-limit
    /// - Stalker'ın cmd alanı zaten oynatılabilir URL içeriyor
    /// - create_link sadece çok eski MAG cihazlarda bazı yönlendirmeler için gerekli
    /// - Modern uygulamaların tümü (TiviMate dahil) cmd'yi direkt kullanır
    /// </summary>
    private static List<Channel> BuildChannels(
        IReadOnlyCollection<StalkerListItem> items,
        string endpoint,
        string token,
        ChannelType channelType,
        IReadOnlyDictionary<string, string> genreMap)
    {
        if (items.Count == 0) return [];

        // Endpoint'ten base URL'yi çıkar (create_link fallback için)
        var baseUrl = ExtractBaseUrl(endpoint);

        var channels = new List<Channel>(items.Count);
        var skippedCount = 0;

        foreach (var item in items)
        {
            var streamUrl = NormalizeStreamCommand(item.Cmd, baseUrl, token);
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                skippedCount++;
                System.Diagnostics.Debug.WriteLine(
                    $"[Stalker] Skipped channel '{item.Name}' — empty/unparseable cmd: '{item.Cmd}'");
                continue;
            }

            channels.Add(new Channel
            {
                Name = string.IsNullOrWhiteSpace(item.Name) ? "İsimsiz Kanal" : item.Name.Trim(),
                StreamUrl = streamUrl,
                LogoUrl = NormalizeLogoUrl(item.Logo, baseUrl),
                GroupTitle = ResolveGenre(item.TvGenreId, genreMap, channelType),
                Type = channelType
            });
        }

        if (skippedCount > 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Stalker] Skipped {skippedCount}/{items.Count} channels with empty stream URLs");
        }

        return channels;
    }

    // ─────────────────────────────────────────────────────────────
    //  HTTP YARDIMCILARI — GET + Cookie Auth (Gerçek Stalker Protokolü)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stalker Portal API'ye GET isteği gönderir ve "js" alanını döndürür.
    /// 
    /// Stalker protokolü GET + Cookie header kullanır:
    ///   Cookie: mac={MAC}; stb_lang=en; timezone=Europe/Istanbul
    ///   Authorization: Bearer {token}  (bazı sunucular için)
    /// </summary>
    private async Task<JsonElement> GetForJsAsync(
        string endpoint,
        string queryString,
        string macAddress,
        string? token,
        CancellationToken cancellationToken)
    {
        var fullUrl = $"{endpoint}?{queryString}";

        var json = await NetworkRetry.ExecuteAsync(async () =>
        {
            using var request = BuildGetRequest(endpoint, queryString, macAddress, token);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }, cancellationToken: cancellationToken);

        System.Diagnostics.Debug.WriteLine($"[Stalker] Request: {queryString} -> Response length: {json.Length}");
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("js", out var js))
        {
            // Bazı sunucular doğrudan array veya object döndürüyor
            // js alanı yoksa root'u kullan
            System.Diagnostics.Debug.WriteLine(
                $"[Stalker] Warning: 'js' field not found in response for: {fullUrl}");
            return doc.RootElement.Clone();
        }

        return js.Clone();
    }

    /// <summary>
    /// Doğru header'larla GET isteği oluşturur.
    /// </summary>
    private static HttpRequestMessage BuildGetRequest(
        string endpoint,
        string queryString,
        string macAddress,
        string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{queryString}");

        // Stalker protokolü: MAC adresi Cookie'de gönderilir
        request.Headers.TryAddWithoutValidation("Cookie", $"mac={macAddress}");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        // MAG STB kimliği taklit etmek için gerekli
        request.Headers.TryAddWithoutValidation("X-User-Agent", "Model: MAG250; Link: WiFi");
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3");

        // Token varsa Authorization header'ı da ekle (bazı modern sunucular için)
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return request;
    }

    // ─────────────────────────────────────────────────────────────
    //  URL & STREAM NORMALIZASYONU
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stalker'dan gelen cmd değerini oynatılabilir URL'ye dönüştürür.
    /// 
    /// cmd örnekleri:
    ///   "ffrt http://server:8080/live/user/pass/12345.ts"
    ///   "ffrt1 http://server/stream"  
    ///   "http://server:8080/live/user/pass/12345.ts"
    ///   "/live/user/pass/12345.ts"   (relative URL)
    ///   "auto /live/user/pass/12345.ts"
    /// </summary>
    private static string NormalizeStreamCommand(string? cmd, string baseUrl, string token)
    {
        if (string.IsNullOrWhiteSpace(cmd))
            return string.Empty;

        var trimmed = cmd.Trim();

        // Direkt http/https URL içeriyorsa çıkar
        var httpIdx = trimmed.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (httpIdx < 0)
            httpIdx = trimmed.IndexOf("https://", StringComparison.OrdinalIgnoreCase);

        if (httpIdx >= 0)
            return trimmed[httpIdx..].Trim();

        // "ffrt", "ffrt1", "auto" gibi prefix'leri temizle
        var cleaned = System.Text.RegularExpressions.Regex
            .Replace(trimmed, @"^(ffrt\d*|auto|ch)\s*", string.Empty, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Trim();

        // Relative URL ise base URL ile birleştir
        if (cleaned.StartsWith('/') && !string.IsNullOrWhiteSpace(baseUrl))
            return $"{baseUrl}{cleaned}";

        // Eğer hâlâ http ile başlamıyorsa ve içerik varsa base URL ekle
        if (!string.IsNullOrWhiteSpace(cleaned) && !cleaned.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
                return $"{baseUrl}/{cleaned.TrimStart('/')}";
        }

        return cleaned;
    }

    private static string? NormalizeLogoUrl(string? logo, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(logo)) return null;
        if (logo.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return logo;
        if (logo.StartsWith('/') && !string.IsNullOrWhiteSpace(baseUrl))
            return $"{baseUrl}{logo}";
        return logo;
    }

    private static string ExtractBaseUrl(string endpoint)
    {
        try
        {
            var uri = new Uri(endpoint);
            return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveGenre(
        string? genreId,
        IReadOnlyDictionary<string, string> genreMap,
        ChannelType type)
    {
        if (!string.IsNullOrWhiteSpace(genreId) && genreMap.TryGetValue(genreId, out var name))
            return name;

        return type == ChannelType.Live ? "Live" : "VOD";
    }

    // ─────────────────────────────────────────────────────────────
    //  YARDIMCI METODLAR
    // ─────────────────────────────────────────────────────────────

    private static string BuildQueryString(Dictionary<string, string> parameters)
    {
        var pairs = parameters
            .Where(kv => kv.Value != null)
            .Select(kv => $"{HttpUtility.UrlEncode(kv.Key)}={HttpUtility.UrlEncode(kv.Value)}");
        return string.Join("&", pairs);
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
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        var raw = GetString(element, propertyName);
        return int.TryParse(raw, out var value) ? value : null;
    }

    // ─────────────────────────────────────────────────────────────
    //  İÇ SINIFLAR
    // ─────────────────────────────────────────────────────────────

    private sealed class StalkerListItem
    {
        public string? Name     { get; set; }
        public string? Cmd      { get; set; }
        public string? Logo     { get; set; }
        public string? TvGenreId { get; set; }
    }

    private readonly record struct CachedTokenState(string Token, DateTimeOffset ExpiresAt);
}
