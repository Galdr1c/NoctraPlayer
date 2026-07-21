using System.Diagnostics;
using Noctra.Services.Interfaces;
using Windows.Services.Store;

namespace Noctra.Avalonia.Services;

/// <summary>
/// Microsoft Store API kullanarak güncelleme kontrolü yapan Windows servisi.
/// Sadece MSIX/Appx paketlenmiş uygulamalarda çalışır.
/// Unpackaged/debug build'lerde NoOpUpdateService kullanılır.
/// </summary>
public sealed class MicrosoftStoreUpdateService : IAppUpdateService
{
    private readonly IPackageIdentityService? _packageIdentityService;

    public MicrosoftStoreUpdateService(IPackageIdentityService? packageIdentityService = null)
    {
        _packageIdentityService = packageIdentityService;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_packageIdentityService?.IsPackaged != true)
        {
            return new UpdateCheckResult { IsUpdateAvailable = false };
        }

        try
        {
            var context = StoreContext.GetDefault();
            if (context == null) return new UpdateCheckResult { IsUpdateAvailable = false };

            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates == null || updates.Count == 0)
            {
                return new UpdateCheckResult { IsUpdateAvailable = false };
            }

            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersion = null,
                Changelog = $"{updates.Count} package update(s) available",
                IsMandatory = false
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MicrosoftStoreUpdateService] Check failed: {ex.Message}");
            return new UpdateCheckResult { IsUpdateAvailable = false };
        }
    }

    public async Task<bool> StartUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_packageIdentityService?.IsPackaged != true) return false;

        try
        {
            var context = StoreContext.GetDefault();
            if (context == null) return false;

            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates == null || updates.Count == 0) return false;

            var installOperation = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            if (installOperation == null) return false;

            Debug.WriteLine($"[MicrosoftStoreUpdateService] Update flow started for {updates.Count} packages");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MicrosoftStoreUpdateService] StartUpdate failed: {ex.Message}");
            return false;
        }
    }
}
