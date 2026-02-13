using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface IStalkerPortalService
{
    Task<bool> AuthenticateAsync(string portalUrl, string macAddress, CancellationToken cancellationToken = default);

    Task<List<Channel>> GetChannelsAsync(
        string portalUrl,
        string macAddress,
        bool includeVod = true,
        CancellationToken cancellationToken = default);
}

