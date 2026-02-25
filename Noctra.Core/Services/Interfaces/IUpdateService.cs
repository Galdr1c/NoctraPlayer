using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<bool> StartUpdateAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default);
}
