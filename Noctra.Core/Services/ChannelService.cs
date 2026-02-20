using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

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
        Channel? dbChannel = null;

        if (channel.Id > 0)
        {
            dbChannel = await _context.Channels.FindAsync(channel.Id);
        }

        if (dbChannel == null)
        {
            dbChannel = await _context.Channels
                .FirstOrDefaultAsync(c =>
                    c.PlaylistId == channel.PlaylistId &&
                    c.StreamUrl == channel.StreamUrl &&
                    c.Name == channel.Name);
        }

        if (dbChannel != null)
        {
            dbChannel.IsFavorite = channel.IsFavorite;
            dbChannel.IsInMyList = channel.IsInMyList;
            dbChannel.LastWatched = channel.LastWatched;
            
            await _context.SaveChangesAsync();
        }
    }
}
