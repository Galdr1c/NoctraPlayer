using System.Threading;
using System.Threading.Tasks;
using Noctra.Core.Advertising;

namespace Noctra.Core.Advertising;

/// <summary>
/// Fetches ad placement rules from the trusted endpoint. Any failure keeps the
/// current (ConservativeDefault) options — fail-closed. Exposed as an interface
/// so the startup pipeline and its tests do not depend on the HttpClient-backed
/// implementation.
/// </summary>
public interface IRemoteAdvertisingConfigService
{
    /// <summary>
    /// Raised on the fetch thread whenever <see cref="CurrentOptions"/> is
    /// replaced by a freshly parsed config. Consumers (e.g. feed controls via
    /// the provider's eligibility change) should rebuild their layout.
    /// </summary>
    event EventHandler? OptionsChanged;

    AdvertisingOptions CurrentOptions { get; }

    Task RefreshAsync(CancellationToken cancellationToken = default);
}