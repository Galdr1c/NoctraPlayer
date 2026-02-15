using Noctra.Models;
using Noctra.Services;

namespace Noctra.Avalonia.Services;

public sealed class StubMetadataService : IMetadataService
{
    public Task<ChannelMetadata?> FetchMetadataAsync(string searchQuery, ChannelType? type = null)
        => Task.FromResult<ChannelMetadata?>(null);

    public Task EnrichChannelAsync(Channel channel) => Task.CompletedTask;

    public Task EnrichChannelsAsync(IEnumerable<Channel> channels, IProgress<int>? progress = null)
        => Task.CompletedTask;

    public Task<List<string>> GetGenresAsync(List<int> genreIds)
        => Task.FromResult(new List<string>());

    public void ClearCache()
    {
    }
}

