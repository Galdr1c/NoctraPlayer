using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Data;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

public class ChannelService : IChannelService
{
    private readonly AppDbContext _context;

    public ChannelService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Channel?> GetByIdAsync(int id)
    {
        return await _context.Channels.FindAsync(id);
    }

    public async Task UpdateChannelAsync(Channel channel)
    {
        // Context tracking issues can occur if we just attach, 
        // especially with navigation properties like Playlist.
        // Safer to find and update only specific fields or use EntityState.Modified.
        
        var dbChannel = await _context.Channels.FindAsync(channel.Id);
        if (dbChannel != null)
        {
            dbChannel.IsFavorite = channel.IsFavorite;
            dbChannel.IsInMyList = channel.IsInMyList;
            dbChannel.LastWatched = channel.LastWatched;
            
            _context.Channels.Update(dbChannel);
            await _context.SaveChangesAsync();
        }
    }
}
