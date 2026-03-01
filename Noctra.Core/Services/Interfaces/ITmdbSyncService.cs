using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

public interface ITmdbSyncService
{
    /// <summary>
    /// Starts the background sync process. Returns immediately.
    /// </summary>
    void StartSync();

    /// <summary>
    /// Force triggers an immediate sync pass, even if it's currently sleeping.
    /// </summary>
    void TriggerSync();

    /// <summary>
    /// Stops the background sync process gracefully.
    /// </summary>
    void StopSync();
}
