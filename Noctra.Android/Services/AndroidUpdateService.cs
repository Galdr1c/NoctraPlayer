using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Android update service. It keeps the existing manifest check but opens a
/// mobile-appropriate target: update download URL first, then Play Store,
/// then the market:// deep link when available.
/// </summary>
public sealed class AndroidUpdateService : IUpdateService
{
    private const string UpdateManifestUrl = "https://raw.githubusercontent.com/Galdr1c/NoctraPlayer/main/update.json";

    private readonly HttpClient _httpClient;
    private readonly Context _context;
    private readonly AndroidActivityProvider _activityProvider;

    public AndroidUpdateService(
        HttpClient httpClient,
        Context context,
        AndroidActivityProvider activityProvider)
    {
        _httpClient = httpClient;
        _context = context.ApplicationContext ?? context;
        _activityProvider = activityProvider;
    }

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<UpdateInfo>(UpdateManifestUrl, cancellationToken);
            if (response is null)
            {
                return null;
            }

            return IsNewerVersion(response.Version, CurrentVersion) ? response : null;
        }
        catch
        {
            return null;
        }
    }

    public Task<bool> StartUpdateAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        if (!string.IsNullOrWhiteSpace(updateInfo.DownloadUrl) && TryOpenUri(updateInfo.DownloadUrl))
        {
            return Task.FromResult(true);
        }

        var packageName = _context.PackageName;
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            if (TryOpenUri($"https://play.google.com/store/apps/details?id={packageName}"))
            {
                return Task.FromResult(true);
            }

            if (TryOpenUri($"market://details?id={packageName}"))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    private bool TryOpenUri(string uri)
    {
        try
        {
            var activity = _activityProvider.CurrentActivity;
            var startContext = (Context?)activity ?? _context;

            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri));
            if (activity is null)
            {
                intent.AddFlags(ActivityFlags.NewTask);
            }

            startContext.StartActivity(intent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNewerVersion(string remoteVersion, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(remoteVersion))
        {
            return false;
        }

        remoteVersion = remoteVersion.Trim().TrimStart('v', 'V');
        currentVersion = currentVersion.Trim().TrimStart('v', 'V');

        if (Version.TryParse(remoteVersion, out var remote) &&
            Version.TryParse(currentVersion, out var current))
        {
            return remote > current;
        }

        return string.Compare(remoteVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
