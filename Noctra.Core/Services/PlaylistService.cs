using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace Noctra.Services;

/// <summary>
/// Playlist yönetim servisi
/// </summary>
public class PlaylistService : IPlaylistService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AddPlaylistLocks = new(StringComparer.Ordinal);
    private readonly AppDbContext _context;
    private readonly IM3UParser _parser;
    private readonly IMediaService _mediaService;
    private readonly IPlaylistOrganizerService _organizer;
    private readonly LanguageDetectionService _languageDetection;
    private readonly EpgSourceResolver _epgSourceResolver;
    private readonly IEpgService _epgService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;

    public PlaylistService(
        AppDbContext context, 
        IM3UParser parser, 
        IMediaService mediaService, 
        IPlaylistOrganizerService organizer,
        LanguageDetectionService languageDetection,
        EpgSourceResolver epgSourceResolver,
        IEpgService epgService,
        IServiceScopeFactory scopeFactory,
        HttpClient httpClient)
    {
        _context = context;
        _parser = parser;
        _mediaService = mediaService;
        _organizer = organizer;
        _languageDetection = languageDetection;
        _epgSourceResolver = epgSourceResolver;
        _epgService = epgService;
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
    }

    public async Task<Playlist> AddFromUrlAsync(string name, string url, int? profileId = null)
    {
        var normalizedUrl = (url ?? string.Empty).Trim();
        var lockKey = $"{profileId?.ToString() ?? "null"}|{normalizedUrl}";
        var gate = AddPlaylistLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try 
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] AddFromUrlAsync: {name} - {url}");
            
            // Check existing first to avoid potential parsing overhead
            var existing = await _context.Playlists
                .FirstOrDefaultAsync(p => p.Url == normalizedUrl && p.ProfileId == profileId && p.IsActive);
                
            if (existing != null) 
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] Found existing playlist: {existing.Id} with {existing.ChannelCount} channels");
                return existing;
            }

            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Downloading and parsing M3U from: {normalizedUrl}");
            var channels = await _parser.ParseFromUrlAsync(normalizedUrl);
            var detectedEpgUrl = NormalizeEpgUrl(_parser.LastDetectedEpgUrl);
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Parsed {channels.Count} channels from M3U");

            // Otomatik organizasyon: dedup, kategorize, sıralama
            var organized = _organizer.Organize(channels);
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Organized: {channels.Count} › {organized.Count} channels");

            return await AddFromChannelsAsync(name, normalizedUrl, organized, profileId, detectedEpgUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddFromUrlAsync error: {ex}");
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Playlist> AddFromChannelsAsync(string name, string sourceUrl, IReadOnlyCollection<Channel> channels, int? profileId = null, string? detectedEpgUrl = null)
    {
        var existing = await _context.Playlists
            .FirstOrDefaultAsync(p => p.Url == sourceUrl && p.ProfileId == profileId && p.IsActive);

        if (existing != null)
        {
            return existing;
        }

        var playlist = new Playlist
        {
            Name = name,
            Url = sourceUrl,
            ProfileId = profileId,
            CreatedAt = DateTime.Now,
            LastUpdated = DateTime.Now,
            ChannelCount = channels.Count,
            IsActive = true,
            EpgUrl = NormalizeEpgUrl(detectedEpgUrl)
        };

        _context.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            _context.Playlists.Add(playlist);
            await _context.SaveChangesAsync();
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Created playlist with ID: {playlist.Id}");

            const int batchSize = 1000;
            var channelList = channels.ToList();
            for (int i = 0; i < channelList.Count; i += batchSize)
            {
                var batch = channelList.Skip(i).Take(batchSize).ToList();
                foreach (var channel in batch)
                {
                    channel.PlaylistId = playlist.Id;
                }

                _context.Channels.AddRange(batch);
                await _context.SaveChangesAsync();
            }

            await _mediaService.AggregateContentAsync(playlist.Id);

            // AUTO EPG in isolated scope to avoid DbContext cross-thread usage.
            var playlistId = playlist.Id;
            var channelSnapshot = channels.ToList();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedEpgService = scope.ServiceProvider.GetRequiredService<IEpgService>();
                    var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var channelNames = channelSnapshot.Select(c => c.Name ?? "").ToList();
                    var countryCandidates = _languageDetection.DetectCountries(channelNames)
                        .Where(c => c.Percentage > 10 || c.ChannelCount > 5)
                        .Select(c => c.CountryCode)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(3)
                        .ToList();

                    if (countryCandidates.Count == 0)
                    {
                        countryCandidates.Add("TR");
                    }

                    var detectedCountry = countryCandidates[0];
                    var epgSources = countryCandidates
                        .SelectMany(country => _epgSourceResolver.ResolveEpgSources(
                            country,
                            m3uEpgUrl: NormalizeEpgUrl(detectedEpgUrl)))
                        .GroupBy(source => source.Url, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .OrderBy(source => source.Priority)
                        .ToList();

                    for (var i = 0; i < epgSources.Count; i++)
                    {
                        epgSources[i].ClearBeforeLoad = (i == 0);
                    }

                    string? usedEpgUrl = null;
                    string? autoEpgError = null;

                    foreach (var source in epgSources)
                    {
                        try
                        {
                            if (source.ClearBeforeLoad)
                            {
                                await scopedEpgService.ClearEpgAsync();
                            }

                            System.Diagnostics.Debug.WriteLine($"[AutoEPG] Loading from {source.Url}");
                            var beforeCount = await scopedEpgService.GetTotalProgramCountAsync();
                            await scopedEpgService.LoadEpgAsync(source.Url, source.IsPrimary, channelSnapshot);
                            var afterCount = await scopedEpgService.GetTotalProgramCountAsync();
                            if (afterCount > beforeCount)
                            {
                                usedEpgUrl = source.Url;
                                autoEpgError = null;
                                System.Diagnostics.Debug.WriteLine($"[PlaylistService] EPG loaded from {source.Type} (+{afterCount - beforeCount})");
                                break;
                            }

                            autoEpgError = $"{source.Type}: 0 program";
                            System.Diagnostics.Debug.WriteLine($"[PlaylistService] EPG source had no matches: {source.Type}");
                        }
                        catch (Exception ex)
                        {
                            autoEpgError = $"{source.Type}: {ex.Message}";
                            System.Diagnostics.Debug.WriteLine($"[PlaylistService] EPG source failed: {source.Type} - {ex.Message}");
                        }
                    }

                    if (usedEpgUrl == null && string.IsNullOrWhiteSpace(autoEpgError))
                    {
                        autoEpgError = "Otomatik EPG kaynagindan veri alinamadi.";
                    }

                    var playlistToUpdate = await scopedDb.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
                    if (playlistToUpdate != null)
                    {
                        playlistToUpdate.DetectedCountry = detectedCountry;
                        playlistToUpdate.EpgLastError = autoEpgError;

                        if (!string.IsNullOrWhiteSpace(usedEpgUrl))
                        {
                            playlistToUpdate.EpgUrl = usedEpgUrl;
                            playlistToUpdate.EpgLastUpdated = DateTime.Now;
                            playlistToUpdate.EpgLastError = null;
                        }

                        await scopedDb.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] Auto-EPG error: {ex.Message}");

                    try
                    {
                        using var fallbackScope = _scopeFactory.CreateScope();
                        var fallbackDb = fallbackScope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var playlistToUpdate = await fallbackDb.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
                        if (playlistToUpdate != null)
                        {
                            playlistToUpdate.EpgLastError = $"AutoEPG: {ex.Message}";
                            await fallbackDb.SaveChangesAsync();
                        }
                    }
                    catch
                    {
                    }
                }
            });

            return playlist;
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task<Playlist> AddFromFileAsync(string name, string filePath, int? profileId = null)
    {
        try 
        {
            var rawChannels = await _parser.ParseFromFileAsync(filePath);
            var detectedEpgUrl = NormalizeEpgUrl(_parser.LastDetectedEpgUrl);
            var channels = _organizer.Organize(rawChannels);
            
            var playlist = new Playlist
            {
                Name = name,
                FilePath = filePath,
                ProfileId = profileId,
                CreatedAt = DateTime.Now,
                LastUpdated = DateTime.Now,
                ChannelCount = channels.Count,
                IsActive = true,
                EpgUrl = detectedEpgUrl
            };

            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            _context.Playlists.Add(playlist);
            await _context.SaveChangesAsync();

            const int batchSize = 500;
            for (int i = 0; i < channels.Count; i += batchSize)
            {
                var batch = channels.Skip(i).Take(batchSize).ToList();
                foreach (var channel in batch)
                {
                    channel.PlaylistId = playlist.Id;
                }
                _context.Channels.AddRange(batch);
                await _context.SaveChangesAsync();
            }

            // Aggregation for Series/VOD
            await _mediaService.AggregateContentAsync(playlist.Id);

            return playlist;
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task<List<Playlist>> GetAllAsync(int? profileId = null)
    {
        var query = _context.Playlists.Where(p => p.IsActive);
        
        if (profileId.HasValue)
        {
            query = query.Where(p => p.ProfileId == profileId);
        }
        
        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Playlist> RefreshAsync(int playlistId)
    {
        var playlist = await _context.Playlists
            .Include(p => p.Channels)
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist == null)
            throw new KeyNotFoundException($"Playlist bulunamadı: {playlistId}");

        RemotePlaylistMetadata? latestRemoteMetadata = null;
        if (!string.IsNullOrWhiteSpace(playlist.Url))
        {
            latestRemoteMetadata = await TryFetchRemoteMetadataAsync(playlist.Url, playlist);
            if (latestRemoteMetadata?.IsUnchanged == true)
            {
                playlist.LastUpdated = DateTime.Now;
                UpdatePlaylistSourceMetadata(playlist, latestRemoteMetadata);
                await _context.SaveChangesAsync();
                return playlist;
            }
        }

        // Yeni kanalları parse et
        List<Channel> newChannels;
        if (!string.IsNullOrEmpty(playlist.Url))
        {
            newChannels = await _parser.ParseFromUrlAsync(playlist.Url);
        }
        else if (!string.IsNullOrEmpty(playlist.FilePath))
        {
            newChannels = await _parser.ParseFromFileAsync(playlist.FilePath);
        }
        else
        {
            throw new InvalidOperationException("Playlist'in URL veya dosya yolu yok");
        }

        // Organizasyon pipeline'ı uygula
        var organizedChannels = _organizer.Organize(newChannels);
        var existingFingerprints = new HashSet<string>(
            playlist.Channels.Select(BuildChannelFingerprint),
            StringComparer.OrdinalIgnoreCase);
        var channelsToAdd = organizedChannels
            .Where(c => !existingFingerprints.Contains(BuildChannelFingerprint(c)))
            .ToList();

        if (channelsToAdd.Count == 0)
        {
            playlist.LastUpdated = DateTime.Now;
            if (latestRemoteMetadata != null)
            {
                UpdatePlaylistSourceMetadata(playlist, latestRemoteMetadata);
            }
            await _context.SaveChangesAsync();
            return playlist;
        }

        foreach (var channel in channelsToAdd)
        {
            channel.PlaylistId = playlist.Id;
        }

        _context.Channels.AddRange(channelsToAdd);
        playlist.ChannelCount = playlist.Channels.Count + channelsToAdd.Count;
        playlist.LastUpdated = DateTime.Now;
        if (latestRemoteMetadata != null)
        {
            UpdatePlaylistSourceMetadata(playlist, latestRemoteMetadata);
        }
        await _context.SaveChangesAsync();

        // Re-aggregate only when there is a real delta.
        await _mediaService.AggregateContentAsync(playlist.Id);

        return playlist;
    }

    public async Task DeleteAsync(int playlistId)
    {
        var playlist = await _context.Playlists
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist != null)
        {
            playlist.IsActive = false;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<Channel>> GetChannelsAsync(int playlistId)
    {
        return await _context.Channels
            .Where(c => c.PlaylistId == playlistId)
            .OrderBy(c => c.GroupTitle)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Get channels with filtering and pagination for fast loading
    /// </summary>
    public async Task<List<Channel>> GetChannelsFilteredAsync(int playlistId, string? searchText = null, string? group = null, ChannelType? type = null, bool onlyFavorites = false, int limit = 1000, ChannelSortOrder sortOrder = ChannelSortOrder.NewestFirst)
    {
        var query = BuildFilteredChannelQuery(playlistId, searchText, group, type, onlyFavorites);
        
        return await ApplySort(query, sortOrder)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Channel>> GetChannelsFilteredPageAsync(int playlistId, int skip, int take, string? searchText = null, string? group = null, ChannelType? type = null, bool onlyFavorites = false, ChannelSortOrder sortOrder = ChannelSortOrder.NewestFirst)
    {
        var query = BuildFilteredChannelQuery(playlistId, searchText, group, type, onlyFavorites);

        return await ApplySort(query, sortOrder)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(1, take))
            .ToListAsync();
    }

    /// <summary>
    /// Get only group names for fast initial loading
    /// </summary>
    public async Task<List<string>> GetGroupsAsync(int playlistId)
    {
        return await _context.Channels
            .Where(c => c.PlaylistId == playlistId && c.GroupTitle != null)
            .Select(c => c.GroupTitle!.Trim())
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();
    }

    public async Task<List<string>> GetGroupsByTypeAsync(int playlistId, ChannelType type)
    {
        return await _context.Channels
            .Where(c => c.PlaylistId == playlistId && c.Type == type && c.GroupTitle != null)
            .Select(c => c.GroupTitle!.Trim())
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();
    }

    /// <summary>
    /// Get channel count without loading all channels
    /// </summary>
    public async Task<int> GetChannelCountAsync(int playlistId)
    {
        return await _context.Channels
            .Where(c => c.PlaylistId == playlistId)
            .CountAsync();
    }

    private static IOrderedQueryable<Channel> ApplySort(IQueryable<Channel> query, ChannelSortOrder sortOrder)
    {
        return sortOrder switch
        {
            ChannelSortOrder.OldestFirst => query.OrderBy(c => c.Id),
            ChannelSortOrder.NameAsc => query.OrderBy(c => c.Name).ThenBy(c => c.Id),
            ChannelSortOrder.NameDesc => query.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id),
            _ => query.OrderByDescending(c => c.Id)
        };
    }

    private IQueryable<Channel> BuildFilteredChannelQuery(int playlistId, string? searchText, string? group, ChannelType? type, bool onlyFavorites)
    {
        var query = _context.Channels.Where(c => c.PlaylistId == playlistId);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(search) ||
                                     (c.GroupTitle != null && c.GroupTitle.ToLower().Contains(search)));
        }

        if (!string.IsNullOrEmpty(group))
        {
            var normalizedGroup = group.Trim();
            query = query.Where(c =>
                c.GroupTitle != null &&
                (c.GroupTitle == normalizedGroup || c.GroupTitle.Trim() == normalizedGroup));
        }

        if (type.HasValue)
        {
            query = query.Where(c => c.Type == type.Value);
        }

        if (onlyFavorites)
        {
            query = query.Where(c => c.IsFavorite);
        }

        return query;
    }
    public async Task UpdateProviderExpirationAsync(int providerId, DateTime expirationDate)
    {
        var account = await _context.ProviderAccounts.FindAsync(providerId);
        if (account != null)
        {
            account.ExpirationDate = expirationDate;
            await _context.SaveChangesAsync();
        }
    }

    private static string BuildChannelFingerprint(Channel channel)
    {
        var typeKey = ((int)channel.Type).ToString();
        var tvgId = NormalizeIdentityToken(channel.TvgId);
        if (!string.IsNullOrWhiteSpace(tvgId))
        {
            return $"tvgid|{typeKey}|{tvgId}";
        }

        var tvgName = NormalizeIdentityToken(channel.TvgName);
        var name = NormalizeIdentityToken(channel.Name);
        var group = NormalizeIdentityToken(channel.GroupTitle);

        if (!string.IsNullOrWhiteSpace(tvgName))
        {
            return $"tvgname|{typeKey}|{tvgName}|{group}";
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return $"name|{typeKey}|{name}|{group}";
        }

        var streamPath = NormalizeStreamIdentity(channel.StreamUrl);
        return $"stream|{typeKey}|{streamPath}";
    }

    private static string NormalizeIdentityToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return string.Join(" ", raw
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeStreamIdentity(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return string.Empty;
        }

        var trimmed = streamUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            var path = uri.AbsolutePath.Trim().ToLowerInvariant();
            return $"{host}{path}";
        }

        var q = trimmed.IndexOf('?');
        var pathOnly = q >= 0 ? trimmed[..q] : trimmed;
        return pathOnly.Trim().ToLowerInvariant();
    }

    private async Task<RemotePlaylistMetadata?> TryFetchRemoteMetadataAsync(string url, Playlist playlist)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            if (!string.IsNullOrWhiteSpace(playlist.SourceEtag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", playlist.SourceEtag);
            }

            if (playlist.SourceLastModified.HasValue)
            {
                request.Headers.IfModifiedSince = playlist.SourceLastModified.Value;
            }

            using var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new RemotePlaylistMetadata
                {
                    IsUnchanged = true,
                    Etag = playlist.SourceEtag,
                    LastModified = playlist.SourceLastModified,
                    ContentLength = playlist.SourceContentLength
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var etag = response.Headers.ETag?.Tag;
            var lastModified = response.Content.Headers.LastModified?.UtcDateTime;
            var contentLength = response.Content.Headers.ContentLength;

            var unchanged = false;
            if (!string.IsNullOrWhiteSpace(etag) &&
                !string.IsNullOrWhiteSpace(playlist.SourceEtag) &&
                string.Equals(etag, playlist.SourceEtag, StringComparison.Ordinal))
            {
                unchanged = true;
            }
            else if (lastModified.HasValue && playlist.SourceLastModified.HasValue &&
                     lastModified.Value == playlist.SourceLastModified.Value &&
                     contentLength.HasValue && playlist.SourceContentLength.HasValue &&
                     contentLength.Value == playlist.SourceContentLength.Value)
            {
                unchanged = true;
            }

            return new RemotePlaylistMetadata
            {
                IsUnchanged = unchanged,
                Etag = etag,
                LastModified = lastModified,
                ContentLength = contentLength
            };
        }
        catch
        {
            return null;
        }
    }

    private static void UpdatePlaylistSourceMetadata(Playlist playlist, RemotePlaylistMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Etag))
        {
            playlist.SourceEtag = metadata.Etag;
        }

        if (metadata.LastModified.HasValue)
        {
            playlist.SourceLastModified = metadata.LastModified.Value;
        }

        if (metadata.ContentLength.HasValue)
        {
            playlist.SourceContentLength = metadata.ContentLength.Value;
        }
    }

    private sealed class RemotePlaylistMetadata
    {
        public bool IsUnchanged { get; set; }
        public string? Etag { get; set; }
        public DateTime? LastModified { get; set; }
        public long? ContentLength { get; set; }
    }

    public async Task RefreshEpgAsync(int playlistId)
    {
        var playlist = await _context.Playlists
            .Include(p => p.Channels)
            .FirstOrDefaultAsync(p => p.Id == playlistId);
        
        if (playlist == null) return;

        var channels = playlist.Channels.ToList();
        
        var channelNames = channels.Select(c => c.Name ?? "").ToList();
        var countryCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(playlist.DetectedCountry))
        {
            countryCandidates.Add(playlist.DetectedCountry);
        }

        foreach (var country in _languageDetection.DetectCountries(channelNames)
            .Where(c => c.Percentage > 10 || c.ChannelCount > 5)
            .Select(c => c.CountryCode))
        {
            if (!countryCandidates.Contains(country, StringComparer.OrdinalIgnoreCase))
            {
                countryCandidates.Add(country);
            }
        }

        if (countryCandidates.Count == 0)
        {
            countryCandidates.Add("TR");
        }

        playlist.DetectedCountry = countryCandidates[0];

        // EPG kaynaklarını çöz (çoklu ülke + tekilleştirme)
        var epgSources = countryCandidates
            .Take(3)
            .SelectMany(country => _epgSourceResolver.ResolveEpgSources(
                country,
                m3uEpgUrl: playlist.EpgUrl))
            .GroupBy(source => source.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(source => source.Priority)
            .ToList();

        for (var i = 0; i < epgSources.Count; i++)
        {
            epgSources[i].ClearBeforeLoad = (i == 0);
        }

        string? lastError = null;
        foreach (var source in epgSources)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg trying: {source.Type} - {source.Url}");
                // Eğer temizlik gerekiyorsa
                if (source.ClearBeforeLoad)
                {
                    await _epgService.ClearEpgAsync();
                }

                var beforeCount = await _epgService.GetTotalProgramCountAsync();
                await _epgService.LoadEpgAsync(source.Url, source.IsPrimary, channels);
                var afterCount = await _epgService.GetTotalProgramCountAsync();
                if (afterCount <= beforeCount)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg no program loaded: {source.Type}");
                    continue;
                }

                playlist.EpgUrl = source.Url;
                playlist.EpgLastUpdated = DateTime.Now;
                playlist.EpgLastError = null;
                await _context.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg success: {source.Type} (+{afterCount - beforeCount})");
                return;
            }
            catch (Exception ex)
            {
                lastError = $"{source.Type}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg failed: {source.Type} - {ex.Message}");
            }
        }

        playlist.EpgLastError = string.IsNullOrWhiteSpace(lastError)
            ? "EPG kaynaklarindan veri alinamadi."
            : lastError;
        await _context.SaveChangesAsync();
    }

    private static string? NormalizeEpgUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var normalized = url.Trim().Trim('"', '\'');
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString();
        }

        return null;
    }
}





