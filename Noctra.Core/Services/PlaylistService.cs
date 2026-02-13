using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Noctra.Services;

/// <summary>
/// Playlist yönetim servisi
/// </summary>
public class PlaylistService : IPlaylistService
{
    private readonly AppDbContext _context;
    private readonly IM3UParser _parser;
    private readonly IMediaService _mediaService;
    private readonly IPlaylistOrganizerService _organizer;
    private readonly LanguageDetectionService _languageDetection;
    private readonly EpgSourceResolver _epgSourceResolver;
    private readonly IEpgService _epgService;
    private readonly IServiceScopeFactory _scopeFactory;

    public PlaylistService(
        AppDbContext context, 
        IM3UParser parser, 
        IMediaService mediaService, 
        IPlaylistOrganizerService organizer,
        LanguageDetectionService languageDetection,
        EpgSourceResolver epgSourceResolver,
        IEpgService epgService,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _parser = parser;
        _mediaService = mediaService;
        _organizer = organizer;
        _languageDetection = languageDetection;
        _epgSourceResolver = epgSourceResolver;
        _epgService = epgService;
        _scopeFactory = scopeFactory;
    }

    public async Task<Playlist> AddFromUrlAsync(string name, string url, int? profileId = null)
    {
        try 
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] AddFromUrlAsync: {name} - {url}");
            
            // Check existing first to avoid potential parsing overhead
            var existing = await _context.Playlists
                .FirstOrDefaultAsync(p => p.Url == url && p.ProfileId == profileId && p.IsActive);
                
            if (existing != null) 
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] Found existing playlist: {existing.Id} with {existing.ChannelCount} channels");
                return existing;
            }

            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Downloading and parsing M3U from: {url}");
            var channels = await _parser.ParseFromUrlAsync(url);
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Parsed {channels.Count} channels from M3U");

            // Otomatik organizasyon: dedup, kategorize, sıralama
            var organized = _organizer.Organize(channels);
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Organized: {channels.Count} → {organized.Count} channels");

            return await AddFromChannelsAsync(name, url, organized, profileId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddFromUrlAsync error: {ex}");
            throw;
        }
    }

    public async Task<Playlist> AddFromChannelsAsync(string name, string sourceUrl, IReadOnlyCollection<Channel> channels, int? profileId = null)
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
            IsActive = true
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
                    var detectedCountry = _languageDetection.DetectCountry(channelNames);
                    var epgSources = _epgSourceResolver.ResolveEpgSources(detectedCountry);

                    string? usedEpgUrl = null;
                    foreach (var source in epgSources)
                    {
                        try
                        {
                            if (source.ClearBeforeLoad)
                            {
                                await scopedEpgService.ClearEpgAsync();
                            }

                            System.Diagnostics.Debug.WriteLine($"[AutoEPG] Loading from {source.Url}");
                            await scopedEpgService.LoadEpgAsync(source.Url, source.IsPrimary, channelSnapshot);
                            usedEpgUrl = source.Url;
                            System.Diagnostics.Debug.WriteLine($"[PlaylistService] EPG loaded from {source.Type}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[PlaylistService] EPG source failed: {source.Type} - {ex.Message}");
                        }
                    }

                    var playlistToUpdate = await scopedDb.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
                    if (playlistToUpdate != null)
                    {
                        playlistToUpdate.DetectedCountry = detectedCountry;
                        playlistToUpdate.EpgUrl = usedEpgUrl;
                        playlistToUpdate.EpgLastUpdated = DateTime.Now;
                        await scopedDb.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] Auto-EPG error: {ex.Message}");
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
            var channels = _organizer.Organize(rawChannels);
            
            var playlist = new Playlist
            {
                Name = name,
                FilePath = filePath,
                ProfileId = profileId,
                CreatedAt = DateTime.Now,
                LastUpdated = DateTime.Now,
                ChannelCount = channels.Count,
                IsActive = true
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

        // Mevcut kanalları sil
        _context.Channels.RemoveRange(playlist.Channels);

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

        // Yeni kanalları ekle
        foreach (var channel in organizedChannels)
        {
            channel.PlaylistId = playlist.Id;
            _context.Channels.Add(channel);
        }

        playlist.ChannelCount = organizedChannels.Count;
        playlist.LastUpdated = DateTime.Now;

        await _context.SaveChangesAsync();

        // Re-aggregate
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
            .Select(c => c.GroupTitle!)
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
            query = query.Where(c => c.GroupTitle == group);
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

    public async Task RefreshEpgAsync(int playlistId)
    {
        var playlist = await _context.Playlists
            .Include(p => p.Channels)
            .FirstOrDefaultAsync(p => p.Id == playlistId);
        
        if (playlist == null) return;

        var channels = playlist.Channels.ToList();
        
        // Ülke tespiti (yoksa yap)
        if (string.IsNullOrEmpty(playlist.DetectedCountry))
        {
            var channelNames = channels.Select(c => c.Name ?? "").ToList();
            playlist.DetectedCountry = _languageDetection.DetectCountry(channelNames);
        }

        // EPG kaynaklarını çöz
        var epgSources = _epgSourceResolver.ResolveEpgSources(
            playlist.DetectedCountry ?? "TR",
            m3uEpgUrl: playlist.EpgUrl);

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

                await _epgService.LoadEpgAsync(source.Url, source.IsPrimary, channels);
                playlist.EpgUrl = source.Url;
                playlist.EpgLastUpdated = DateTime.Now;
                await _context.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg success: {source.Type}");
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg failed: {source.Type} - {ex.Message}");
            }
        }
    }
}


