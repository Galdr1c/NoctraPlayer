using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class ChannelService : IChannelService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ChannelService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Channel?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Channels.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task UpdateChannelAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        Channel? dbChannel = null;

        if (channel.Id > 0)
        {
            dbChannel = await context.Channels.FindAsync(new object[] { channel.Id }, cancellationToken);
        }

        if (dbChannel == null)
        {
            dbChannel = await context.Channels
                .FirstOrDefaultAsync(c =>
                    c.PlaylistId == channel.PlaylistId &&
                    c.StreamUrl == channel.StreamUrl &&
                    c.Name == channel.Name, cancellationToken);
        }

        if (dbChannel != null)
        {
            dbChannel.IsFavorite = channel.IsFavorite;
            dbChannel.IsInMyList = channel.IsInMyList;
            dbChannel.LastWatched = channel.LastWatched;
            
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
