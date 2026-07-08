using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Service for monitoring and reporting network connection status
/// </summary>
public interface INetworkService
{
    /// <summary>
    /// Gets the current network kind token (e.g. "Wi-Fi", "Ethernet", "Cellular", "Offline", "Unvalidated", "Online", "Unknown") — localize for display.
    /// </summary>
    string CurrentNetworkStatus { get; }

    /// <summary>
    /// Raised when the network status changes
    /// </summary>
    event EventHandler<string>? NetworkStatusChanged;
}
