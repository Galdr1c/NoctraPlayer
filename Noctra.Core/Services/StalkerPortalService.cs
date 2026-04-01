using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Stalker Middleware Portal API servisi — Kategori-önce aşamalı yükleme.
///
/// PERFORMANS STRATEJİSİ:
///   Eski: Tüm 230k kanalı çek → 10-15 dakika
///   Yeni: Kategori listesi (~500ms) → Her kategorinin kanallarını paralel çek
///         → Her kategori bitince DB'ye yaz → UI anlık güncellenir
///         → Kullanıcı ~3-5 saniyede uygulamayı kullanmaya başlayabilir
/// </summary>
public class StalkerPortalService : IStalkerPortalService
{
    private readonly HttpClient _httpClient;

    private static readonly ConcurrentDictionary<string, CachedTokenState>    TokenCache  = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>       TokenLocks  = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CachedEndpointState> EndpointCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _dummy = new(); // Not used but keeps structure
    private static int _cleanupCounter;
    private static readonly TimeSpan TokenTtl    = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan EndpointTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

    private static readonly string[] KnownPortalPaths =
    [
        "/stalker_portal/server/load.php",
        "/stalker_portal/c/",
        "/server/load.php",
        "/c/",
        "/portal.php",
    ];

    public StalkerPortalService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // ═══════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════

    public async Task<bool> AuthenticateAsync(
        string portalUrl, string macAddress, CancellationToken cancellationToken = default)
    {
        var (normalizedUrl, endpoint, initialToken) =
            await ResolveEndpointParallelAsync(portalUrl, macAddress, cancellationToken);
        if (endpoint == null) return false;
        var token = await GetOrCreateTokenAsync(
            normalizedUrl, endpoint, macAddress, initialToken, cancellationToken);
        return !string.IsNullOrWhiteSpace(token);
    }

    /// <summary>
    /// Geriye dönük uyum — yeni kodlar GetChannelsProgressiveAsync kullanmalı.
    /// </summary>
    public string GetEpgUrl(string portalUrl)
    {
        var normalized = NormalizePortalUrl(portalUrl);
        return $"{normalized}/itv/xmltv.php";
    }

    public async Task<List<Channel>> GetChannelsAsync(
        string portalUrl, string macAddress, bool includeVod = true,
        CancellationToken cancellationToken = default)
    {
        var all = new List<Channel>();
        await GetChannelsProgressiveAsync(
            portalUrl, macAddress, includeVod,
            onCategoriesDiscovered: (cats, _) => Task.FromResult(cats),
            onCategoryLoaded: (channels, _) => { all.AddRange(channels); return Task.CompletedTask; },
            progress: null,
            cancellationToken: cancellationToken);
        return all;
    }

    // ─────────────────────────────────────────────────────────────
    //  YENİ: Kategori listesi — hızlı (~500ms)
    // ─────────────────────────────────────────────────────────────

