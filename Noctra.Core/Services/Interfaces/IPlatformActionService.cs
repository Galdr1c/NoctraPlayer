using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Opens platform-owned experiences such as browser pages or file managers.
/// ViewModels should use this instead of System.Diagnostics.Process.Start so
/// mobile builds can route the action through Android intents.
/// </summary>
public interface IPlatformActionService
{
    Task<bool> OpenUrlAsync(string? url, CancellationToken cancellationToken = default);
    Task<bool> OpenDirectoryAsync(string? directoryPath, CancellationToken cancellationToken = default);
}
