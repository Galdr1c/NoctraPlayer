using System.Diagnostics;
using System.Globalization;
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
            return new UpdateCheckResult { Status = UpdateCheckStatus.Unsupported };
        }

        try
        {
            var context = StoreContext.GetDefault();
            if (context == null) return new UpdateCheckResult { Status = UpdateCheckStatus.Error, ErrorMessage = "StoreContext unavailable" };

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(10));

            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates == null || updates.Count == 0)
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            }

            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                LatestVersion = string.Format(
                    CultureInfo.InvariantCulture,
                    updates.Count == 1 ? "{0} {1}" : "{0} {1}",
                    updates.Count,
                    updates.Count == 1 ? "update" : "updates"),
                Changelog = $"{updates.Count} package update(s) available",
                IsMandatory = false
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Error, ErrorMessage = "Check timed out" };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MicrosoftStoreUpdateService] Check failed: {ex.Message}");
            return new UpdateCheckResult { Status = UpdateCheckStatus.Error, ErrorMessage = ex.Message };
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

            // RequestDownloadAndInstallStorePackageUpdatesAsync shows a modal dialog.
            // Without HWND binding this may fail in unpackaged builds with ERROR_INVALID_WINDOW_HANDLE.
            // Try/catch provides a user-friendly fallback.
            var installOperation = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            if (installOperation == null) return false;

            var overallState = installOperation.OverallState;
            Debug.WriteLine($"[MicrosoftStoreUpdateService] Update result: {overallState}");

            return overallState switch
            {
                StorePackageUpdateState.Completed => true,
                StorePackageUpdateState.Canceled => false,
                StorePackageUpdateState.ErrorLowBattery => false,
                StorePackageUpdateState.ErrorWiFiRecommended => false,
                StorePackageUpdateState.ErrorWiFiRequired => false,
                _ => true
            };
        }
        catch (Exception ex)
        {
            // HWND hatası veya Store API hatası — kullanıcıya bilgi ver
            Debug.WriteLine($"[MicrosoftStoreUpdateService] StartUpdate failed: {ex.Message}");
            return false;
        }
    }

    public Task<bool> CompleteUpdateAsync(CancellationToken cancellationToken = default)
    {
        // Microsoft Store API'sinde manuel complete yok, Store kendi yönetir
        return Task.FromResult(false);
    }

    public Task<UpdateCheckResult> CheckPendingUpdateAsync(CancellationToken cancellationToken = default)
    {
        // Microsoft Store kendi güncelleme döngüsünü yönetir
        return Task.FromResult(new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate });
    }
}