    public async Task<List<StalkerCategory>> GetCategoriesAsync(
        string portalUrl, string macAddress, CancellationToken cancellationToken = default)
    {
        var (normalizedUrl, endpoint, initialToken) =
            await ResolveEndpointParallelAsync(portalUrl, macAddress, cancellationToken);
        if (endpoint == null) return [];

        var token = await GetOrCreateTokenAsync(
            normalizedUrl, endpoint, macAddress, initialToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return [];

        // 3 kategori tipini paralel çek
        var tasks = new[]
        {
            FetchCategoriesAsync(endpoint, token, macAddress, "itv",    cancellationToken),
            FetchCategoriesAsync(endpoint, token, macAddress, "vod",    cancellationToken),
            FetchCategoriesAsync(endpoint, token, macAddress, "series", cancellationToken),
        };
        await Task.WhenAll(tasks);

        var result = new List<StalkerCategory>();
        foreach (var t in tasks)
            result.AddRange(t.Result);

        Log($"GetCategoriesAsync: {result.Count} categories found in total");
        return result;
    }

    // ─────────────────────────────────────────────────────────────
    //  YENİ: Tek kategori kanalları — lazy loading için
    // ─────────────────────────────────────────────────────────────

    public async Task<List<Channel>> GetChannelsByCategoryAsync(
        string portalUrl, string macAddress,
        string categoryId, string categoryType,
        CancellationToken cancellationToken = default)
    {
        var (normalizedUrl, endpoint, initialToken) =
            await ResolveEndpointParallelAsync(portalUrl, macAddress, cancellationToken);
        if (endpoint == null) return [];

        var token = await GetOrCreateTokenAsync(
            normalizedUrl, endpoint, macAddress, initialToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return [];

        var genreMap = await FetchCategoriesAsync(
            endpoint, token, macAddress, categoryType, cancellationToken)
            .ContinueWith(t => (IReadOnlyDictionary<string, string>)
                t.Result.ToDictionary(c => c.Id, c => c.Name));

        var items = await GetAllPagesForCategoryAsync(
            endpoint, token, macAddress, categoryType, categoryId, cancellationToken);

        var baseUrl  = ExtractBaseUrl(endpoint);
        var chanType = categoryType == "itv" ? ChannelType.Live
                     : categoryType == "series" ? ChannelType.Series
                     : ChannelType.VOD;

        return BuildChannels(items, baseUrl, chanType, genreMap);
    }

    // ─────────────────────────────────────────────────────────────
    //  YENİ: Aşamalı yükleme — 230k hesabın çözümü
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Kategori-önce yükleme stratejisi:
    /// 1. Tüm kategori listelerini paralel çek (~500ms)
    /// 2. Her kategorinin kanallarını paralel çek (MAX_CAT_PARALLEL kategorisi eş zamanlı)
    /// 3. Her kategori bittiğinde onCategoryLoaded çağrılır
    ///    → Çağıran kod DB'ye yazabilir, UI güncellenebilir
    ///
    /// Sonuç: Kullanıcı ~3-5 saniyede ilk içerikleri görür,
    ///        tüm içerik arka planda yüklenmeye devam eder.
    /// </summary>
    public async Task GetChannelsProgressiveAsync(
        string portalUrl,
        string macAddress,
        bool includeVod,
        Func<List<StalkerCategory>, Action<string>, Task<List<StalkerCategory>>> onCategoriesDiscovered,
        Func<List<Channel>, StalkerCategory, Task> onCategoryLoaded,
        IProgress<StalkerLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var normalizedPortalUrl = NormalizePortalUrl(portalUrl);
        if (string.IsNullOrWhiteSpace(normalizedPortalUrl))
            throw new InvalidOperationException("Geçersiz veya boş bir Stalker Portal adresi girdiniz.");

        if (string.IsNullOrWhiteSpace(macAddress))
            throw new InvalidOperationException("MAC adresi boş olamaz.");

        var (normalizedUrl, endpoint, initialToken) =
            await ResolveEndpointParallelAsync(normalizedPortalUrl, macAddress, cancellationToken);
        if (endpoint == null)
            throw new InvalidOperationException("Stalker Portal endpoint bulunamadı. URL veya MAC adresini kontrol edin.");

        var token = await GetOrCreateTokenAsync(
            normalizedUrl, endpoint, macAddress, initialToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Stalker Portal handshake başarısız. MAC adresini kontrol edin.");

        Log($"Auth ready in {sw.ElapsedMilliseconds}ms");

        // ── Adım 1: Tüm kategorileri hızlıca çek ──────────────────
        var typesToFetch = includeVod
            ? new[] { "itv", "vod", "series" }
            : new[] { "itv" };

        var categoryTasks = typesToFetch.Select(t =>
            FetchCategoriesAsync(endpoint, token, macAddress, t, cancellationToken)).ToArray();
        await Task.WhenAll(categoryTasks);

        // Önce Live TV, sonra VOD, sonra Series — en çok kullanılan önce
        var allCategories = new List<StalkerCategory>();
        foreach (var t in categoryTasks)
            allCategories.AddRange(t.Result);

        var totalCategories   = allCategories.Count;
        var loadedCategories  = 0;
        var totalChannelCount = 0;

        Log($"Categories fetched: {totalCategories} in {sw.ElapsedMilliseconds}ms");

        // Görev kuyruğu ve önceliklendirme mekanizması (Lazy Loading anında tıklananı öne almak için)
        var pendingCategories = new List<StalkerCategory>(allCategories);
        var queueLock = new object();

        Action<string> prioritizeAction = (categoryName) =>
        {
            lock (queueLock)
            {
                var idx = pendingCategories.FindIndex(c => c.Name == categoryName);
                if (idx > 0)
                {
                    var cat = pendingCategories[idx];
                    pendingCategories.RemoveAt(idx);
                    pendingCategories.Insert(0, cat); // En öne al
                    Log($"[Prioritized] Kategori öne alındı: {categoryName}");
                }
            }
        };

        // --- Yeni Özellik: Arayüzün anında dolması için keşfedilen kategorileri bildir ---
        if (totalCategories > 0)
        {
            var filteredCategories = await onCategoriesDiscovered(allCategories, prioritizeAction);
            pendingCategories = new List<StalkerCategory>(filteredCategories);
        }

        if (totalCategories == 0)
        {
            Log("No categories found — falling back to genre=* fetch");
            // Fallback: eski yöntem
            var fallback = await LoadAllWithGenreStarAsync(
                endpoint, token, macAddress, includeVod, cancellationToken);
            var fakeCategory = new StalkerCategory
                { Id = "*", Name = "Tüm İçerikler", Type = "itv" };
            var filteredFallback = await onCategoriesDiscovered(new List<StalkerCategory> { fakeCategory }, prioritizeAction);
            if (filteredFallback.Count > 0)
            {
                await onCategoryLoaded(fallback, fakeCategory);
            }
            return;
        }

        // ── Adım 2: Her kategoriyi paralel çek (Kuyruktan tüketerek) ────────────────────
        // Stalker sunucuları 5-8 eş zamanlı isteği genellikle kaldırır
        const int MaxCategoryParallel = 5;
        var baseUrl = ExtractBaseUrl(endpoint);

        // Kategori türüne göre ChannelType belirle
        ChannelType GetChanType(string t) => t switch
        {
            "itv"    => ChannelType.Live,
            "series" => ChannelType.Series,
            _        => ChannelType.VOD
        };

        var workerTasks = Enumerable.Range(0, MaxCategoryParallel).Select(async workerId =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                StalkerCategory? category = null;

                lock (queueLock)
                {
                    if (pendingCategories.Count > 0)
                    {
                        category = pendingCategories[0];
                        pendingCategories.RemoveAt(0);
                    }
                }

                // Kuyruk bittiyse worker sonlanır
                if (category == null) break;

                try
                {
                    Log($"[Worker] Loading category: {category.Name} ({category.Type})");
                    var items = await GetAllPagesForCategoryAsync(
                        endpoint, token, macAddress,
                        category.Type, category.Id, cancellationToken);

                    // If zero items, call onCategoryLoaded anyway to clear the dummy channel from UI
                    if (items.Count == 0)
                    {
                        var loaded = Interlocked.Increment(ref loadedCategories);
                        progress?.Report(new StalkerLoadProgress
                        {
                            CurrentCategory = category.Name,
                            LoadedCategories = loaded,
                            TotalCategories = totalCategories,
                            LoadedChannels = totalChannelCount,
                            TotalChannels = null
                        });

                        await onCategoryLoaded([], category);
                    }
                    else
                    {
                        var channels = BuildChannels(
                            items, baseUrl, GetChanType(category.Type),
                            new Dictionary<string, string> { [category.Id] = category.Name });

                        var loaded = Interlocked.Increment(ref loadedCategories);
                        var total  = Interlocked.Add(ref totalChannelCount, channels.Count);

                        progress?.Report(new StalkerLoadProgress
                        {
                            CurrentCategory = category.Name,
                            LoadedCategories = loaded,
                            TotalCategories = totalCategories,
                            LoadedChannels = total, 
                            TotalChannels = null 
                        });

                        await onCategoryLoaded(channels, category);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref loadedCategories);
                    Log($"[Error] Category {category.Name} (ID: {category.Id}, Type: {category.Type}) failed: {ex.Message}");
                    
                    // Hata durumunda da boş liste bildir ki UI'daki "yükleniyor..." uyarısı kalksın
                    await onCategoryLoaded([], category);
                }
            }
        });

        await Task.WhenAll(workerTasks);

        Log($"GetChannelsProgressiveAsync DONE: {totalChannelCount} channels, " +
            $"{totalCategories} categories, {sw.ElapsedMilliseconds}ms total");
    }

    // ═══════════════════════════════════════════════════════════
    //  ENDPOINT KEŞFİ — PARALEL
    // ═══════════════════════════════════════════════════════════

    private async Task<(string NormalizedUrl, string? Endpoint, string? InitialToken)>
        ResolveEndpointParallelAsync(
            string portalUrl, string macAddress, CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizePortalUrl(portalUrl);
        var cacheKey      = $"{normalizedUrl}|{macAddress.Trim().ToUpperInvariant()}";

        // Endpoint cache — 24 saat geçerli
        if (EndpointCache.TryGetValue(cacheKey, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            Log($"Endpoint from cache: {cached.Endpoint}");
            return (normalizedUrl, cached.Endpoint, null);
        }

        // Eğer kullanıcı URL'nin sonuna /c/ vb. eklediyse, temizleyip ana domain'i (base URL) bul
        var baseUrl = normalizedUrl;
        foreach (var knownPath in KnownPortalPaths)
        {
            var p = knownPath.TrimEnd('/');
            if (!string.IsNullOrEmpty(p) && baseUrl.EndsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - p.Length).TrimEnd('/');
                break;
            }
        }

        var handshakeQuery = BuildQueryString(new Dictionary<string, string>
        {
            ["action"] = "handshake",
            ["type"]   = "stb",
            ["token"]  = "",
            ["mac"]    = macAddress
        });

        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(ProbeTimeout);

        var candidates = KnownPortalPaths.Select(p => $"{baseUrl}{p}").ToList();
        if (!candidates.Contains(baseUrl)) candidates.Add(baseUrl);

        var results = new ConcurrentBag<(string Endpoint, string? Token, int Priority)>();

        var tasks = candidates.Select((c, idx) => Task.Run(async () =>
        {
            try
            {
                using var req  = BuildGetRequest(c, handshakeQuery, macAddress, null);
                using var resp = await _httpClient.SendAsync(
                    req, HttpCompletionOption.ResponseContentRead, probeCts.Token);
                if (!resp.IsSuccessStatusCode) return;

                var json    = await resp.Content.ReadAsStringAsync(probeCts.Token);
                var trimmed = json.Trim();
                if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[')) return;

                if (!json.Contains("\"js\"",     StringComparison.OrdinalIgnoreCase) &&
                    !json.Contains("\"result\"", StringComparison.OrdinalIgnoreCase) &&
                    !json.Contains("\"token\"",  StringComparison.OrdinalIgnoreCase))
                    return;

                string? token = null;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    token = root.TryGetProperty("js", out var jsEl)
                        ? GetString(jsEl, "token")
                        : GetString(root, "token");
                }
                catch { /* token parse hatası — endpoint yine de geçerli */ }

                results.Add((c, token, idx));
                Log($"Probe OK: {c}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log($"Probe failed: {c} — {ex.Message}"); }
        }, probeCts.Token));

        await Task.WhenAll(tasks);

        if (results.IsEmpty)
        {
            Log($"No endpoint found for: {normalizedUrl}");
            return (normalizedUrl, null, null);
        }

        var best = results.OrderBy(r => r.Priority).First();
        EndpointCache[cacheKey] = new CachedEndpointState(best.Endpoint,
            DateTimeOffset.UtcNow.Add(EndpointTtl));
        return (normalizedUrl, best.Endpoint, best.Token);
    }

    // ═══════════════════════════════════════════════════════════
    //  TOKEN YÖNETİMİ
    // ═══════════════════════════════════════════════════════════

    private async Task<string> GetOrCreateTokenAsync(
        string normalizedPortalUrl, string endpoint, string macAddress,
        string? initialToken, CancellationToken cancellationToken)
    {
        var key = $"{normalizedPortalUrl}|{macAddress.Trim().ToUpperInvariant()}";

        if (TokenCache.TryGetValue(key, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow &&
            !string.IsNullOrWhiteSpace(cached.Token))
            return cached.Token;

        // Periyodik temizleme (her 50 talepte bir)
        if (Interlocked.Increment(ref _cleanupCounter) % 50 == 0)
        {
            _ = Task.Run(CleanupExpiredTokens);
        }

        var tokenLock = TokenLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (TokenCache.TryGetValue(key, out cached) &&
                cached.ExpiresAt > DateTimeOffset.UtcNow &&
                !string.IsNullOrWhiteSpace(cached.Token))
                return cached.Token;

            var token = initialToken;
            if (string.IsNullOrWhiteSpace(token))
                token = await HandshakeAsync(endpoint, macAddress, cancellationToken);

            if (!string.IsNullOrWhiteSpace(token))
            {
                _ = GetProfileAsync(endpoint, token, macAddress, cancellationToken)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted) Log($"get_profile failed (non-fatal): {t.Exception?.InnerException?.Message}");
                    }, TaskScheduler.Default);

                TokenCache[key] = new CachedTokenState(token, DateTimeOffset.UtcNow.Add(TokenTtl));
            }

            return token ?? string.Empty;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private static void InvalidateToken(string normalizedPortalUrl, string macAddress)
    {
        var key = $"{normalizedPortalUrl}|{macAddress.Trim().ToUpperInvariant()}";
        TokenCache.TryRemove(key, out _);
        
        // Lock'ı da temizle - Dispose riski nedeniyle sadece dictionary'den kaldırıyoruz
        // Aktif kullanımda olma ihtimaline karşı Dispose() çağrılmıyor, GC'ye bırakılıyor
        TokenLocks.TryRemove(key, out _);
    }

    /// <summary>
    /// Eski ve süresi dolmuş tokenlar ile lock nesnelerini temizler.
    /// </summary>
    private static void CleanupExpiredTokens()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var expiredKeys = TokenCache
                .Where(kvp => kvp.Value.ExpiresAt < now.AddMinutes(-5)) // 5 dk tolerans
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                TokenCache.TryRemove(key, out _);
                
                if (TokenLocks.TryRemove(key, out var sem))
                {
                    // Güvenli Dispose: Eğer kilit şu an kullanılmıyorsa (CurrentCount == 1) Dispose et.
                    // Nadir durumlarda WaitAsync bekleyen bir thread varsa ObjectDisposedException
                    // oluşmaması için try-catch içinde tutuyoruz.
                    try
                    {
                        if (sem.CurrentCount == 1)
                        {
                            sem.Dispose();
                        }
                    }
                    catch { /* Yutulabilir */ }
                }
            }

            if (expiredKeys.Count > 0)
            {
                Log($"CleanupExpiredTokens: {expiredKeys.Count} expired tokens and locks purged.");
            }
        }
        catch (Exception ex)
        {
            Log($"CleanupExpiredTokens failed: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  STALKER API ÇAĞRILARI
    // ═══════════════════════════════════════════════════════════

    private async Task<string> HandshakeAsync(
        string endpoint, string macAddress, CancellationToken ct)
    {
        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["action"] = "handshake", ["type"] = "stb", ["token"] = "", ["mac"] = macAddress
        });
        var js = await GetForJsAsync(endpoint, query, macAddress, null, ct);
        return GetString(js, "token") ?? string.Empty;
    }

