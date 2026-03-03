using Noctra.Models;
using Noctra.Services;

namespace Noctra.Avalonia.Services;

public class StubMetadataService : IMetadataService
{
    public Task<ChannelMetadata?> FetchMetadataAsync(string searchQuery, ChannelType? type = null, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
        => Task.FromResult<ChannelMetadata?>(null);

    public Task EnrichChannelAsync(Channel channel, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task EnrichChannelsAsync(IEnumerable<Channel> channels, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<List<string>> GetGenresAsync(List<int> genreIds, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
        => Task.FromResult(new List<string>());

    public Task<TmdbDetail?> FetchSeriesDetailsAsync(int tmdbId, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
        => Task.FromResult<TmdbDetail?>(null);

    public Task<TmdbSeasonDetail?> FetchSeasonDetailsAsync(int tmdbId, int seasonNumber, string languageCode = "tr-TR", CancellationToken cancellationToken = default)
        => Task.FromResult<TmdbSeasonDetail?>(null);

    public Task<ChannelMetadata?> SearchSeriesAsync(string searchQuery, string languageCode = "tr-TR", CancellationToken cancellationToken = default) 
        => Task.FromResult<ChannelMetadata?>(null);

    public void ApplyHeuristics(TmdbDetail details, ChannelMetadata metadata, string languageCode, string? contextTitle)
    {
        // No-op for stub
    }

    public void ClearCache()
    {
    }
}
