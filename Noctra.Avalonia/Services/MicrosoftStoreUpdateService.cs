using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Noctra.Services.Interfaces;
using Windows.Services.Store;
using WinRT;

namespace Noctra.Avalonia.Services;

/// <summary>
/// Microsoft Store API kullanarak güncelleme kontrolü yapan Windows servisi.
/// Sadece MSIX/Appx paketlenmiş uygulamalarda çalışır.
/// Unpackaged/debug build'lerde NoOpUpdateService kullanılır.
/// </summary>
public sealed class MicrosoftStoreUpdateService : IAppUpdateService
{
    private readonly IPackageIdentityService? _packageIdentityService;
    private readonly IWindowHandleProvider? _windowHandleProvider;

    public event EventHandler<UpdateStateChangedEventArgs>? UpdateStateChanged;

    public MicrosoftStoreUpdateService(
        IPackageIdentityService? packageIdentityService = null,
        IWindowHandleProvider? windowHandleProvider = null)
    {
        _packageIdentityService = packageIdentityService;
        _windowHandleProvider = windowHandleProvider;
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

            var operation = context.GetAppAndOptionalStorePackageUpdatesAsync();
            var updates = await operation
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            if (updates == null || updates.Count == 0)
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            }

            // Microsoft Store API semantic version number vermez, sadece update sayısı verir.
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                LatestVersion = null, // Platform mesajı için null, ViewModel yönetecek
                Changelog = $"{updates.Count} package update(s) available",
                IsMandatory = false
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Error, ErrorMessage = "Check cancelled or timed out" };
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

            // Desktop uygulamalarda Store modal dialog'unu ana pencereye bağla
            if (_windowHandleProvider != null && _windowHandleProvider.WindowHandle != IntPtr.Zero)
            {
                var initializeWithWindow = (IInitializeWithWindow)(object)context;
                initializeWithWindow.Initialize(_windowHandleProvider.WindowHandle);
            }

            var updatesOp = context.GetAppAndOptionalStorePackageUpdatesAsync();
            var updates = await updatesOp
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            if (updates == null || updates.Count == 0) return false;

            var installOp = context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            var installOperation = await installOp.AsTask().WaitAsync(cancellationToken);

            if (installOperation == null) return false;

            var overallState = installOperation.OverallState;
            Debug.WriteLine($"[MicrosoftStoreUpdateService] Update result: {overallState}");

            return overallState == StorePackageUpdateState.Completed;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[MicrosoftStoreUpdateService] StartUpdate cancelled");
            return false;
        }
        catch (Exception ex)
        {
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

/// <summary>
/// WinRT IInitializeWithWindow COM arayüzü — StoreContext modal dialog'unu
/// desktop pencere handle'ına bağlamak için gerekli.
/// </summary>
[ComImport]
[Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithWindow
{
    void Initialize(IntPtr hwnd);
}