    private async Task GetProfileAsync(
        string endpoint, string token, string macAddress, CancellationToken ct)
    {
        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["action"] = "get_profile", ["type"] = "stb", ["token"] = token,
            ["mac"] = macAddress, ["hd"] = "1", ["num_banks"] = "1",
            ["sn"] = "00000000000000", ["stb_type"] = "MAG250", ["image_version"] = "218"
        });
        await GetForJsAsync(endpoint, query, macAddress, token, ct);
    }

    private async Task<List<StalkerCategory>> FetchCategoriesAsync(
        string endpoint, string token, string macAddress,
        string contentType, CancellationToken ct)
    {
        var categories = await FetchCategoriesInternalAsync(endpoint, token, macAddress, contentType, ct);
        
        // Fallback: Eğer categories boş gelirse ve series'se ordered list dene
        if (categories.Count == 0 && contentType == "series")
        {
            Log($"[Fallback] {contentType} categories empty, trying ordered_list approach");
            return await FetchSeriesCategoriesFallbackAsync(endpoint, token, macAddress, ct);
        }
        
        return categories;
    }

    private async Task<List<StalkerCategory>> FetchSeriesCategoriesFallbackAsync(
        string endpoint, string token, string macAddress, CancellationToken ct)
    {
        // get_ordered_list ile type=series&category=* kullanarak en azından tüm dizileri çekmeyi deniyoruz
        // Bazı sağlayıcılarda kategoriler gelmese bile bu yöntemle tüm listeye ulaşılabiliyor.
        return new List<StalkerCategory>
        {
            new StalkerCategory
            {
                Id = "*",
                Name = "Tüm Diziler",
                Type = "series",
                Count = 0
            }
        };
    }

    /// <summary>
    /// Bir içerik tipinin tüm kategorilerini çeker (İç Mantık).
    /// ITV için "get_genres", VOD/Series için "get_categories".
    /// </summary>
    private async Task<List<StalkerCategory>> FetchCategoriesInternalAsync(
        string endpoint, string token, string macAddress,
        string contentType, CancellationToken ct)
    {
        try
        {
            // ITV genres, VOD/Series categories — Stalker API farklı action kullanıyor
            var action = contentType == "itv" ? "get_genres" : "get_categories";
            var query  = BuildQueryString(new Dictionary<string, string>
            {
                ["action"] = action,
                ["type"]   = contentType,
                ["token"]  = token
            });

            var js = await GetForJsAsync(endpoint, query, macAddress, token, ct);
            if (js.ValueKind != JsonValueKind.Array) return [];

            var categories = new List<StalkerCategory>();
            foreach (var el in js.EnumerateArray())
            {
                var id   = GetString(el, "id")       ?? GetString(el, "category_id") ?? GetString(el, "genre_id");
                var name = GetString(el, "title")    ?? GetString(el, "name");
                var cnt  = GetInt(el, "censors")     ?? GetInt(el, "count");

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                if (id == "*") continue; // "Tümü" kategorisi — zaten ayrı ayrı çekeceğiz

                categories.Add(new StalkerCategory
                {
                    Id   = id,
                    Name = name.Trim(),
                    Type = contentType,
                    Count = cnt
                });
            }

            Log($"FetchCategories({contentType}): {categories.Count} categories");
            return categories;
        }
        catch (Exception ex)
        {
            Log($"FetchCategories({contentType}) failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Belirli bir kategorinin TÜM sayfalarını çeker.
    /// Sayfa sayısı küçük olduğundan (~5-20 sayfa) hızlı çalışır.
    /// </summary>
    private async Task<List<StalkerListItem>> GetAllPagesForCategoryAsync(
        string endpoint, string token, string macAddress,
        string listType, string categoryId, CancellationToken ct)
    {
        // İlk sayfayı çek — sayfa sayısını öğren
        var (firstItems, totalItems, maxPageItems) =
            await GetPageAsync(endpoint, token, macAddress, listType, categoryId, 1, ct);

        if (firstItems.Count == 0) return [];

        int itemsPerPage = maxPageItems ?? firstItems.Count;
        if (itemsPerPage <= 0) itemsPerPage = 14;

        int totalPages = totalItems.HasValue
            ? (int)Math.Ceiling(totalItems.Value / (double)itemsPerPage)
            : 1;

        if (totalPages <= 1) return firstItems;

        // Kalan sayfaları paralel çek (kategori başına max 4 paralel)
        var pageResults    = new List<StalkerListItem>[totalPages];
        pageResults[0]     = firstItems;

        using var sem = new SemaphoreSlim(4, 4);
        var pageTasks = Enumerable.Range(2, totalPages - 1).Select(p => Task.Run(async () =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var (items, _, _) = await GetPageAsync(
                    endpoint, token, macAddress, listType, categoryId, p, ct);
                pageResults[p - 1] = items;
            }
            catch { pageResults[p - 1] = []; }
            finally { sem.Release(); }
        }, ct));

        await Task.WhenAll(pageTasks);

        var all = new List<StalkerListItem>(totalItems ?? totalPages * itemsPerPage);
        foreach (var page in pageResults)
            if (page != null) all.AddRange(page);
        return all;
    }

    public async Task<StalkerSeriesInfo?> GetSeriesInfoAsync(
        string portalUrl,
        string macAddress,
        string seriesId,
        CancellationToken cancellationToken = default)
    {
        var normalizedPortalUrl = NormalizePortalUrl(portalUrl);
        var (normalizedUrl, endpoint, initialToken) = await ResolveEndpointParallelAsync(
            normalizedPortalUrl, macAddress, cancellationToken);

        if (endpoint == null) return null;

        var token = await GetOrCreateTokenAsync(
            normalizedUrl, endpoint, macAddress, initialToken, cancellationToken);

        var queryParams = new Dictionary<string, string>
        {
            ["action"] = "get_ordered_list",
            ["type"] = "series",
            ["movie_id"] = seriesId,
            ["token"] = token
        };

        var query = BuildQueryString(queryParams);
        try
        {
            var stalkerItems = new List<JsonElement>();
            var js = await GetForJsAsync(endpoint, query, macAddress, token, cancellationToken);
            
            if (js.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                stalkerItems.AddRange(dataEl.EnumerateArray());

                // Sayfalama desteği — Tüm bölümleri çekmek için gerekirse diğer sayfaları da dolaş
                var totalItems = GetInt(js, "total_items");
                var maxPageItems = GetInt(js, "max_page_items") ?? 14; 
                
                if (totalItems > maxPageItems)
                {
                    int totalPages = (int)Math.Ceiling(totalItems.Value / (double)maxPageItems);
                    for (int p = 2; p <= totalPages; p++)
                    {
                        var pageQuery = BuildQueryString(new Dictionary<string, string>(queryParams) { ["p"] = p.ToString() });
                        var pJs = await GetForJsAsync(endpoint, pageQuery, macAddress, token, cancellationToken);
                        if (pJs.TryGetProperty("data", out var pData) && pData.ValueKind == JsonValueKind.Array)
                        {
                            stalkerItems.AddRange(pData.EnumerateArray());
                        }
                    }
                }
            }

            if (stalkerItems.Count == 0)
            {
                Log($"GetSeriesInfoAsync: no items returned for seriesId={seriesId}");
                return null;
            }

            var result = new StalkerSeriesInfo();
            bool metadataSet = false;
            var seasonContainerCount = 0;

            // 1. Tüm detaylı bölüm objelerini ID'lerine göre haritala
            var richEpisodeMap = new Dictionary<string, JsonElement>();
            foreach (var item in stalkerItems)
            {
                var id = GetString(item, "id");
                // Eğer bir 'series' dizisi içermiyorsa, bu bir 'Bölüm' detay objesidir.
                if (!string.IsNullOrEmpty(id) && !item.TryGetProperty("series", out _))
                {
                    richEpisodeMap[id] = item;
                }
            }

            Log($"GetSeriesInfoAsync: seriesId={seriesId}, items={stalkerItems.Count}, richEpisodes={richEpisodeMap.Count}");
            if (richEpisodeMap.Count > 0)
            {
                var sampleEpisode = richEpisodeMap.Values.First();
                Log($"GetSeriesInfoAsync sample episode: keys={string.Join(", ", sampleEpisode.EnumerateObject().Select(p => p.Name))}");
                Log($"GetSeriesInfoAsync sample episode fields: name='{GetString(sampleEpisode, "name")}', pic='{GetString(sampleEpisode, "pic")}', screenshot_uri='{GetString(sampleEpisode, "screenshot_uri")}', icon='{GetString(sampleEpisode, "icon")}', cover='{GetString(sampleEpisode, "cover")}', movie_image='{GetString(sampleEpisode, "movie_image")}', duration='{GetString(sampleEpisode, "duration")}', added='{GetString(sampleEpisode, "added")}'");
            }

            foreach (var item in stalkerItems)
            {
                // Bir sezona ait 'series' dizisi yoksa bu bir sezon konteyneri değildir, atla.
                if (!item.TryGetProperty("series", out var seriesEl) || seriesEl.ValueKind != JsonValueKind.Array)
                    continue;

                seasonContainerCount++;

                if (!metadataSet)
                {
                    result.Description = GetString(item, "description");
                    result.Director = GetString(item, "director");
                    result.Actors = GetString(item, "actors") ?? GetString(item, "actor");
                    result.Year = GetString(item, "year");
                    result.TmdbId = GetString(item, "tmdb_id") ?? GetString(item, "tmdb");
                    result.RatingImdb = GetString(item, "rating_imdb");
                    result.Age = GetString(item, "age");
                    result.CoverUrl = NormalizeLogoUrl(
                        GetString(item, "screenshot_uri") ?? GetString(item, "pic"),
                        ExtractBaseUrl(endpoint));
                    result.GenresStr = GetString(item, "genres_str");
                    metadataSet = true;
                }

                var season = new StalkerSeasonInfo
                {
                    Id = GetString(item, "id") ?? string.Empty,
                    Name = GetString(item, "name") ?? string.Empty,
                    Cmd = GetString(item, "cmd") ?? string.Empty,
                };

                foreach (var ep in seriesEl.EnumerateArray())
                {
                    JsonElement epObj;
                    bool isRich = false;

                    if (ep.ValueKind == JsonValueKind.Number)
                    {
                        var epId = ep.ToString();
                        if (richEpisodeMap.TryGetValue(epId, out var mappedEp))
                        {
                            epObj = mappedEp;
                            isRich = true;
                        }
                        else
                        {
                            // Detay bulunamadıysa sadece numarayı al
                            if (ep.TryGetInt32(out int epNum))
                                season.Episodes.Add(new StalkerEpisodeInfo { EpisodeNumber = epNum });
                            continue;
                        }
                    }
                    else if (ep.ValueKind == JsonValueKind.Object)
                    {
                        epObj = ep;
                        isRich = true;
                    }
                    else continue;

                    if (isRich)
                    {
                        var numStr = GetString(epObj, "name") ?? GetString(epObj, "id") ?? "0";
                        var numId = 0;
                        var match = System.Text.RegularExpressions.Regex.Match(numStr, @"\d+");
                        if (match.Success) int.TryParse(match.Value, out numId);

                        season.Episodes.Add(new StalkerEpisodeInfo
                        {
                            EpisodeNumber = numId,
                            Name = GetString(epObj, "name"),
                            Description = GetString(epObj, "description"),
                            Pic = NormalizeLogoUrl(
                                GetString(epObj, "pic")
                                ?? GetString(epObj, "screenshot_uri")
                                ?? GetString(epObj, "icon")
                                ?? GetString(epObj, "cover")
                                ?? GetString(epObj, "movie_image")
                                ?? GetString(epObj, "screenshot_url")
                                ?? result.CoverUrl, // Fallback to series cover
                                ExtractBaseUrl(endpoint)),
                            Duration = GetString(epObj, "duration"),
                            Added = GetString(epObj, "added")
                        });
                    }
                }
                
                result.Seasons.Add(season);
            }

            var totalEpisodes = result.Seasons.Sum(s => s.Episodes.Count);
            var episodesWithImages = result.Seasons.Sum(s => s.Episodes.Count(e => !string.IsNullOrWhiteSpace(e.Pic)));
            var episodesWithDescriptions = result.Seasons.Sum(s => s.Episodes.Count(e => !string.IsNullOrWhiteSpace(e.Description)));
            Log($"GetSeriesInfoAsync parsed: seriesId={seriesId}, seasons={result.Seasons.Count}, seasonContainers={seasonContainerCount}, episodes={totalEpisodes}, episodeImages={episodesWithImages}, episodeDescriptions={episodesWithDescriptions}, seriesCover='{result.CoverUrl}', seriesPlotPresent={!string.IsNullOrWhiteSpace(result.Description)}");

            return result;
        }
        catch (Exception ex)
        {
            Log($"GetSeriesInfoAsync failed for {seriesId}: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> CreateLinkAsync(
        string portalUrl,
        string macAddress,
        string type,
        string cmd,
        string episodeNum = "0",
        CancellationToken cancellationToken = default)
    {
        var normalizedPortalUrl = NormalizePortalUrl(portalUrl);
        var (normalizedUrl, endpoint, initialToken) = await ResolveEndpointParallelAsync(
            normalizedPortalUrl, macAddress, cancellationToken);

        if (endpoint == null) return null;

        var token = await GetOrCreateTokenAsync(
            normalizedUrl, endpoint, macAddress, initialToken, cancellationToken);

        var queryParams = new Dictionary<string, string>
        {
            ["action"] = "create_link",
            ["type"] = type,
            ["cmd"] = cmd,
            ["force_ch_link"] = "1",
            ["token"] = token
        };

        if (!string.IsNullOrEmpty(episodeNum) && episodeNum != "0")
        {
            queryParams["series"] = episodeNum;
        }

        var query = BuildQueryString(queryParams);
        try
        {
            var js = await GetForJsAsync(endpoint, query, macAddress, token, cancellationToken);
            if (js.TryGetProperty("cmd", out var cmdEl))
            {
                var rawCmd = cmdEl.GetString();
                return NormalizeStreamCommand(rawCmd, ExtractBaseUrl(endpoint));
            }
            return null;
        }
        catch (Exception ex)
        {
            Log($"CreateLinkAsync failed cmd={cmd}: {ex.Message}");
            return null;
        }
    }

    private async Task<(List<StalkerListItem> Items, int? TotalItems, int? MaxPageItems)> GetPageAsync(
        string endpoint, string token, string macAddress,
        string listType, string categoryId, int page, CancellationToken ct)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["action"] = "get_ordered_list",
            ["type"]   = listType,
            ["sortby"] = listType == "itv" ? "number" : "added",
            ["p"]      = page.ToString(),
            ["token"]  = token
        };

        // Kategori filtresi — * yerine spesifik ID
        if (listType == "itv")
            queryParams["genre"] = categoryId;
        else
            queryParams["category"] = categoryId;

        var query = BuildQueryString(queryParams);
        JsonElement js;
        try
        {
            js = await GetForJsAsync(endpoint, query, macAddress, token, ct);
        }
        catch (Exception ex)
        {
            Log($"GetPageAsync failed for category {categoryId}, page {page}: {ex.Message}");
            return ([], null, null);
        }

        JsonElement dataEl;
        int? totalItems = null;
        int? maxPageItems = null;

        if (js.ValueKind == JsonValueKind.Array)
        {
            dataEl = js;
            totalItems = js.GetArrayLength();
        }
        else
        {
            totalItems   = GetInt(js, "total_items");
            maxPageItems = GetInt(js, "max_page_items");
            
            // Try different data property names used by various Stalker portals
            if (!js.TryGetProperty("data", out dataEl))
            {
                if (!js.TryGetProperty("js", out dataEl))
                {
                    _ = js.TryGetProperty("result", out dataEl);
                }
            }
        }

        var items = new List<StalkerListItem>();
        if (dataEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataEl.EnumerateArray())
            {
                items.Add(new StalkerListItem
                {
                    Id        = GetString(item, "id"),
                    Name      = GetString(item, "name"),
                    Cmd       = GetString(item, "cmd"),
                    Logo      = GetString(item, "logo") ?? 
                                GetString(item, "pic") ?? 
                                GetString(item, "cover") ?? 
                                GetString(item, "screenshot_uri"),
                    TvGenreId = GetString(item, "tv_genre_id") ??
                                GetString(item, "category_id") ??
                                GetString(item, "genre_id"),
                    CategoryName = GetString(item, "category_name") ??
                                   GetString(item, "genre_name")
                });
            }
        }

        return (items, totalItems, maxPageItems);
    }

    // ═══════════════════════════════════════════════════════════
    //  FALLBACK — Kategori sistemi çalışmazsa eski yönteme dön
    // ═══════════════════════════════════════════════════════════

    private async Task<List<Channel>> LoadAllWithGenreStarAsync(
        string endpoint, string token, string macAddress,
        bool includeVod, CancellationToken ct)
    {
        var liveGenreTask  = FetchCategoriesAsync(endpoint, token, macAddress, "itv", ct);
        var liveItemsTask  = GetAllPagesForCategoryAsync(endpoint, token, macAddress, "itv", "*", ct);

        Task<List<StalkerCategory>>?   vodGenreTask  = null;
        Task<List<StalkerListItem>>?   vodItemsTask  = null;
        Task<List<StalkerCategory>>?   serGenreTask  = null;
        Task<List<StalkerListItem>>?   serItemsTask  = null;

        if (includeVod)
        {
            vodGenreTask = FetchCategoriesAsync(endpoint, token, macAddress, "vod",    ct);
            vodItemsTask = GetAllPagesForCategoryAsync(endpoint, token, macAddress, "vod",    "*", ct);
            serGenreTask = FetchCategoriesAsync(endpoint, token, macAddress, "series", ct);
            serItemsTask = GetAllPagesForCategoryAsync(endpoint, token, macAddress, "series", "*", ct);
        }

        await Task.WhenAll(
            liveGenreTask, liveItemsTask,
            vodGenreTask  ?? Task.CompletedTask,
            vodItemsTask  ?? Task.CompletedTask,
            serGenreTask  ?? Task.CompletedTask,
            serItemsTask  ?? Task.CompletedTask);

        var baseUrl = ExtractBaseUrl(endpoint);
        var channels = new List<Channel>();

        channels.AddRange(BuildChannels(liveItemsTask.Result, baseUrl, ChannelType.Live,
            liveGenreTask.Result.ToDictionary(c => c.Id, c => c.Name)));

        if (includeVod)
        {
            channels.AddRange(BuildChannels(vodItemsTask!.Result, baseUrl, ChannelType.VOD,
                vodGenreTask!.Result.ToDictionary(c => c.Id, c => c.Name)));
            channels.AddRange(BuildChannels(serItemsTask!.Result, baseUrl, ChannelType.Series,
                serGenreTask!.Result.ToDictionary(c => c.Id, c => c.Name)));
        }

        return channels;
    }

    // ═══════════════════════════════════════════════════════════
    //  KANAL OLUŞTURMA
    // ═══════════════════════════════════════════════════════════

    private static List<Channel> BuildChannels(
        IReadOnlyCollection<StalkerListItem> items,
        string baseUrl,
        ChannelType channelType,
        IReadOnlyDictionary<string, string> genreMap)
    {
        if (items.Count == 0) return [];
        var channels = new List<Channel>(items.Count);

        foreach (var item in items)
        {
            var streamUrl = NormalizeStreamCommand(item.Cmd, baseUrl);

            // Stalker API genellikle diziler için 'cmd' döndürmez.
            // Arayüzde görünebilmesi için onlara geçici bir kimlik linki atıyoruz.
            if (string.IsNullOrWhiteSpace(streamUrl) && channelType == ChannelType.Series && !string.IsNullOrWhiteSpace(item.Id))
            {
                streamUrl = $"stalker-series://{item.Id}";
            }

            if (string.IsNullOrWhiteSpace(streamUrl)) continue;

            var group = item.TvGenreId != null && genreMap.TryGetValue(item.TvGenreId, out var gn)
                ? gn
                : (!string.IsNullOrWhiteSpace(item.CategoryName)
                    ? item.CategoryName
                    : channelType == ChannelType.Live ? "Live"
                    : channelType == ChannelType.Series ? "Series" : "VOD");

            var name = string.IsNullOrWhiteSpace(item.Name) ? "İsimsiz Kanal" : item.Name.Trim();

            channels.Add(new Channel
            {
                Name       = name,
                StreamUrl  = streamUrl,
                LogoUrl    = NormalizeLogoUrl(item.Logo, baseUrl),
                GroupTitle = group,
                Type       = channelType
            });
        }

        return channels;
    }
    // ═══════════════════════════════════════════════════════════
    //  HTTP
    // ═══════════════════════════════════════════════════════════

    private async Task<JsonElement> GetForJsAsync(
        string endpoint, string queryString, string macAddress,
        string? token, CancellationToken ct)
    {
        int retryCount = 0;
        int maxRetries = 3;
        int delayMs = 500;

        while (true)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var request = BuildGetRequest(endpoint, queryString, macAddress, token);
                response = await _httpClient.SendAsync(request, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && retryCount < maxRetries)
                {
                    retryCount++;
                    Log($"[StalkerService] 429 Too Many Requests. Retrying in {delayMs}ms... (Attempt {retryCount}/{maxRetries})");
                    await Task.Delay(delayMs, ct);
                    delayMs *= 2; 
                    continue;
                }

                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex) when (retryCount < maxRetries)
            {
                retryCount++;
                Log($"[StalkerService] Network error: {ex.Message}. Retrying in {delayMs}ms... (Attempt {retryCount}/{maxRetries})");
                await Task.Delay(delayMs, ct);
                delayMs *= 2;
                continue;
            }
            catch (Exception ex)
            {
                Log($"[StalkerService] HTTP error: {ex.Message}");
                throw;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
            return JsonDocument.Parse("[]").RootElement;

        // --- YENİ: HTML/Hata Sayfası Kontrolü ---
        var trimmedBody = body.TrimStart();
        if (trimmedBody.StartsWith("<") || trimmedBody.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = trimmedBody.Length > 100 ? trimmedBody.Substring(0, 100) : trimmedBody;
            Log($"[StalkerService] HTML response instead of JSON (Snippet: {snippet})");
            throw new InvalidOperationException($"Sunucu geçerli bir JSON yanıtı yerine HTML hata sayfası döndürdü. (Bkz log)");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return (doc.RootElement.TryGetProperty("js", out var js) ? js : doc.RootElement).Clone();
        }
        catch (JsonException ex)
        {
            Log($"[StalkerService] JSON parse error: {ex.Message}. URL: {endpoint}?{queryString}");
            throw;
        }
        }
    }

    private static HttpRequestMessage BuildGetRequest(
        string endpoint, string queryString, string macAddress, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{queryString}");
        request.Headers.TryAddWithoutValidation(
            "Cookie", $"mac={macAddress}; stb_lang=en; timezone=Europe/Istanbul");
        request.Headers.TryAddWithoutValidation("X-User-Agent", "Model: MAG250; Link: WiFi");
        request.Headers.TryAddWithoutValidation("User-Agent", 
            "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("Referer", endpoint);
        return request;
    }

    // ═══════════════════════════════════════════════════════════
    //  YARDIMCILAR
    // ═══════════════════════════════════════════════════════════

    private static string NormalizeStreamCommand(string? cmd, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return string.Empty;
        var trimmed = cmd.Trim();

        var httpIdx = trimmed.IndexOf("http://",  StringComparison.OrdinalIgnoreCase);
        if (httpIdx < 0) httpIdx = trimmed.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (httpIdx >= 0) return trimmed[httpIdx..].Trim();

        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            trimmed, @"^(ffrt\d*|auto|ch)\s*", string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        if (cleaned.StartsWith('/') && !string.IsNullOrWhiteSpace(baseUrl))
            return $"{baseUrl}{cleaned}";
        if (!string.IsNullOrWhiteSpace(cleaned) &&
            !cleaned.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(baseUrl))
            return $"{baseUrl}/{cleaned.TrimStart('/')}";
        return cleaned;
    }

    private static string? NormalizeLogoUrl(string? logo, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(logo)) return null;
        if (logo.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return logo;
        if (logo.StartsWith('/') && !string.IsNullOrWhiteSpace(baseUrl)) return $"{baseUrl}{logo}";
        return logo;
    }

    private static string ExtractBaseUrl(string endpoint)
    {
        try
        {
            var uri = new Uri(endpoint);
            return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}";
        }
        catch { return string.Empty; }
    }

    private static string NormalizePortalUrl(string portalUrl)
    {
        if (string.IsNullOrWhiteSpace(portalUrl))
            return string.Empty;

        var n = portalUrl.Trim();
        if (!n.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) &&
            !n.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            n = $"http://{n}";

        // Geçersiz bir HTTP URL'si ise boş dön (Örn: http://)
        if (n.Length <= 7 || (n.StartsWith("http://") && n.Length == 7))
            return string.Empty;

        return n.TrimEnd('/');
    }

    private static string ResolveGenre(
        string? genreId, IReadOnlyDictionary<string, string> genreMap, ChannelType type)
    {
        if (!string.IsNullOrWhiteSpace(genreId) && genreMap.TryGetValue(genreId, out var name))
            return name;
        return type == ChannelType.Live ? "Live" : "VOD";
    }

    private static string BuildQueryString(Dictionary<string, string> parameters)
    {
        return string.Join("&", parameters
            .Where(kv => kv.Value != null)
            .Select(kv => $"{HttpUtility.UrlEncode(kv.Key)}={HttpUtility.UrlEncode(kv.Value)}"));
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
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
        return int.TryParse(raw, out var v) ? v : null;
    }

    private static void Log(string msg)
    {
        System.Diagnostics.Debug.WriteLine($"[Stalker] {msg}");
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logPath = Path.Combine(localAppData, "Noctra", "logs", "startup.log");
            var directory = Path.GetDirectoryName(logPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [StalkerService] {msg}{Environment.NewLine}";
            File.AppendAllText(logPath, line, System.Text.Encoding.UTF8);
        }
        catch { /* Loglama hatası ana akışı bozmamalı */ }
    }

    // ═══════════════════════════════════════════════════════════
    //  İÇ SINIFLAR
    // ═══════════════════════════════════════════════════════════

    private sealed class StalkerListItem
    {
        public string? Id           { get; set; }
        public string? Name         { get; set; }
        public string? Cmd          { get; set; }
        public string? Logo         { get; set; }
        public string? TvGenreId    { get; set; }
        public string? CategoryName { get; set; }
    }

    private readonly record struct CachedTokenState(string Token, DateTimeOffset ExpiresAt);
    private readonly record struct CachedEndpointState(string Endpoint, DateTimeOffset ExpiresAt);
}
