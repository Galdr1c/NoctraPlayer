using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Noctra.Services;

/// <summary>
/// Service for fetching content metadata from TMDB (The Movie Database)
/// </summary>
public partial class MetadataService : IMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<MetadataService>? _logger;
    
    private const string TMDB_BASE_URL = "https://api.themoviedb.org/3";
    private const string TMDB_IMAGE_BASE_URL = "https://image.tmdb.org/t/p";
    
    // API key - should be configured via appsettings or environment variable
    private string _apiKey = string.Empty;
    
    // Genre cache
    private Dictionary<int, string>? _movieGenres;
    private Dictionary<int, string>? _tvGenres;
    private readonly SemaphoreSlim _genreLock = new(1, 1);
    
    public MetadataService(
        HttpClient httpClient,
        ISettingsService? settingsService = null,
        ILogger<MetadataService>? logger = null)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
        
        // Try to get API key from environment or use a placeholder
        _apiKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? "";
    }
    
    /// <summary>
    /// Sets the TMDB API key
    /// </summary>
    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
    }
    
    public async Task<ChannelMetadata?> FetchMetadataAsync(string searchQuery, ChannelType? type = null)
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

            string url;
            if (type == ChannelType.VOD)
            {
                // Sadece film ara
                url = $"{TMDB_BASE_URL}/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(cleanQuery)}&include_adult=false&language=tr-TR";
            }
            else if (type == ChannelType.Series)
            {
                // Sadece dizi ara
                url = $"{TMDB_BASE_URL}/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(cleanQuery)}&include_adult=false&language=tr-TR";
            }
            else
            {
                // Karışık ara (Multi search)
                url = $"{TMDB_BASE_URL}/search/multi?api_key={_apiKey}&query={Uri.EscapeDataString(cleanQuery)}&include_adult=false&language=tr-TR";
            }
            
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("TMDB API request failed: {StatusCode}", response.StatusCode);
                return null;
            }
            
            var data = await response.Content.ReadFromJsonAsync<TmdbSearchResponse>();
            
            if (data?.Results == null || data.Results.Count == 0)
                return null;
            
            // Sonuçları filtrele ve en iyisini seç
            TmdbResult? best = null;

            if (type == ChannelType.VOD)
            {
                 // Zaten movie endpoint'i kullandık, popülerliğe göre al
                 best = data.Results.OrderByDescending(r => r.Popularity).FirstOrDefault();
            }
            else if (type == ChannelType.Series)
            {
                 // Zaten tv endpoint'i kullandık
                 best = data.Results.OrderByDescending(r => r.Popularity).FirstOrDefault();
            }
            else
            {
                // Multi search sonuçlarında type'a göre önceliklendirme yapabiliriz ama type null ise:
                best = data.Results
                    .Where(r => r.MediaType == "movie" || r.MediaType == "tv")
                    .OrderByDescending(r => r.Popularity)
                    .FirstOrDefault();
            }
            
            if (best == null)
                return null;
            
            // Get detailed info (Cast, Director, etc.)
            var mediaType = best.MediaType ?? (type == ChannelType.VOD ? "movie" : "tv");
            var details = await FetchDetailsAsync(best.Id, mediaType);
            
            // Get genre names
            var genres = await GetGenresAsync(best.GenreIds);
            
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

            return metadata;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error fetching metadata for: {Query}", searchQuery);
            return null;
        }
    }
    
    private async Task<TmdbDetail?> FetchDetailsAsync(int id, string mediaType)
    {
        try
        {
            var endpoint = mediaType == "movie" ? "movie" : "tv";
            var url = $"{TMDB_BASE_URL}/{endpoint}/{id}?api_key={_apiKey}&append_to_response=credits&language=tr-TR";
            
            return await _httpClient.GetFromJsonAsync<TmdbDetail>(url);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error fetching details for {Id} ({Type})", id, mediaType);
            return null;
        }
    }

    public async Task EnrichChannelAsync(Channel channel)
    {
        if (channel.Type == ChannelType.Live)
            return; // Don't enrich live channels
        
        // Kanal türünü geçirerek aramayı daralt
        var metadata = await FetchMetadataAsync(channel.Name, channel.Type);
        
        if (metadata == null)
            return;
        
        // Apply metadata to channel
        channel.Plot = metadata.Description;
        channel.Rating = metadata.Rating;
        channel.ReleaseYear = metadata.ReleaseYear;
        channel.BackdropUrl = metadata.BackdropUrl;
        channel.Director = metadata.Director;
        channel.Cast = metadata.Cast;
        
        // Use poster as logo if no logo exists OR if default logo is generic
        if (string.IsNullOrEmpty(channel.LogoUrl) && !string.IsNullOrEmpty(metadata.PosterUrl))
            channel.LogoUrl = metadata.PosterUrl;
    }
    
    public async Task EnrichChannelsAsync(IEnumerable<Channel> channels, IProgress<int>? progress = null)
    {
        var vodChannels = channels.Where(c => c.Type != ChannelType.Live).ToList();
        var processed = 0;
        
        foreach (var channel in vodChannels)
        {
            await EnrichChannelAsync(channel);
            processed++;
            progress?.Report(processed * 100 / vodChannels.Count);
            
            // Rate limiting - TMDB allows ~40 requests per 10 seconds
            await Task.Delay(250);
        }
    }
    
    public async Task<List<string>> GetGenresAsync(List<int> genreIds)
    {
        if (genreIds.Count == 0)
            return new List<string>();
        
        await EnsureGenresCachedAsync();
        
        var genres = new List<string>();
        
        foreach (var id in genreIds)
        {
            if (_movieGenres?.TryGetValue(id, out var movieGenre) == true)
                genres.Add(movieGenre);
            else if (_tvGenres?.TryGetValue(id, out var tvGenre) == true)
                genres.Add(tvGenre);
        }
        
        return genres.Distinct().ToList();
    }
    
    public void ClearCache()
    {
        _movieGenres = null;
        _tvGenres = null;
    }
    
    private async Task EnsureGenresCachedAsync()
    {
        if (_movieGenres != null && _tvGenres != null)
            return;
        
        await _genreLock.WaitAsync();
        try
        {
            if (_movieGenres != null && _tvGenres != null)
                return;
            
            // Fetch movie genres
            var movieGenreUrl = $"{TMDB_BASE_URL}/genre/movie/list?api_key={_apiKey}&language=tr-TR";
            var movieResponse = await _httpClient.GetFromJsonAsync<TmdbGenreResponse>(movieGenreUrl);
            _movieGenres = movieResponse?.Genres.ToDictionary(g => g.Id, g => g.Name) ?? new();
            
            // Fetch TV genres
            var tvGenreUrl = $"{TMDB_BASE_URL}/genre/tv/list?api_key={_apiKey}&language=tr-TR";
            var tvResponse = await _httpClient.GetFromJsonAsync<TmdbGenreResponse>(tvGenreUrl);
            _tvGenres = tvResponse?.Genres.ToDictionary(g => g.Id, g => g.Name) ?? new();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error caching genres");
            _movieGenres ??= new();
            _tvGenres ??= new();
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
        // Remove file extensions
        query = Path.GetFileNameWithoutExtension(query);
        
        // Remove season/episode codes (S01E01, 1x01, S01 - E01)
        query = SeriesCodeRegex().Replace(query, "");
        query = SeriesCodeRegex2().Replace(query, "");
        query = SeriesCodeRegex3().Replace(query, "");

        // Remove quality tags like 1080p, 720p, 4K, HDR, etc.
        query = QualityTagsRegex().Replace(query, "");
        
        // Remove year in parentheses or brackets
        query = YearRegex().Replace(query, "");
        
        // Remove common separators and clean up
        query = query.Replace(".", " ").Replace("_", " ").Replace("-", " ");
        
        // Remove extra whitespace
        query = ExtraWhitespaceRegex().Replace(query, " ").Trim();
        
        return query;
    }
    
    [GeneratedRegex(@"\b(1080p?|720p?|480p?|4K|UHD|HDR|HEVC|x264|x265|BluRay|WEB-?DL|WEB-?Rip|DVDRip|BRRip|HDTV)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityTagsRegex();
    
    [GeneratedRegex(@"[\(\[]\d{4}[\)\]]")]
    private static partial Regex YearRegex();
    
    [GeneratedRegex(@"S(\d{1,2})E(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesCodeRegex();

    [GeneratedRegex(@"(\d{1,2})x(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesCodeRegex2();
    
    [GeneratedRegex(@"S(\d{1,2})\s*-\s*E(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesCodeRegex3();

    [GeneratedRegex(@"\s+")]
    private static partial Regex ExtraWhitespaceRegex();

    private void EnsureApiKeyLoaded()
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            return;
        }

        var fromSettings = _settingsService?.Settings?.TmdbApiKey;
        if (!string.IsNullOrWhiteSpace(fromSettings))
        {
            _apiKey = fromSettings.Trim();
            return;
        }

        var fromEnv = Environment.GetEnvironmentVariable("TMDB_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            _apiKey = fromEnv.Trim();
        }
    }
}


