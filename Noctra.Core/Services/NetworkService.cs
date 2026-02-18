using System.Net.NetworkInformation;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Implementation of INetworkService using System.Net.NetworkInformation
/// </summary>
public class NetworkService : INetworkService, IDisposable
{
    private string _currentStatus = "Bilinmiyor";
    public string CurrentNetworkStatus => _currentStatus;

    public event EventHandler<string>? NetworkStatusChanged;

    public NetworkService()
    {
        UpdateStatus();
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        var oldStatus = _currentStatus;
        UpdateStatus();
        
        if (oldStatus != _currentStatus)
        {
            NetworkStatusChanged?.Invoke(this, _currentStatus);
        }
    }

    private void UpdateStatus()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && 
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            if (interfaces.Count == 0)
            {
                _currentStatus = "Offline";
                return;
            }

            // Simple filter for virtual adapters
            var realInterfaces = interfaces.Where(n => 
                !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("Pseudo", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("Microsoft Loopback", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("Wpcap", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // Use real interfaces if any, fallback to all if none (e.g. maybe we over-filtered)
            var targetInterfaces = realInterfaces.Any() ? realInterfaces : interfaces;

            // Prioritize the interface that has a gateway (likely the internet path)
            var interfaceWithGateway = targetInterfaces.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Any());
            
            if (interfaceWithGateway != null)
            {
                _currentStatus = GetFriendlyName(interfaceWithGateway.NetworkInterfaceType);
            }
            else
            {
                // Fallback to priority list if no gateway found
                if (targetInterfaces.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || 
                                           n.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet ||
                                           n.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT ||
                                           n.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx))
                {
                    _currentStatus = "Ethernet";
                }
                else if (targetInterfaces.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                {
                    _currentStatus = "Wi-Fi";
                }
                else if (targetInterfaces.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wwanpp || 
                                           n.NetworkInterfaceType == NetworkInterfaceType.Wwanpp2))
                {
                    _currentStatus = "Mobil veri";
                }
                else
                {
                    _currentStatus = "Çevrimiçi";
                }
            }
        }
        catch (Exception ex)
        {
            _currentStatus = "Bilinmiyor";
        }
    }

    private string GetFriendlyName(NetworkInterfaceType type)
    {
        return type switch
        {
            NetworkInterfaceType.Ethernet => "Ethernet",
            NetworkInterfaceType.GigabitEthernet => "Ethernet",
            NetworkInterfaceType.FastEthernetT => "Ethernet",
            NetworkInterfaceType.FastEthernetFx => "Ethernet",
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Wwanpp => "Mobil veri",
            NetworkInterfaceType.Wwanpp2 => "Mobil veri",
            _ => "Çevrimiçi"
        };
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
    }
}
