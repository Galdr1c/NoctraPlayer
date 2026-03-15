using Noctra.Models;
using Noctra.Services.Interfaces;
using System.Net.Http.Json;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noctra.Services;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private const string UpdateManifestUrl = "https://raw.githubusercontent.com/Galdr1c/IPTVMediaPlayer/main/update.json";

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // In a real scenario, this would fetch from a real URL.
            // For now, mirroring the version check logic.
            var response = await _httpClient.GetFromJsonAsync<UpdateInfo>(UpdateManifestUrl, cancellationToken);
            if (response == null) return null;

            if (IsNewerVersion(response.Version, CurrentVersion))
            {
                return response;
            }
        }
        catch
        {
            // Silently fail if update check fails (no internet, etc.)
        }

        return null;
    }

    public Task<bool> StartUpdateAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
            return Task.FromResult(false);

        try
        {
            // For simple Windows apps without MSIX/Squirrel integrated, 
            // the safest cross-platform way is to open the download page.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateInfo.DownloadUrl,
                    UseShellExecute = true
                });
                return Task.FromResult(true);
            }
        }
        catch
        {
            // Ignore failure
        }

        return Task.FromResult(false);
    }

    private static bool IsNewerVersion(string remoteVersion, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(remoteVersion)) return false;

        // Clean versions (remove leading 'v', 'V' or spaces)
        remoteVersion = remoteVersion.Trim().TrimStart('v', 'V');
        currentVersion = currentVersion.Trim().TrimStart('v', 'V');

        if (Version.TryParse(remoteVersion, out var remote) && 
            Version.TryParse(currentVersion, out var current))
        {
            return remote > current;
        }
        
        // Fallback for non-standard versions (e.g. "1.0.0-beta")
        try 
        {
            return string.Compare(remoteVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
        }
        catch 
        {
            return false;
        }
    }
}
