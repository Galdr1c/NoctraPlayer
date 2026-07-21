using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Services.Interfaces;
using Windows.Foundation;
using Windows.Services.Store;
using WinRT;

namespace Noctra.Avalonia.Services;

/// <summary>
/// Microsoft Store paket güncellemelerini denetler ve Store tarafından yönetilen
/// indirme/kurulum akışını başlatır. Yalnızca paket kimliği olan MSIX/Store build'lerinde çalışır.
/// </summary>
public sealed class MicrosoftStoreUpdateService : IAppUpdateService, IDisposable
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    private readonly IPackageIdentityService _packageIdentityService;
    private readonly IWindowHandleProvider _windowHandleProvider;
    private readonly IDispatcherService _dispatcherService;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public event EventHandler<UpdateStateChangedEventArgs>? UpdateStateChanged;

    public MicrosoftStoreUpdateService(
        IPackageIdentityService packageIdentityService,
        IWindowHandleProvider windowHandleProvider,
        IDispatcherService dispatcherService)
    {
        _packageIdentityService = packageIdentityService ?? throw new ArgumentNullException(nameof(packageIdentityService));
        _windowHandleProvider = windowHandleProvider ?? throw new ArgumentNullException(nameof(windowHandleProvider));
        _dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_packageIdentityService.IsPackaged)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Unsupported };
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var context = await _dispatcherService
                .InvokeAsync(() => CreateStoreContext(requireWindow: false))
                .ConfigureAwait(false);
            var updates = await GetUpdatesAsync(context, cancellationToken).ConfigureAwait(false);

            return updates.Count == 0
                ? new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate }
                : new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpdateAvailable,
                    // Store API burada kullanıcıya gösterilecek semantic sürüm döndürmez.
                    LatestVersion = null,
                    IsMandatory = updates.Any(update => update.Mandatory)
                };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ErrorResult("Microsoft Store update check timed out.");
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Canceled };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MicrosoftStoreUpdateService] Check failed: {ex}");
            return ErrorResult(ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<bool> StartUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!_packageIdentityService.IsPackaged)
        {
            RaiseState(UpdateCheckStatus.Unsupported);
            return false;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IAsyncOperationWithProgress<StorePackageUpdateResult, StorePackageUpdateStatus>? operation = null;
        try
        {
            ThrowIfDisposed();
            var context = await _dispatcherService
                .InvokeAsync(() => CreateStoreContext(requireWindow: true))
                .ConfigureAwait(false);
            var updates = await GetUpdatesAsync(context, cancellationToken).ConfigureAwait(false);
            if (updates.Count == 0)
            {
                RaiseState(UpdateCheckStatus.UpToDate);
                return false;
            }

            RaiseState(UpdateCheckStatus.Downloading, progressPercent: 0);

            // Bu çağrı Store izin penceresi gösterebildiği için mutlaka UI thread'de oluşturulmalı.
            operation = await _dispatcherService
                .InvokeAsync(() => context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates))
                .ConfigureAwait(false);

            operation.Progress = (_, progress) =>
            {
                // Microsoft Store bu değeri indirme + kurulum için 0.0–1.0 aralığında verir.
                var percent = Math.Clamp(progress.PackageDownloadProgress * 100d, 0d, 100d);
                RaiseState(UpdateCheckStatus.Downloading, progressPercent: percent);
            };

            var result = await operation
                .AsTask()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                RaiseState(UpdateCheckStatus.Error, "Microsoft Store returned no installation result.");
                return false;
            }

            Debug.WriteLine($"[MicrosoftStoreUpdateService] Update result: {result.OverallState}");

            return result.OverallState switch
            {
                StorePackageUpdateState.Completed => CompleteSuccessfully(),
                StorePackageUpdateState.Canceled => CancelUpdate(),
                _ => FailUpdate(result.OverallState)
            };
        }
        catch (OperationCanceledException)
        {
            try
            {
                operation?.Cancel();
            }
            catch
            {
                // WinRT operation may already be terminal.
            }

            Debug.WriteLine("[MicrosoftStoreUpdateService] StartUpdate canceled");
            RaiseState(UpdateCheckStatus.Canceled);
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MicrosoftStoreUpdateService] StartUpdate failed: {ex}");
            RaiseState(UpdateCheckStatus.Error, ex.Message);
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private bool CompleteSuccessfully()
    {
        RaiseState(UpdateCheckStatus.UpToDate);
        return true;
    }

    private bool CancelUpdate()
    {
        RaiseState(UpdateCheckStatus.Canceled);
        return false;
    }

    private bool FailUpdate(StorePackageUpdateState state)
    {
        RaiseState(UpdateCheckStatus.Error, $"Microsoft Store update failed: {state}");
        return false;
    }

    public Task<bool> CompleteUpdateAsync(CancellationToken cancellationToken = default)
    {
        // Microsoft Store kurulumu kendi tamamlar; Android'deki gibi ayrı complete adımı yoktur.
        return Task.FromResult(false);
    }

    public Task<UpdateCheckResult> CheckPendingUpdateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new UpdateCheckResult
        {
            Status = _packageIdentityService.IsPackaged
                ? UpdateCheckStatus.UpToDate
                : UpdateCheckStatus.Unsupported
        });
    }

    private StoreContext CreateStoreContext(bool requireWindow)
    {
        var context = StoreContext.GetDefault()
            ?? throw new InvalidOperationException("Microsoft Store context is unavailable.");

        if (!requireWindow)
        {
            return context;
        }

        var hwnd = _windowHandleProvider.WindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("An active desktop window handle is unavailable.");
        }

        // Desteklenen CsWinRT masaüstü bağlama yöntemi.
        WinRT.Interop.InitializeWithWindow.Initialize(context, hwnd);
        return context;
    }

    private static async Task<global::System.Collections.Generic.IReadOnlyList<StorePackageUpdate>> GetUpdatesAsync(
        StoreContext context,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CheckTimeout);

        var operation = context.GetAppAndOptionalStorePackageUpdatesAsync();
        try
        {
            return await operation
                .AsTask()
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                operation.Cancel();
            }
            catch
            {
                // WinRT operasyonu terminal durumda olabilir.
            }

            throw;
        }
    }

    private void RaiseState(
        UpdateCheckStatus status,
        string? errorMessage = null,
        double? progressPercent = null)
    {
        UpdateStateChangedEventArgs args;
        if (progressPercent.HasValue)
        {
            args = new UpdateStateChangedEventArgs(status, progressPercent.Value);
        }
        else if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            args = new UpdateStateChangedEventArgs(status, errorMessage);
        }
        else
        {
            args = new UpdateStateChangedEventArgs(status);
        }

        UpdateStateChanged?.Invoke(this, args);
    }

    private static UpdateCheckResult ErrorResult(string message) =>
        new()
        {
            Status = UpdateCheckStatus.Error,
            ErrorMessage = message
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationGate.Dispose();
    }
}
