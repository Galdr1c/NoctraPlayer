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

    public PlaylistService(AppDbContext context, IM3UParser parser)
    {
        _context = context;
        _parser = parser;
    }

    public async Task<Playlist> AddFromUrlAsync(string name, string url, int? profileId = null)
    {
        try 
        {
            // Duplicate check
            var existing = await _context.Playlists
                .FirstOrDefaultAsync(p => p.Url == url && p.ProfileId == profileId && p.IsActive);
                
            if (existing != null) return existing;

            var channels = await _parser.ParseFromUrlAsync(url);
            
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

            // Optimization for large playlists
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            
            _context.Playlists.Add(playlist);
            await _context.SaveChangesAsync();

            // Add channels in batches
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
        // Change tracking optimization for bulk updates
        _context.ChangeTracker.AutoDetectChangesEnabled = false;

        try
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

            return playlist;
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
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
}
