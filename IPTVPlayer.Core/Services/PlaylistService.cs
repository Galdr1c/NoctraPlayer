using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Data;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

/// <summary>
/// Playlist yönetim servisi
/// </summary>
public class PlaylistService : IPlaylistService
{
    private readonly AppDbContext _context;
    private readonly IM3UParser _parser;
    private readonly IMediaService _mediaService;

    public PlaylistService(AppDbContext context, IM3UParser parser, IMediaService mediaService)
    {
        _context = context;
        _parser = parser;
        _mediaService = mediaService;
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
            
            var playlist = new Playlist
            {
                Name = name,
                Url = url,
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

                // Add channels in batches - increased batch size for speed
                const int batchSize = 1000;
                for (int i = 0; i < channels.Count; i += batchSize)
                {
                    var batch = channels.Skip(i).Take(batchSize).ToList();
                    foreach (var channel in batch)
                    {
                        channel.PlaylistId = playlist.Id;
                    }
                    _context.Channels.AddRange(batch);
                    await _context.SaveChangesAsync();
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] Saved batch {i / batchSize + 1}, total saved: {Math.Min(i + batchSize, channels.Count)}");
                }
                
                // Aggregation for Series/VOD
                await _mediaService.AggregateContentAsync(playlist.Id);
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] Completed aggregation");

                return playlist;
            }
            finally
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddFromUrlAsync error: {ex}");
            throw;
        }
    }

    public async Task<Playlist> AddFromFileAsync(string name, string filePath, int? profileId = null)
    {
        try 
        {
            var channels = await _parser.ParseFromFileAsync(filePath);
            
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

        // Yeni kanalları ekle
        foreach (var channel in newChannels)
        {
            channel.PlaylistId = playlist.Id;
            _context.Channels.Add(channel);
        }

        playlist.ChannelCount = newChannels.Count;
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
    public async Task<List<Channel>> GetChannelsFilteredAsync(int playlistId, string? searchText = null, string? group = null, ChannelType? type = null, int limit = 1000)
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
        
        return await query
            .OrderBy(c => c.GroupTitle)
            .ThenBy(c => c.Name)
            .Take(limit)
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
}
