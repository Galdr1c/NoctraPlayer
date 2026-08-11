using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Noctra.Data;
using Noctra.Core.Services;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Service for fetching content metadata from TMDB (The Movie Database)
/// </summary>
public partial class MetadataService : IMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly IDbContextFactory<AppDbContext>? _dbContextFactory;
    private readonly ISettingsService? _settingsService;
    private readonly ILocalizationService? _localizationService;
    private readonly ILogger<MetadataService>? _logger;
    
    private const string TMDB_BASE_URL = "https://tmdb-proxy-galdric.vercel.app/api/tmdb";
    private const string DIRECT_TMDB_BASE_URL = "https://api.themoviedb.org/3";
    private static readonly bool IsUsingProxy = !TMDB_BASE_URL.Contains("api.themoviedb.org");
    private const string TMDB_IMAGE_BASE_URL = "https://image.tmdb.org/t/p";
    
    // TMDB credential. v4 access tokens use Bearer auth; v3 API keys must stay in the query string.
    private string _apiKey = string.Empty;
    private bool _useQueryApiKey;
    
    // Genre cache: LanguageCode -> (GenreID -> GenreName)
    private readonly ConcurrentDictionary<string, Dictionary<int, string>> _genreCache = new();
    private readonly SemaphoreSlim _genreLock = new(1, 1);
    
    public MetadataService(
        HttpClient httpClient,
        IDbContextFactory<AppDbContext>? dbContextFactory = null,
        ISettingsService? settingsService = null,
        ILocalizationService? localizationService = null,
        ILogger<MetadataService>? logger = null)
    {
        _httpClient = httpClient;
        _dbContextFactory = dbContextFactory;
        _settingsService = settingsService;
        _localizationService = localizationService;
        _logger = logger;
        
        LoadApiKeyFromEnvironment();
    }

    /// <summary>
    /// Resolves the effective language code to use for TMDB API calls.
    /// If a language code is explicitly provided, uses it.
    /// Otherwise, reads from the localization service, falling back to "en-US".
    /// </summary>
    private string ResolveLanguage(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
            return languageCode!;
        return _localizationService?.CurrentLanguage ?? "en-US";
    }
    
    /// <summary>
    /// Sets the TMDB API key
    /// </summary>
    public void SetApiKey(string apiKey)
    {
        ApplyApiCredential(apiKey, forceBearer: true);
    }

    private void ApplyApiCredential(string? credential, bool forceBearer)
    {
        _apiKey = credential?.Trim() ?? string.Empty;
        _useQueryApiKey = !forceBearer && IsLikelyV3ApiKey(_apiKey);

        if (string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            return;
        }

        _httpClient.DefaultRequestHeaders.Authorization = IsUsingProxy || _useQueryApiKey
            ? null
            : new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    private void LoadApiKeyFromEnvironment()
    {
        var bearerToken = Environment.GetEnvironmentVariable("TMDB_BEARER_TOKEN");
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            ApplyApiCredential(bearerToken, forceBearer: true);
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable("TMDB_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            ApplyApiCredential(apiKey, forceBearer: false);
        }
    }

    private string AddApiKeyIfNeeded(string url)
    {
        // Proxy mode: proxy handles auth on its own, no need to send API key
        if (IsUsingProxy)
            return url;

        return AddApiKeyToUrl(url);
    }

    /// <summary>
    /// Adds the TMDB API key to the URL for direct API access (used during proxy fallback).
    /// </summary>
    private string AddApiKeyToUrl(string url)
    {
        if (!_useQueryApiKey || string.IsNullOrWhiteSpace(_apiKey))
            return url;

        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}api_key={Uri.EscapeDataString(_apiKey)}";
    }

    /// <summary>
    /// Builds a fallback URL by replacing the proxy base with the direct TMDB API base.
    /// Returns null if the URL doesn't contain the proxy base.
    /// </summary>
    private string? BuildFallbackUrl(string proxyUrl)
    {
        var fallback = proxyUrl.Replace(TMDB_BASE_URL, DIRECT_TMDB_BASE_URL);
        return fallback != proxyUrl ? fallback : null;
    }

    /// <summary>
    /// Executes an HTTP GET with automatic proxy fallback.
    /// Tries the proxy URL first. On failure (5xx, network error, timeout),
    /// falls back to the direct TMDB API with the API key.
    /// </summary>
    private async Task<HttpResponseMessage?> GetWithFallbackAsync(string url, CancellationToken cancellationToken)
    {
        // Attempt 1: Proxy
        HttpResponseMessage? response = null;
        string? fallbackUrl = null;

        try
        {
            response = await SendGetAsync(AddApiKeyIfNeeded(url), includeCredential: !IsUsingProxy, cancellationToken);

            if (response.IsSuccessStatusCode)
                return response;

            if ((int)response.StatusCode >= 500)
            {
                _logger?.LogWarning("TMDB proxy returned {StatusCode}, falling back to direct API", (int)response.StatusCode);
                fallbackUrl = BuildFallbackUrl(url);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == null)
        {
            // Network-level error (DNS, connection refused, SSL, etc.) — not an HTTP response error
            _logger?.LogWarning(ex, "TMDB proxy network error, falling back to direct API");
            fallbackUrl = BuildFallbackUrl(url);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout (not user cancellation)
            _logger?.LogWarning("TMDB proxy request timed out, falling back to direct API");
            fallbackUrl = BuildFallbackUrl(url);
        }

        if (fallbackUrl == null)
            return response;

        // Attempt 2: Direct API
        response?.Dispose();

        try
        {
            // Ensure API key is loaded for the direct API fallback
            EnsureApiKeyLoaded();
            var directUrl = AddApiKeyToUrl(fallbackUrl);
            response = await SendGetAsync(directUrl, includeCredential: true, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Direct TMDB API fallback also failed: {StatusCode}", (int)response.StatusCode);
                return null;
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Direct TMDB API fallback failed for: {Url}", TrimForLog(fallbackUrl));
            return null;
        }
    }

    /// <summary>
    /// Fetches and deserializes JSON with automatic proxy fallback.
    /// </summary>
    private async Task<T?> FetchJsonWithFallbackAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        var response = await GetWithFallbackAsync(url, cancellationToken);
        if (response?.IsSuccessStatusCode != true)
            return null;

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string url, bool includeCredential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (includeCredential && !_useQueryApiKey && !string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static bool IsLikelyV3ApiKey(string credential)
    {
        return credential.Length == 32 && Regex.IsMatch(credential, "^[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant);
    }

    public async Task<ChannelMetadata?> FetchMetadataAsync(string searchQuery, ChannelType? type = null, string? languageCode = null, CancellationToken cancellationToken = default)
    {
        if (!IsUsingProxy)
            EnsureApiKeyLoaded();

        if (string.IsNullOrWhiteSpace(searchQuery) || (!IsUsingProxy && string.IsNullOrEmpty(_apiKey)))
            return null;
        
        try
        {
            languageCode = ResolveLanguage(languageCode);

            // Clean up search query (remove year, quality tags, SxxExx, etc.)
            var cleanQuery = CleanSearchQuery(searchQuery);
            
            // Eğer cleanQuery boş kaldıysa (örn: sadece "S01E01" ise), orijinali kullanmayı dene veya null dön
            if (string.IsNullOrWhiteSpace(cleanQuery))
                cleanQuery = searchQuery;

            var (queryWithoutYear, year) = ExtractYearFromQuery(cleanQuery);

            string url;
            if (type == ChannelType.VOD)
            {
                // Sadece film ara
                url = $"{TMDB_BASE_URL}/search/movie?query={Uri.EscapeDataString(queryWithoutYear)}&include_adult=false&language={languageCode}";
                if (year.HasValue)
                    url += $"&primary_release_year={year.Value}";
            }
            else if (type == ChannelType.Series)
            {
                // Sadece dizi ara
                url = $"{TMDB_BASE_URL}/search/tv?query={Uri.EscapeDataString(queryWithoutYear)}&include_adult=false&language={languageCode}";
                if (year.HasValue)
                    url += $"&first_air_date_year={year.Value}";
            }
            else
            {
                // Karışık ara (Multi search)
                url = $"{TMDB_BASE_URL}/search/multi?query={Uri.EscapeDataString(queryWithoutYear)}&include_adult=false&language={languageCode}";
            }
            
            var response = await GetWithFallbackAsync(url, cancellationToken);
            
            if (response == null)
            {
                _logger?.LogWarning("TMDB API request failed (proxy + fallback)", null);
                return null;
            }
            
            var data = await response.Content.ReadFromJsonAsync<TmdbSearchResponse>(cancellationToken: cancellationToken);
            
            if (data?.Results == null || data.Results.Count == 0)
                return null;
            
            // Score and rank results by name similarity (not just popularity)
            int ScoreResult(TmdbResult r)
            {
                var resultName = r.DisplayTitle ?? "";
                // Exact match is king
                if (resultName.Equals(queryWithoutYear, StringComparison.OrdinalIgnoreCase)) return 100;
                // Exact match on original title/name
                if ((r.OriginalTitle ?? r.OriginalName ?? "").Equals(queryWithoutYear, StringComparison.OrdinalIgnoreCase)) return 95;
                // Result title starts with query
                if (resultName.StartsWith(queryWithoutYear, StringComparison.OrdinalIgnoreCase)) return 80;
                // Query starts with result title (e.g. query="Barry 2018", result="Barry")
                if (queryWithoutYear.StartsWith(resultName, StringComparison.OrdinalIgnoreCase)) return 75;
                // Contains as whole word
                if (resultName.Contains(" " + queryWithoutYear, StringComparison.OrdinalIgnoreCase) ||
                    resultName.Contains(queryWithoutYear + " ", StringComparison.OrdinalIgnoreCase)) return 40;
                // Contains anywhere
                if (resultName.Contains(queryWithoutYear, StringComparison.OrdinalIgnoreCase)) return 20;
                // Fallback
                return 1;
            }

            TmdbResult? best = null;

            if (type == ChannelType.VOD)
            {
                best = data.Results
                    .OrderByDescending(r => ScoreResult(r))
                    .ThenByDescending(r => r.Popularity)
                    .FirstOrDefault();
            }
            else if (type == ChannelType.Series)
            {
                best = data.Results
                    .OrderByDescending(r => ScoreResult(r))
                    .ThenByDescending(r => r.Popularity)
                    .FirstOrDefault();
            }
            else
            {
                best = data.Results
                    .Where(r => r.MediaType == "movie" || r.MediaType == "tv")
                    .OrderByDescending(r => ScoreResult(r))
                    .ThenByDescending(r => r.Popularity)
                    .FirstOrDefault();
            }
            
            if (best == null)
                return null;
            
            // Get detailed info (Cast, Director, etc.)
            var mediaType = best.MediaType ?? (type == ChannelType.VOD ? "movie" : "tv");
            var details = await FetchDetailsAsync(best.Id, mediaType, languageCode, cancellationToken);
            
            // Get genre names
            var genres = await GetGenresAsync(best.GenreIds, languageCode, cancellationToken);
            
            var metadata = new ChannelMetadata
            {
                Title = best.DisplayTitle,
                Description = best.Overview,
                PosterUrl = GetImageUrl(best.PosterPath, "w500"),
                BackdropUrl = GetImageUrl(best.BackdropPath, "original"),
                Rating = best.VoteAverage,
                ReleaseYear = best.ReleaseYear,
                Genres = genres,
                MediaType = mediaType,
                TmdbId = best.Id
            };

            if (details?.Credits != null)
            {
                // Director
                var director = details.DirectorName;
                if (!string.IsNullOrEmpty(director))
                    metadata.Director = director;

                // Cast (Top 5)
                var castList = details.Credits.Cast.OrderBy(c => c.Order).Take(5).Select(c => c.Name).ToList();
                if (castList.Any())
                    metadata.Cast = string.Join(", ", castList);
            }

            // Apply heuristics (Rating, Network etc.)
            if (details != null)
            {
                ApplyHeuristics(details, metadata, languageCode, searchQuery);
            }

            return metadata;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error fetching metadata for: {Query}", searchQuery);
            return null;
        }
    }
    
    public async Task<TmdbDetail?> FetchSeriesDetailsAsync(int tmdbId, string? languageCode = null, CancellationToken cancellationToken = default)
    {
        if (!IsUsingProxy)
        {
            EnsureApiKeyLoaded();
            if (string.IsNullOrEmpty(_apiKey)) return null;
        }

        try
        {
            languageCode = ResolveLanguage(languageCode);
            var lang = languageCode.Contains('-') ? languageCode.Split('-')[0] : languageCode;
            var url = $"{TMDB_BASE_URL}/tv/{tmdbId}?append_to_response=credits,content_ratings,videos,watch/providers&include_video_language={lang},en,null&language={languageCode}";
            
            // Retry up to 2 times on transient network errors
            return await FetchJsonWithFallbackAsync<TmdbDetail>(url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error fetching series details for TmdbId {Id}", tmdbId);
            return null;
        }
    }

    public async Task<TmdbSeasonDetail?> FetchSeasonDetailsAsync(int tmdbId, int seasonNumber, string? languageCode = null, CancellationToken cancellationToken = default)
    {
        if (!IsUsingProxy)
        {
            EnsureApiKeyLoaded();
            if (string.IsNullOrEmpty(_apiKey)) return null;
        }

        try
        {
            languageCode = ResolveLanguage(languageCode);
            var url = $"{TMDB_BASE_URL}/tv/{tmdbId}/season/{seasonNumber}?language={languageCode}";
            // Retry up to 2 times on transient network errors
            return await FetchJsonWithFallbackAsync<TmdbSeasonDetail>(url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error fetching season {SeasonNumber} details for TmdbId {Id}", seasonNumber, tmdbId);
            return null;
        }
    }

    private async Task<TmdbDetail?> FetchDetailsAsync(int id, string mediaType, string languageCode, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = mediaType == "movie" ? "movie" : "tv";
            var append = mediaType == "movie" ? "credits,release_dates,videos,watch/providers" : "credits,content_ratings,videos,watch/providers";
            var url = $"{TMDB_BASE_URL}/{endpoint}/{id}?append_to_response={append}&language={languageCode}";
            
            return await FetchJsonWithFallbackAsync<TmdbDetail>(url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error fetching details for {Id} ({Type})", id, mediaType);
            return null;
        }
    }

    public void ApplyHeuristics(TmdbDetail details, ChannelMetadata metadata, string languageCode, string? contextTitle)
    {
        var contextLower = (contextTitle ?? "").ToLower()
            .Replace('ı', 'i')
            .Replace('İ', 'i');

        // Determine country code with better fallback for TR/DIZI style contexts
        var countryCode = languageCode.Contains('-') ? languageCode.Split('-').Last().ToUpper() : "TR";
        if (contextLower.Contains("tr/dizi") || contextLower.Contains("tr-dizi") || contextLower.Contains("[tr]"))
        {
            countryCode = "TR";
        }

        // 1. Content Rating (Certification)
        if (details.ContentRatings?.Results != null)
        {
            // Priority: Detected country -> TR -> DE -> US -> Any
            var trRating = details.ContentRatings.Results.FirstOrDefault(r => r.IsoCode == "TR")?.Rating;
            var deRating = details.ContentRatings.Results.FirstOrDefault(r => r.IsoCode == "DE")?.Rating;
            var usRating = details.ContentRatings.Results.FirstOrDefault(r => r.IsoCode == "US")?.Rating;
            var detectedRating = details.ContentRatings.Results.FirstOrDefault(r => r.IsoCode == countryCode)?.Rating;
            var anyRating = details.ContentRatings.Results.FirstOrDefault(r => !string.IsNullOrEmpty(r.Rating))?.Rating;

            metadata.ContentRating = detectedRating ?? trRating ?? deRating ?? usRating ?? anyRating;
        }
        else if (details.ReleaseDates?.Results != null) // Movies
        {
            var trRating = details.ReleaseDates.Results.FirstOrDefault(r => r.IsoCode == "TR")?.ReleaseDates.FirstOrDefault(rd => !string.IsNullOrEmpty(rd.Certification))?.Certification;
            var deRating = details.ReleaseDates.Results.FirstOrDefault(r => r.IsoCode == "DE")?.ReleaseDates.FirstOrDefault(rd => !string.IsNullOrEmpty(rd.Certification))?.Certification;
            var usRating = details.ReleaseDates.Results.FirstOrDefault(r => r.IsoCode == "US")?.ReleaseDates.FirstOrDefault(rd => !string.IsNullOrEmpty(rd.Certification))?.Certification;
            var detectedRating = details.ReleaseDates.Results.FirstOrDefault(r => r.IsoCode == countryCode)?.ReleaseDates.FirstOrDefault(rd => !string.IsNullOrEmpty(rd.Certification))?.Certification;
            var anyRating = details.ReleaseDates.Results.SelectMany(r => r.ReleaseDates).FirstOrDefault(rd => !string.IsNullOrEmpty(rd.Certification))?.Certification;

            metadata.ContentRating = detectedRating ?? trRating ?? deRating ?? usRating ?? anyRating;
        }

        // 2. Network / Publisher Logos
        if ((details.Networks != null && details.Networks.Count > 0) || (details.WatchProviders?.Results != null))
        {
            // Priority 1: Direct match in category/title name (Original Networks)
            var matchingNetwork = details.Networks?.FirstOrDefault(n => 
            {
                if (string.IsNullOrEmpty(n.Name)) return false;
                var nName = n.Name.ToLower();
                if (contextLower.Contains(nName)) return true;
                
                // Common aliases
                if (nName == "prime video" && contextLower.Contains("amazon")) return true;
                if (nName == "apple tv+" && contextLower.Contains("apple")) return true;
                if (nName == "disney+" && contextLower.Contains("disney")) return true;
                if (nName == "hbo" || nName == "hbo max" || nName == "max") { if (contextLower.Contains("hbo") || contextLower.Contains("max")) return true; }
                if (nName == "paramount+" && contextLower.Contains("paramount")) return true;
                return false;
            });

            // Priority 2: Direct match in Watch Providers (Streaming Platforms for the current country)
            if (matchingNetwork == null && details.WatchProviders?.Results != null && details.WatchProviders.Results.TryGetValue(countryCode, out var countryProviders))
            {
                var allProviders = new List<TmdbProvider>();
                if (countryProviders.Flatrate != null) allProviders.AddRange(countryProviders.Flatrate);
                if (countryProviders.Rent != null) allProviders.AddRange(countryProviders.Rent);
                if (countryProviders.Buy != null) allProviders.AddRange(countryProviders.Buy);

                var bestProvider = allProviders.FirstOrDefault(p => 
                {
                    if (string.IsNullOrEmpty(p.Name)) return false;
                    var pName = p.Name.ToLower()
                        .Replace('ı', 'i')
                        .Replace('İ', 'i')
                        .Replace(" ", "")
                        .Replace("+", "plus");
                    
                    var contextSimple = contextLower.Replace(" ", "").Replace("+", "plus");
                    
                    if (contextSimple.Contains(pName)) return true;
                    if (pName.Contains("tvplus") && contextSimple.Contains("tvplus")) return true;
                    return false;
                });

                if (bestProvider != null)
                {
                    metadata.NetworkName = bestProvider.Name;
                    if (!string.IsNullOrEmpty(bestProvider.LogoPath))
                        metadata.NetworkLogoUrl = $"https://image.tmdb.org/t/p/w92{bestProvider.LogoPath}";
                    return; // Found a specific streaming provider match
                }

                // NEW: Default to first streaming provider (Flatrate) for the country if no context match
                // This is better than the production studio for generic categories (e.g. "TR/DIZI")
                var firstFlatrate = countryProviders.Flatrate?.FirstOrDefault();
                if (firstFlatrate != null)
                {
                    metadata.NetworkName = firstFlatrate.Name;
                    if (!string.IsNullOrEmpty(firstFlatrate.LogoPath))
                        metadata.NetworkLogoUrl = $"https://image.tmdb.org/t/p/w92{firstFlatrate.LogoPath}";
                    return;
                }
            }

            // Priority 3: Origin Country match in Networks
            matchingNetwork ??= details.Networks?.FirstOrDefault(n => n.OriginCountry == countryCode);

            // Priority 4: First network in list
            var network = matchingNetwork ?? (details.Networks != null && details.Networks.Count > 0 ? details.Networks[0] : null);

            if (network != null)
            {
                metadata.NetworkName = network.Name;
                if (!string.IsNullOrEmpty(network.LogoPath))
                    metadata.NetworkLogoUrl = $"https://image.tmdb.org/t/p/w92{network.LogoPath}";
            }
        }
    }

    private static (string CleanedQuery, int? Year) ExtractYearFromQuery(string query)
    {
        var yearMatch = Regex.Match(query, @"\(?(?:19|20)(\d{2})\)?");
        if (!yearMatch.Success)
            return (query.Trim(), null);

        int year = int.Parse(yearMatch.Value.Trim('(', ')'));
        var cleaned = query.Remove(yearMatch.Index, yearMatch.Length).Trim(' ', '-', '(', ')');
        return (cleaned, year);
    }

    /// <summary>
    /// Lightweight search-only method for scroll enrichment.
    /// Returns basic metadata from the search response WITHOUT fetching details (credits, content_ratings, trailer).
    /// 1 API call instead of 2.
    /// </summary>
    public async Task<ChannelMetadata?> SearchSeriesAsync(string searchQuery, string? languageCode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return null;
        }

        if (!IsUsingProxy)
        {
            EnsureApiKeyLoaded();

            if (string.IsNullOrEmpty(_apiKey))
            {
                return null;
            }
        }

        try
        {
            languageCode = ResolveLanguage(languageCode);
            var cleanQuery = CleanSearchQuery(searchQuery);
            if (string.IsNullOrWhiteSpace(cleanQuery))
                cleanQuery = searchQuery;

            foreach (var queryCandidate in BuildSeriesSearchQueries(cleanQuery))
            {
                foreach (var lang in BuildSeriesSearchLanguages(languageCode))
                {
                    var metadata = await SearchSeriesOnceAsync(queryCandidate, lang, cancellationToken);
                    if (metadata?.PosterUrl != null)
                    {
                        if (!string.Equals(queryCandidate, cleanQuery, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(lang, languageCode, StringComparison.OrdinalIgnoreCase))
                        {
                        }

                        return metadata;
                    }
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TMDB series search failed for: {Query}", searchQuery);
            return null;
        }
    }

    private async Task<ChannelMetadata?> SearchSeriesOnceAsync(string query, string languageCode, CancellationToken cancellationToken)
    {
        var (queryWithoutYear, year) = ExtractYearFromQuery(query);
        if (string.IsNullOrWhiteSpace(queryWithoutYear))
        {
            return null;
        }

        var url = $"{TMDB_BASE_URL}/search/tv?query={Uri.EscapeDataString(queryWithoutYear)}&include_adult=false&language={languageCode}";
        if (year.HasValue)
        {
            url += $"&first_air_date_year={year.Value}";
        }

        var response = await GetWithFallbackAsync(url, cancellationToken);
        if (response == null)
        {
            return null;
        }

        var data = await response.Content.ReadFromJsonAsync<TmdbSearchResponse>(cancellationToken: cancellationToken);
        if (data?.Results == null || data.Results.Count == 0)
        {
            return null;
        }

        int ScoreResult(TmdbResult r)
        {
            var resultName = r.DisplayTitle ?? "";
            if (resultName.Equals(queryWithoutYear, StringComparison.OrdinalIgnoreCase)) return 100;
            if ((r.OriginalTitle ?? r.OriginalName ?? "").Equals(queryWithoutYear, StringComparison.OrdinalIgnoreCase)) return 95;
            if (resultName.StartsWith(queryWithoutYear, StringComparison.OrdinalIgnoreCase)) return 80;
            if (queryWithoutYear.StartsWith(resultName, StringComparison.OrdinalIgnoreCase)) return 75;
            if (resultName.Contains(" " + queryWithoutYear, StringComparison.OrdinalIgnoreCase) ||
                resultName.Contains(queryWithoutYear + " ", StringComparison.OrdinalIgnoreCase)) return 40;
            if (resultName.Contains(queryWithoutYear, StringComparison.OrdinalIgnoreCase)) return 20;
            return 1;
        }

        var best = data.Results
            .Where(r => !string.IsNullOrWhiteSpace(r.PosterPath))
            .OrderByDescending(r => ScoreResult(r))
            .ThenByDescending(r => r.Popularity)
            .FirstOrDefault();

        if (best == null)
        {
            return null;
        }

        var genres = await GetGenresAsync(best.GenreIds, languageCode, cancellationToken);
        return new ChannelMetadata
        {
            TmdbId = best.Id,
            Title = best.DisplayTitle,
            Description = best.Overview,
            PosterUrl = GetImageUrl(best.PosterPath, "w500"),
            BackdropUrl = GetImageUrl(best.BackdropPath, "w780"),
            Rating = best.VoteAverage,
            ReleaseYear = best.ReleaseYear,
            Genres = genres,
            MediaType = "tv"
        };
    }

    private static IEnumerable<string> BuildSeriesSearchQueries(string cleanQuery)
    {
        yield return cleanQuery;
    }

    private static IEnumerable<string> BuildSeriesSearchLanguages(string languageCode)
    {
        yield return languageCode;
        if (!languageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase))
        {
            yield return "en-US";
        }
    }

    private static string TrimForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var normalized = value.Replace("|", "/", StringComparison.Ordinal).Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80] + "...";
    }
    
    public async Task<List<string>> GetGenresAsync(List<int> genreIds, string? languageCode = null, CancellationToken cancellationToken = default)
    {
        if (genreIds.Count == 0)
            return new List<string>();

        languageCode = ResolveLanguage(languageCode);
        
        await EnsureGenresCachedAsync(languageCode, cancellationToken);
        
        var genres = new List<string>();
        
        foreach (var id in genreIds)
        {
            if (_genreCache.TryGetValue(languageCode, out var langCache) && langCache.TryGetValue(id, out var genreName))
                genres.Add(genreName);
        }
        
        return genres.Distinct().ToList();
    }
    
    public void ClearCache()
    {
        _genreCache.Clear();
    }
    
    private async Task EnsureGenresCachedAsync(string languageCode, CancellationToken cancellationToken)
    {
        if (_genreCache.ContainsKey(languageCode))
            return;
        
        await _genreLock.WaitAsync(cancellationToken);
        try
        {
            if (_genreCache.ContainsKey(languageCode))
                return;
            
            var langGenres = new Dictionary<int, string>();

            // Fetch movie genres
            var movieGenreUrl = $"{TMDB_BASE_URL}/genre/movie/list?language={languageCode}";
            var movieResponse = await FetchJsonWithFallbackAsync<TmdbGenreResponse>(movieGenreUrl, cancellationToken);
            if (movieResponse?.Genres != null)
            {
                foreach (var g in movieResponse.Genres)
                    langGenres[g.Id] = g.Name;
            }
            
            // Fetch TV genres
            var tvGenreUrl = $"{TMDB_BASE_URL}/genre/tv/list?language={languageCode}";
            var tvResponse = await FetchJsonWithFallbackAsync<TmdbGenreResponse>(tvGenreUrl, cancellationToken);
            if (tvResponse?.Genres != null)
            {
                foreach (var g in tvResponse.Genres)
                    langGenres[g.Id] = g.Name;
            }

            _genreCache.TryAdd(languageCode, langGenres);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error caching genres for language: {Lang}", languageCode);
        }
        finally
        {
            _genreLock.Release();
        }
    }
    
    private static string? GetImageUrl(string? path, string size)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        
        return $"{TMDB_IMAGE_BASE_URL}/{size}{path}";
    }

    /// <summary>
    /// Cleans up a search query by removing common tags
    /// </summary>
    private static string CleanSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;
        
        // Use generalized series name cleaner which handles seasons, episodes, quality tags and noise
        return SeriesInfoParser.CleanSeriesName(query);
    }

    private void EnsureApiKeyLoaded()
    {
        if (!string.IsNullOrWhiteSpace(_apiKey)) return;
        LoadApiKeyFromEnvironment();
    }
}
