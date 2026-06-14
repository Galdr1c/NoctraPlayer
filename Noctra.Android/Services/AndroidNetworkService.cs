using System;
using Android.Content;
using Android.Net;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidNetworkService : INetworkService, IDisposable
{
    private readonly ConnectivityManager _connectivityManager;
    private readonly ConnectivityCallback _callback;
    private string _currentNetworkStatus;
    private bool _disposed;

    public AndroidNetworkService(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _connectivityManager = context.GetSystemService(Context.ConnectivityService)
            as ConnectivityManager
            ?? throw new InvalidOperationException("Android ConnectivityManager is unavailable.");
        _currentNetworkStatus = ReadCurrentStatus();
        _callback = new ConnectivityCallback(this);
        _connectivityManager.RegisterDefaultNetworkCallback(_callback);
    }

    public string CurrentNetworkStatus => _currentNetworkStatus;

    public event EventHandler<string>? NetworkStatusChanged;

    private void RefreshStatus()
    {
        var status = ReadCurrentStatus();
        if (string.Equals(status, _currentNetworkStatus, StringComparison.Ordinal))
        {
            return;
        }

        _currentNetworkStatus = status;
        NetworkStatusChanged?.Invoke(this, status);
    }

    private string ReadCurrentStatus()
    {
        var activeNetwork = _connectivityManager.ActiveNetwork;
        if (activeNetwork is null)
        {
            return "Offline";
        }

        var capabilities = _connectivityManager.GetNetworkCapabilities(activeNetwork);
        if (capabilities is null)
        {
            return "Unknown";
        }

        if (capabilities.HasTransport(TransportType.Wifi))
        {
            return "Wi-Fi";
        }

        if (capabilities.HasTransport(TransportType.Ethernet))
        {
            return "Ethernet";
        }

        if (capabilities.HasTransport(TransportType.Cellular))
        {
            return "Cellular";
        }

        return "Online";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _connectivityManager.UnregisterNetworkCallback(_callback);
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            // The callback may already be detached during process shutdown.
        }
    }

    private sealed class ConnectivityCallback : ConnectivityManager.NetworkCallback
    {
        private readonly AndroidNetworkService _owner;

        public ConnectivityCallback(AndroidNetworkService owner)
        {
            _owner = owner;
        }

        public override void OnAvailable(Network network)
        {
            base.OnAvailable(network);
            _owner.RefreshStatus();
        }

        public override void OnLost(Network network)
        {
            base.OnLost(network);
            _owner.RefreshStatus();
        }

        public override void OnCapabilitiesChanged(
            Network network,
            NetworkCapabilities networkCapabilities)
        {
            base.OnCapabilitiesChanged(network, networkCapabilities);
            _owner.RefreshStatus();
        }
    }
}
