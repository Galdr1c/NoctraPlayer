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
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IM3UParser _parser;
    private readonly IMediaService _mediaService;

    public PlaylistService(IDbContextFactory<AppDbContext> contextFactory, IM3UParser parser, IMediaService mediaService)
    {
        _contextFactory = contextFactory;
        _parser = parser;
        _mediaService = mediaService;
    }

    public async Task<Playlist> AddFromUrlAsync(string name, string url, int? profileId = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        try 
        {
            // Duplicate check
            var existing = await context.Playlists
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
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            
            context.Playlists.Add(playlist);
            await context.SaveChangesAsync();

            // Add channels in batches
            const int batchSize = 500;
            for (int i = 0; i < channels.Count; i += batchSize)
            {
                var batch = channels.Skip(i).Take(batchSize).ToList();
                foreach (var channel in batch)
                {
                    channel.PlaylistId = playlist.Id;
                }
                context.Channels.AddRange(batch);
                await context.SaveChangesAsync();
            }

            // Aggregation for Series/VOD
            await _mediaService.AggregateContentAsync(playlist.Id);

            return playlist;
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task<Playlist> AddFromFileAsync(string name, string filePath, int? profileId = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
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

            context.ChangeTracker.AutoDetectChangesEnabled = false;
            context.Playlists.Add(playlist);
            await context.SaveChangesAsync();

            const int batchSize = 500;
            for (int i = 0; i < channels.Count; i += batchSize)
            {
                var batch = channels.Skip(i).Take(batchSize).ToList();
                foreach (var channel in batch)
                {
                    channel.PlaylistId = playlist.Id;
                }
                context.Channels.AddRange(batch);
                await context.SaveChangesAsync();
            }

            // Aggregation for Series/VOD
            await _mediaService.AggregateContentAsync(playlist.Id);

            return playlist;
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task<List<Playlist>> GetAllAsync(int? profileId = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.Playlists.Where(p => p.IsActive);
        
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
        using var context = await _contextFactory.CreateDbContextAsync();
        var playlist = await context.Playlists
            .Include(p => p.Channels)
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist == null)
            throw new KeyNotFoundException($"Playlist bulunamadı: {playlistId}");

        // Mevcut kanalları sil
        context.Channels.RemoveRange(playlist.Channels);

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
            context.Channels.Add(channel);
        }

        playlist.ChannelCount = newChannels.Count;
        playlist.LastUpdated = DateTime.Now;

        await context.SaveChangesAsync();

        // Re-aggregate
        await _mediaService.AggregateContentAsync(playlist.Id);

        return playlist;
    }

    public async Task DeleteAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var playlist = await context.Playlists
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist != null)
        {
            playlist.IsActive = false;
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<Channel>> GetChannelsAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Channels
            .Where(c => c.PlaylistId == playlistId)
            .OrderBy(c => c.GroupTitle)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }
}
