using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Extensions;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Google Play In-App Updates entegrasyonu.
///
/// - Normal güncellemelerde flexible akış kullanır.
/// - Yüksek öncelikli veya uzun süredir bekleyen güncellemelerde immediate akış kullanır.
/// - Activity yeniden oluşturulduğunda devam eden immediate akışı geri yükler.
/// - Flexible indirme durumunu Play durumunu kontrollü aralıklarla sorgulayarak UI'a iletir.
///
/// Not: Xamarin Google Play binding'lerinde generic InstallStateUpdatedListener doğrudan
/// C# ile implemente edildiğinde Java generic-erasure derleme sorunları oluşabildiğinden,
/// burada güvenilir ve binding-bağımsız polling kullanılır.
/// </summary>
public sealed class GooglePlayUpdateService : IAppUpdateService, IDisposable
{
    private const int UpdateRequestCode = 17362;
    private static readonly TimeSpan StoreRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CompleteRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MonitorInitialStateTimeout = TimeSpan.FromMinutes(2);

    private readonly Context _applicationContext;
    private readonly IAppUpdateManager _appUpdateManager;
    private readonly AndroidActivityProvider _activityProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _monitorSync = new();

    private CancellationTokenSource? _flexibleMonitorCts;
    private Task? _flexibleMonitorTask;
    private UpdateCheckStatus? _lastPublishedMonitorStatus;
    private bool _flowAwaitingActivityResult;
    private bool _activeFlowIsImmediate;
    private WeakReference<global::Android.App.Activity>? _flowActivity;
    private bool _disposed;

    public event EventHandler<UpdateStateChangedEventArgs>? UpdateStateChanged;

