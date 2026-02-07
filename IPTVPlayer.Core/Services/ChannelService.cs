using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Data;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

public class ChannelService : IChannelService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ChannelService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Channel?> GetByIdAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Channels.FindAsync(id);
    }

    public async Task UpdateChannelAsync(Channel channel)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        
        // Context tracking issues can occur if we just attach, 
        // especially with navigation properties like Playlist.
        // Safer to find and update only specific fields or use EntityState.Modified.
        
        var dbChannel = await context.Channels.FindAsync(channel.Id);
        if (dbChannel != null)
        {
            dbChannel.IsFavorite = channel.IsFavorite;
            dbChannel.LastWatched = channel.LastWatched;
            
            context.Channels.Update(dbChannel);
            await context.SaveChangesAsync();
        }
    }
}
