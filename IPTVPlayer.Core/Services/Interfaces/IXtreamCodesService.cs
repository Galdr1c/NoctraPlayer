using IPTVPlayer.Models;

namespace IPTVPlayer.Services.Interfaces;

public interface IXtreamCodesService
{
    Task<bool> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default);

    Task<List<Channel>> GetChannelsAsync(
        string baseUrl,
        string username,
        string password,
        bool includeSeriesEpisodes = true,
        CancellationToken cancellationToken = default);
}
