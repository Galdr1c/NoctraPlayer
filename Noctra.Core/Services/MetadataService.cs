using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Noctra.Data;
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
    private readonly ILogger<MetadataService>? _logger;
    
    private const string TMDB_BASE_URL = "https://api.themoviedb.org/3";
    private const string TMDB_IMAGE_BASE_URL = "https://image.tmdb.org/t/p";
    
    // API key - should be configured via appsettings or environment variable
    private string _apiKey = string.Empty;
    
    // Genre cache: LanguageCode -> (GenreID -> GenreName)
    private Dictionary<string, Dictionary<int, string>> _genreCache = new();
    private readonly SemaphoreSlim _genreLock = new(1, 1);
    
    public MetadataService(
        HttpClient httpClient,
        IDbContextFactory<AppDbContext>? dbContextFactory = null,
        ISettingsService? settingsService = null,
        ILogger<MetadataService>? logger = null)
    {
        _httpClient = httpClient;
        _dbContextFactory = dbContextFactory;
        _settingsService = settingsService;
        _logger = logger;
        
        // Try to get API key from environment
        _apiKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? string.Empty;
    }
    
    /// <summary>
    /// Sets the TMDB API key
    /// </summary>
    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
    }
    
    public async Task<ChannelMetadata?> FetchMetadataAsync(string searchQuery, ChannelType? type = null, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
    {
        EnsureApiKeyLoaded();

        if (string.IsNullOrWhiteSpace(searchQuery) || string.IsNullOrEmpty(_apiKey))
            return null;
        
        try
        {
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
                url = $"{TMDB_BASE_URL}/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(queryWithoutYear)}&include_adult=false&language={languageCode}";
                if (year.HasValue)
                    url += $"&primary_release_year={year.Value}";
            }
            else if (type == ChannelType.Series)
            {
                // Sadece dizi ara
                url = $"{TMDB_BASE_URL}/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(queryWithoutYear)}&include_adult=false&language={languageCode}";
                if (year.HasValue)
                    url += $"&first_air_date_year={year.Value}";
            }
            else
            {
                // Karışık ara (Multi search)
                url = $"{TMDB_BASE_URL}/search/multi?api_key={_apiKey}&query={Uri.EscapeDataString(queryWithoutYear)}&include_adult=false&language={languageCode}";
            }
            
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("TMDB API request failed: {StatusCode}", response.StatusCode);
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
                var director = details.Credits.Crew.FirstOrDefault(c => c.Job == "Director")?.Name;
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
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error fetching metadata for: {Query}", searchQuery);
            return null;
        }
    }
    
    public async Task<TmdbDetail?> FetchSeriesDetailsAsync(int tmdbId, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
    {
        EnsureApiKeyLoaded();
        if (string.IsNullOrEmpty(_apiKey)) return null;

        try
        {
            var lang = languageCode.Contains('-') ? languageCode.Split('-')[0] : languageCode;
            var url = $"{TMDB_BASE_URL}/tv/{tmdbId}?api_key={_apiKey}&append_to_response=credits,content_ratings,videos,watch/providers&include_video_language={lang},en,null&language={languageCode}";
            return await _httpClient.GetFromJsonAsync<TmdbDetail>(url, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error fetching series details for TmdbId {Id}", tmdbId);
            return null;
        }
    }

    public async Task<TmdbSeasonDetail?> FetchSeasonDetailsAsync(int tmdbId, int seasonNumber, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
    {
        EnsureApiKeyLoaded();
        if (string.IsNullOrEmpty(_apiKey)) return null;

        try
        {
            var url = $"{TMDB_BASE_URL}/tv/{tmdbId}/season/{seasonNumber}?api_key={_apiKey}&language={languageCode}";
            return await _httpClient.GetFromJsonAsync<TmdbSeasonDetail>(url, cancellationToken);
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
            var append = mediaType == "movie" ? "credits,release_dates,watch/providers" : "credits,content_ratings,watch/providers";
            var url = $"{TMDB_BASE_URL}/{endpoint}/{id}?api_key={_apiKey}&append_to_response={append}&language={languageCode}";
            
            return await _httpClient.GetFromJsonAsync<TmdbDetail>(url, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error fetching details for {Id} ({Type})", id, mediaType);
            return null;
        }
    }

    public async Task EnrichChannelAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        if (channel.Type == ChannelType.Live)
            return; // Don't enrich live channels
        
        // Skip if already enriched from TMDB
        if (channel.TmdbId.HasValue)
            return;
        
        var languageCode = SeriesInfoParser.ExtractLanguageCode(channel.GroupTitle ?? channel.Name);
        
        // Kanal türünü geçirerek aramayı daralt
        var metadata = await FetchMetadataAsync(channel.Name, channel.Type, languageCode, cancellationToken);
        
        if (metadata == null)
            return;
        
        // Apply metadata to in-memory channel
        channel.TmdbId = metadata.TmdbId;
        channel.Plot = metadata.Description;
        channel.Rating = metadata.Rating;
        channel.ReleaseYear = metadata.ReleaseYear;
        channel.BackdropUrl = metadata.BackdropUrl;
        channel.Director = metadata.Director;
        channel.Cast = metadata.Cast;
        channel.ContentRating = metadata.ContentRating;
        
        // Use TMDB poster if available
        if (!string.IsNullOrEmpty(metadata.PosterUrl))
            channel.LogoUrl = metadata.PosterUrl;
        
        // Persist to database so next play doesn't re-fetch
        if (_dbContextFactory != null)
        {
            try
            {
                using var ctx = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var dbChannel = await ctx.Channels.FindAsync(new object[] { channel.Id }, cancellationToken);
                if (dbChannel != null)
                {
                    dbChannel.TmdbId = channel.TmdbId;
                    dbChannel.Plot = channel.Plot;
                    dbChannel.Rating = channel.Rating;
                    dbChannel.ReleaseYear = channel.ReleaseYear;
                    dbChannel.BackdropUrl = channel.BackdropUrl;
                    dbChannel.Director = channel.Director;
                    dbChannel.Cast = channel.Cast;
                    dbChannel.ContentRating = channel.ContentRating;
                    if (!string.IsNullOrEmpty(metadata.PosterUrl))
                        dbChannel.LogoUrl = metadata.PosterUrl;
                    await ctx.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "VOD metadata persist failed for channel: {Name}", channel.Name);
            }
        }
    }
    
    public async Task EnrichChannelsAsync(IEnumerable<Channel> channels, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var vodChannels = channels.Where(c => c.Type != ChannelType.Live).ToList();
        var processed = 0;
        
        foreach (var channel in vodChannels)
        {
            await EnrichChannelAsync(channel, cancellationToken);
            processed++;
            progress?.Report(processed * 100 / vodChannels.Count);
            
            // Rate limiting - TMDB allows ~40 requests per 10 seconds
            await Task.Delay(250, cancellationToken);
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
                        metadata.NetworkLogoUrl = $"https://image.tmdb.org/t/p/h50{bestProvider.LogoPath}";
                    return; // Found a specific streaming provider match
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
                    metadata.NetworkLogoUrl = $"https://image.tmdb.org/t/p/h50{network.LogoPath}";
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
    public async Task<ChannelMetadata?> SearchSeriesAsync(string searchQuery, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
    {
        EnsureApiKeyLoaded();

        if (string.IsNullOrWhiteSpace(searchQuery) || string.IsNullOrEmpty(_apiKey))
            return null;

        try
        {
            var cleanQuery = CleanSearchQuery(searchQuery);
            if (string.IsNullOrWhiteSpace(cleanQuery))
                cleanQuery = searchQuery;

            var (queryWithoutYear, year) = ExtractYearFromQuery(cleanQuery);

            var url = $"{TMDB_BASE_URL}/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(queryWithoutYear)}&include_adult=false&language={languageCode}";

            if (year.HasValue)
                url += $"&first_air_date_year={year.Value}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var data = await response.Content.ReadFromJsonAsync<TmdbSearchResponse>(cancellationToken: cancellationToken);
            if (data?.Results == null || data.Results.Count == 0)
                return null;

            // Same scoring logic as FetchMetadataAsync
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
                .OrderByDescending(r => ScoreResult(r))
                .ThenByDescending(r => r.Popularity)
                .FirstOrDefault();

            if (best == null)
                return null;

            // Get basic details for network info (still 1 extra call, but necessary for correct logo)
            var details = await FetchDetailsAsync(best.Id, "tv", languageCode, cancellationToken);

            // Genre ID → name conversion (uses cache, no extra API call after first)
            var genres = await GetGenresAsync(best.GenreIds, languageCode, cancellationToken);

            var metadata = new ChannelMetadata
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

            // Apply network/rating heuristics
            if (details != null)
            {
                ApplyHeuristics(details, metadata, languageCode, searchQuery);
            }

            return metadata;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TMDB series search failed for: {Query}", searchQuery);
            return null;
        }
    }
    
    public async Task<List<string>> GetGenresAsync(List<int> genreIds, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
    {
        if (genreIds.Count == 0)
            return new List<string>();
        
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
            var movieGenreUrl = $"{TMDB_BASE_URL}/genre/movie/list?api_key={_apiKey}&language={languageCode}";
            var movieResponse = await _httpClient.GetFromJsonAsync<TmdbGenreResponse>(movieGenreUrl, cancellationToken);
            if (movieResponse?.Genres != null)
            {
                foreach (var g in movieResponse.Genres)
                    langGenres[g.Id] = g.Name;
            }
            
            // Fetch TV genres
            var tvGenreUrl = $"{TMDB_BASE_URL}/genre/tv/list?api_key={_apiKey}&language={languageCode}";
            var tvResponse = await _httpClient.GetFromJsonAsync<TmdbGenreResponse>(tvGenreUrl, cancellationToken);
            if (tvResponse?.Genres != null)
            {
                foreach (var g in tvResponse.Genres)
                    langGenres[g.Id] = g.Name;
            }

            _genreCache[languageCode] = langGenres;
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
        // Rely purely on environment variable or hardcoded internal key
        if (!string.IsNullOrWhiteSpace(_apiKey)) return;

        var fromEnv = Environment.GetEnvironmentVariable("TMDB_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            _apiKey = fromEnv.Trim();
        }
    }
}