    public GooglePlayUpdateService(
        Context context,
        AndroidActivityProvider activityProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        _applicationContext = context.ApplicationContext ?? context;
        _appUpdateManager = AppUpdateManagerFactory.Create(_applicationContext);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalledFromGooglePlay())
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Unsupported };
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var info = await GetAppUpdateInfoAsync(cancellationToken).ConfigureAwait(false);
            return CreateCheckResult(info);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ErrorResult("Google Play update check timed out.");
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Canceled };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GooglePlayUpdateService] Check failed: {ex}");
            return ErrorResult(ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<bool> StartUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalledFromGooglePlay())
        {
            RaiseState(UpdateCheckStatus.Unsupported);
            return false;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_flowAwaitingActivityResult)
            {
                return false;
            }

            var activity = GetActivity();
            var info = await GetAppUpdateInfoAsync(cancellationToken).ConfigureAwait(false);

            // Daha önce başlamış flexible akış varsa ikinci bir Store penceresi açma.
            if (TryPublishFlexibleState(info))
            {
                return true;
            }

            var availability = info.UpdateAvailability();
            if (availability != UpdateAvailability.UpdateAvailable &&
                availability != UpdateAvailability.DeveloperTriggeredUpdateInProgress)
            {
                RaiseState(UpdateCheckStatus.UpToDate);
                return false;
            }

            var useImmediate = availability == UpdateAvailability.DeveloperTriggeredUpdateInProgress ||
                               ShouldUseImmediate(info);
            return StartFlowForResult(info, activity, useImmediate);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RaiseState(UpdateCheckStatus.Error, errorMessage: "Google Play update request timed out.");
            return false;
        }
        catch (OperationCanceledException)
        {
            RaiseState(UpdateCheckStatus.Canceled);
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GooglePlayUpdateService] StartUpdate failed: {ex}");
            RaiseState(UpdateCheckStatus.Error, errorMessage: ex.Message);
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<bool> CompleteUpdateAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var info = await GetAppUpdateInfoAsync(cancellationToken).ConfigureAwait(false);
            if (info.InstallStatus() != InstallStatus.Downloaded)
            {
                return false;
            }

            await StopFlexibleMonitorAsync().ConfigureAwait(false);
            RaiseState(UpdateCheckStatus.Downloading, progressPercent: 100);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CompleteRequestTimeout);

            await _appUpdateManager
                .CompleteUpdate()
                .AsAsync()
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);

            // Normalde Google Play uygulamayı yeniden başlatır. Task geri döner ve süreç
            // yaşamaya devam ederse UI'ın kilitli kalmaması için terminal state yayınla.
            RaiseState(UpdateCheckStatus.UpToDate);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RaiseState(UpdateCheckStatus.Error, errorMessage: "Google Play update completion timed out.");
            return false;
        }
        catch (OperationCanceledException)
        {
            RaiseState(UpdateCheckStatus.Canceled);
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GooglePlayUpdateService] CompleteUpdate failed: {ex}");
            RaiseState(UpdateCheckStatus.Error, errorMessage: ex.Message);
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<UpdateCheckResult> CheckPendingUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalledFromGooglePlay())
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Unsupported };
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var info = await GetAppUpdateInfoAsync(cancellationToken).ConfigureAwait(false);
            var result = CreatePendingResult(info);

            if (info.InstallStatus() is InstallStatus.Pending or
                InstallStatus.Downloading or
                InstallStatus.Installing)
            {
                StartFlexibleMonitor();
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ErrorResult("Google Play pending update check timed out.");
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Canceled };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GooglePlayUpdateService] CheckPending failed: {ex}");
            return ErrorResult(ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Activity foreground'a döndüğünde çağrılır. Yarım kalmış immediate akışı yeniden açar;
    /// devam eden veya indirilmiş flexible güncellemeyi UI'a bildirir.
    /// </summary>
    public async Task ResumeUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsInstalledFromGooglePlay())
        {
            return;
        }

        var currentActivity = _activityProvider.CurrentActivity;
        if (currentActivity is null)
        {
            return;
        }

        // Aynı Activity Google Play sonucunu bekliyorsa akışı ikinci kez açma.
        if (_flowAwaitingActivityResult &&
            _flowActivity is not null &&
            _flowActivity.TryGetTarget(out var flowActivity) &&
            ReferenceEquals(flowActivity, currentActivity))
        {
            return;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_flowAwaitingActivityResult &&
                _flowActivity is not null &&
                _flowActivity.TryGetTarget(out var pendingActivity) &&
                ReferenceEquals(pendingActivity, currentActivity))
            {
                return;
            }

            // Activity recreation olduysa eski result callback artık güvenilir değildir.
            if (_flowAwaitingActivityResult)
            {
                ResetActiveFlow();
            }

            var info = await GetAppUpdateInfoAsync(cancellationToken).ConfigureAwait(false);

            // Flexible akışta InstallStatus daha belirleyicidir. Önce bunu ele alarak
            // DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS durumunu yanlışlıkla immediate'a çevirmeyiz.
            if (TryPublishFlexibleState(info))
            {
                return;
            }

            if (info.UpdateAvailability() == UpdateAvailability.DeveloperTriggeredUpdateInProgress &&
                info.IsUpdateTypeAllowed(AppUpdateType.Immediate))
            {
                StartFlowForResult(info, currentActivity, useImmediate: true);
            }
        }
        catch (OperationCanceledException)
        {
            // Activity lifecycle cancellation is expected; do not surface it as an error.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GooglePlayUpdateService] Resume update failed: {ex}");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// MainActivity.OnActivityResult tarafından çağrılır.
    /// </summary>
    public bool TryHandleActivityResult(int requestCode, Result resultCode)
    {
        if (requestCode != UpdateRequestCode)
        {
            return false;
        }

        _flowAwaitingActivityResult = false;
        _flowActivity = null;

        if (resultCode == Result.Ok)
        {
            if (_activeFlowIsImmediate)
            {
                RaiseState(UpdateCheckStatus.Downloading, progressPercent: 100);
            }
            else
            {
                StartFlexibleMonitor();
                RaiseState(UpdateCheckStatus.Downloading);
            }
        }
        else if (resultCode == Result.Canceled)
        {
            StopFlexibleMonitor();
            RaiseState(UpdateCheckStatus.Canceled);
        }
        else
        {
            StopFlexibleMonitor();
            RaiseState(
                UpdateCheckStatus.Error,
                errorMessage: $"Google Play update flow failed with result {resultCode}.");
        }

        _activeFlowIsImmediate = false;
        return true;
    }

    private bool StartFlowForResult(AppUpdateInfo info, global::Android.App.Activity activity, bool useImmediate)
    {
        var updateType = useImmediate ? AppUpdateType.Immediate : AppUpdateType.Flexible;
        if (!info.IsUpdateTypeAllowed(updateType))
        {
            // Prefer the other supported flow rather than presenting a dead button.
            updateType = updateType == AppUpdateType.Immediate
                ? AppUpdateType.Flexible
                : AppUpdateType.Immediate;

            if (!info.IsUpdateTypeAllowed(updateType))
            {
                RaiseState(
                    UpdateCheckStatus.Error,
                    errorMessage: "No supported Google Play update flow is available.");
                return false;
            }
        }

        var isImmediate = updateType == AppUpdateType.Immediate;
        var options = AppUpdateOptions.NewBuilder(updateType).Build();
        var started = _appUpdateManager.StartUpdateFlowForResult(
            info,
            activity,
            options,
            UpdateRequestCode);

        if (!started)
        {
            RaiseState(
                UpdateCheckStatus.Error,
                errorMessage: "Google Play update flow could not be started.");
            return false;
        }

        _activeFlowIsImmediate = isImmediate;
        _flowAwaitingActivityResult = true;
        _flowActivity = new WeakReference<global::Android.App.Activity>(activity);
        return true;
    }

    private void ResetActiveFlow()
    {
        _flowAwaitingActivityResult = false;
        _activeFlowIsImmediate = false;
        _flowActivity = null;
    }

    private UpdateCheckResult CreateCheckResult(AppUpdateInfo info)
    {
        var flexibleState = CreateFlexibleStateResult(info);
        if (flexibleState is not null)
        {
            if (flexibleState.Status == UpdateCheckStatus.Downloading)
            {
                StartFlexibleMonitor();
            }

            return flexibleState;
        }

        var availability = info.UpdateAvailability();
        if (availability == UpdateAvailability.DeveloperTriggeredUpdateInProgress)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                LatestVersion = null,
                IsMandatory = true
            };
        }

        if (availability != UpdateAvailability.UpdateAvailable)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
        }

        var flexibleAllowed = info.IsUpdateTypeAllowed(AppUpdateType.Flexible);
        var immediateAllowed = info.IsUpdateTypeAllowed(AppUpdateType.Immediate);
        if (!flexibleAllowed && !immediateAllowed)
        {
            return ErrorResult("Google Play reported an update, but no supported update flow is available.");
        }

        return new UpdateCheckResult
        {
            Status = UpdateCheckStatus.UpdateAvailable,
            // Play In-App Updates exposes versionCode, not the user-facing versionName.
            LatestVersion = null,
            IsMandatory = ShouldUseImmediate(info)
        };
    }

    private static UpdateCheckResult CreatePendingResult(AppUpdateInfo info)
    {
        var flexibleState = CreateFlexibleStateResult(info);
        if (flexibleState is not null)
        {
            return flexibleState;
        }

        return info.UpdateAvailability() == UpdateAvailability.DeveloperTriggeredUpdateInProgress
            ? new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Downloading,
                IsMandatory = true
            }
            : new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
    }

    private bool TryPublishFlexibleState(AppUpdateInfo info)
    {
        switch (info.InstallStatus())
        {
            case InstallStatus.Downloaded:
                StopFlexibleMonitor();
                RaiseState(UpdateCheckStatus.Downloaded);
                return true;

            case InstallStatus.Pending:
            case InstallStatus.Downloading:
            case InstallStatus.Installing:
                StartFlexibleMonitor();
                RaiseState(UpdateCheckStatus.Downloading);
                return true;

            case InstallStatus.Installed:
                StopFlexibleMonitor();
                RaiseState(UpdateCheckStatus.UpToDate);
                return true;

            case InstallStatus.Failed:
                StopFlexibleMonitor();
                RaiseState(UpdateCheckStatus.Error, errorMessage: "Google Play update failed.");
                return true;

            case InstallStatus.Canceled:
                StopFlexibleMonitor();
                RaiseState(UpdateCheckStatus.Canceled);
                return true;

            default:
                return false;
        }
    }

    private static UpdateCheckResult? CreateFlexibleStateResult(AppUpdateInfo info)
    {
        return info.InstallStatus() switch
        {
            InstallStatus.Downloaded => new UpdateCheckResult { Status = UpdateCheckStatus.Downloaded },
            InstallStatus.Pending or InstallStatus.Downloading or InstallStatus.Installing =>
                new UpdateCheckResult { Status = UpdateCheckStatus.Downloading },
            InstallStatus.Installed => new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate },
            InstallStatus.Failed => ErrorResult("Google Play update failed."),
            InstallStatus.Canceled => new UpdateCheckResult { Status = UpdateCheckStatus.Canceled },
            _ => null
        };
    }

    private static bool ShouldUseImmediate(AppUpdateInfo info)
    {
        var stalenessDays = info.ClientVersionStalenessDays()?.IntValue() ?? 0;
        return info.UpdateAvailability() == UpdateAvailability.DeveloperTriggeredUpdateInProgress ||
               info.UpdatePriority() >= 4 ||
               stalenessDays >= 14;
    }

    private void StartFlexibleMonitor()
    {
        lock (_monitorSync)
        {
            if (_disposed ||
                _flexibleMonitorTask is { IsCompleted: false })
            {
                return;
            }

            _flexibleMonitorCts?.Dispose();
            _flexibleMonitorCts = new CancellationTokenSource();
            _lastPublishedMonitorStatus = null;
            var cts = _flexibleMonitorCts;
            _flexibleMonitorTask = MonitorFlexibleUpdateAsync(cts);
        }
    }

    private async Task MonitorFlexibleUpdateAsync(CancellationTokenSource ownerCts)
    {
        var token = ownerCts.Token;
        var startedAt = DateTime.UtcNow;
        var hasObservedActiveState = false;
        var consecutiveErrors = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var info = await GetAppUpdateInfoAsync(token).ConfigureAwait(false);
                    consecutiveErrors = 0;

                    switch (info.InstallStatus())
                    {
                        case InstallStatus.Downloaded:
                            PublishMonitorState(UpdateCheckStatus.Downloaded);
                            return;

                        case InstallStatus.Pending:
                        case InstallStatus.Downloading:
                        case InstallStatus.Installing:
                            hasObservedActiveState = true;
                            PublishMonitorState(UpdateCheckStatus.Downloading);
                            break;

                        case InstallStatus.Installed:
                            PublishMonitorState(UpdateCheckStatus.UpToDate);
                            return;

                        case InstallStatus.Canceled:
                            PublishMonitorState(UpdateCheckStatus.Canceled);
                            return;

                        case InstallStatus.Failed:
                            RaiseState(
                                UpdateCheckStatus.Error,
                                errorMessage: "Google Play update download failed.");
                            return;

                        default:
                            // Play onayından hemen sonra indirme state'i birkaç sorgu gecikebilir.
                            // Sonsuza kadar beklemek yerine yalnızca başlangıçta sınırlı tolerans ver.
                            if (!hasObservedActiveState &&
                                DateTime.UtcNow - startedAt > MonitorInitialStateTimeout)
                            {
                                RaiseState(
                                    UpdateCheckStatus.Error,
                                    errorMessage: "Google Play update did not enter a downloadable state.");
                                return;
                            }

                            break;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    Debug.WriteLine(
                        $"[GooglePlayUpdateService] Flexible monitor attempt {consecutiveErrors} failed: {ex}");

                    // Geçici Play/ağ hatalarında aktif indirmeyi hemen başarısız sayma.
                    if (consecutiveErrors >= 5)
                    {
                        RaiseState(UpdateCheckStatus.Error, errorMessage: ex.Message);
                        return;
                    }
                }

                await Task.Delay(MonitorInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during dispose, cancellation or terminal state.
        }
        finally
        {
            lock (_monitorSync)
            {
                if (ReferenceEquals(_flexibleMonitorCts, ownerCts))
                {
                    _flexibleMonitorCts = null;
                    _flexibleMonitorTask = null;
                    _lastPublishedMonitorStatus = null;
                    ownerCts.Dispose();
                }
            }
        }
    }

    private void PublishMonitorState(UpdateCheckStatus status)
    {
        lock (_monitorSync)
        {
            if (_lastPublishedMonitorStatus == status)
            {
                return;
            }

            _lastPublishedMonitorStatus = status;
        }

        RaiseState(status);
    }

    private async Task StopFlexibleMonitorAsync()
    {
        Task? task;
        lock (_monitorSync)
        {
            task = _flexibleMonitorTask;
        }

        StopFlexibleMonitor();

        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
    }

    private void StopFlexibleMonitor()
    {
        CancellationTokenSource? cts;
        lock (_monitorSync)
        {
            cts = _flexibleMonitorCts;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Monitor completed concurrently.
        }
    }

    private async Task<AppUpdateInfo> GetAppUpdateInfoAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(StoreRequestTimeout);

        return await _appUpdateManager
            .GetAppUpdateInfo()
            .AsAsync<AppUpdateInfo>()
            .WaitAsync(timeoutCts.Token)
            .ConfigureAwait(false);
    }

    private bool IsInstalledFromGooglePlay()
    {
        try
        {
            string? installer;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                installer = _applicationContext.PackageManager?
                    .GetInstallSourceInfo(_applicationContext.PackageName!)
                    .InstallingPackageName;
            }
            else
            {
#pragma warning disable CS0618
                installer = _applicationContext.PackageManager?
                    .GetInstallerPackageName(_applicationContext.PackageName!);
#pragma warning restore CS0618
            }

            return string.Equals(installer, "com.android.vending", StringComparison.Ordinal);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GooglePlayUpdateService] Installer check failed: {ex.Message}");
            return false;
        }
    }

    private global::Android.App.Activity GetActivity()
    {
        return _activityProvider.CurrentActivity
            ?? throw new InvalidOperationException("Android activity is not available.");
    }

    private void RaiseState(
        UpdateCheckStatus status,
        double? progressPercent = null,
        string? errorMessage = null)
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
        ResetActiveFlow();

        Task? monitorTask;
        lock (_monitorSync)
        {
            monitorTask = _flexibleMonitorTask;
        }

        StopFlexibleMonitor();
        try
        {
            monitorTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Monitor cancellation/teardown exceptions are non-fatal during disposal.
        }

        _operationGate.Dispose();
        _appUpdateManager.Dispose();
    }
}
