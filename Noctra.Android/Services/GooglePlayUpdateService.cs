using Android.App;
using Android.Content;
using Google.Android.Play.AppUpdate;
using Google.Android.Play.AppUpdate.Install;
using Google.Android.Play.AppUpdate.Install.States;
using Noctra.Services.Interfaces;
using Noctra.Android.Services;

namespace Noctra.Android.Services;

/// <summary>
/// Google Play In-App Updates API kullanarak güncelleme kontrolü yapan Android servisi.
/// Flexible (arka plan) ve Immediate (tam ekran) olmak üzere iki güncelleme akışı sunar.
///
/// Google'ın önerdiği flexible update akışı:
/// 1. CheckAsync ile güncelleme varsa öğren
/// 2. StartUpdateAsync ile flexible indirmeyi başlat
/// 3. Listener ile indirme durumunu izle
/// 4. İndirme tamamlandığında kullanıcıya "yeniden başlat ve yükle" göster
/// 5. CompleteUpdateAsync çağrılınca yüklemeyi başlat
/// 6. App resume'ta bekleyen DOWNLOADED güncellemeyi kontrol et
///
/// Not: Bu servis sadece Google Play Store üzerinden yüklenmiş uygulamalarda çalışır.
/// Debug veya sideload build'lerde NoOpUpdateService kullanılır.
/// </summary>
public sealed class GooglePlayUpdateService : IAppUpdateService, IDisposable
{
    private readonly IAppUpdateManager _appUpdateManager;
    private readonly AndroidActivityProvider _activityProvider;
    private IInstallStateUpdatedListener? _installStateListener;
    private UpdateCheckResult? _lastCheckResult;

    private const int UpdateRequestCode = 17362;

    public GooglePlayUpdateService(AndroidActivityProvider activityProvider)
    {
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        var activity = _activityProvider.Current
            ?? throw new InvalidOperationException("Android activity is not available.");
        _appUpdateManager = AppUpdateManagerFactory.Create(activity);
    }

    private Activity GetActivity()
    {
        return _activityProvider.Current
            ?? throw new InvalidOperationException("Android activity is not available.");
    }

    /// <summary>
    /// Google Play'de güncelleme varsa bilgi döndürür.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetAppUpdateInfoAsync(cancellationToken);

            if (info == null)
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.Unsupported };
            }

            var availability = info.UpdateAvailability();

            if (availability == UpdateAvailability.UpdateAvailable)
            {
                // IsMandatory: Play Console'daki update priority veya update'in ne kadar
                // süredir mevcut olduğuna göre karar ver.
                var updatePriority = info.UpdatePriority();
                var stalenessDays = info.ClientVersionStalenessDays();
                var isMandatory = updatePriority >= 4 || (stalenessDays?.IntValue() ?? 0) >= 14;

                _lastCheckResult = new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpdateAvailable,
                    LatestVersion = info.AvailableVersionCode().ToString(),
                    IsMandatory = isMandatory
                };
                return _lastCheckResult;
            }

            // Başka bir developer-triggered update devam ediyor
            if (availability == UpdateAvailability.UpdateInProgress)
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            }

            return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] Check failed: {ex.Message}");
            return new UpdateCheckResult { Status = UpdateCheckStatus.Error, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Flexible update indirmesini başlatır.
    /// Listener ile durum izlenir, auto-complete yapılmaz.
    /// </summary>
    public async Task<bool> StartUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetAppUpdateInfoAsync(cancellationToken);
            if (info == null) return false;

            var activity = GetActivity();

            if (info.IsUpdateTypeAllowed(AppUpdateType.Flexible))
            {
                RegisterInstallStateListener();

                await _appUpdateManager.StartUpdateFlowForResult(
                    info,
                    AppUpdateType.Flexible,
                    activity,
                    UpdateRequestCode);

                return true;
            }

            if (info.IsUpdateTypeAllowed(AppUpdateType.Immediate))
            {
                await _appUpdateManager.StartUpdateFlowForResult(
                    info,
                    AppUpdateType.Immediate,
                    activity,
                    UpdateRequestCode);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] StartUpdate failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Flexible güncelleme tamamlandığında kullanıcı onayıyla yükler.
    /// Google'ın önerdiği akış: completeUpdate() çağrılınca uygulama yeniden başlatılır.
    /// </summary>
    public async Task<bool> CompleteUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetAppUpdateInfoAsync(cancellationToken);
            if (info == null) return false;

            if (info.InstallStatus() == InstallStatus.Downloaded)
            {
                await _appUpdateManager.CompleteUpdate();
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] CompleteUpdate failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Uygulama foreground'a döndüğünde bekleyen indirilmiş güncellemeyi kontrol eder.
    /// Flexible update tamamlanmış ama henüz complete edilmemiş olabilir.
    /// </summary>
    public async Task<UpdateCheckResult> CheckPendingUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetAppUpdateInfoAsync(cancellationToken);
            if (info == null)
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.Unsupported };
            }

            if (info.InstallStatus() == InstallStatus.Downloaded)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Downloaded,
                    LatestVersion = info.AvailableVersionCode().ToString()
                };
            }

            return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] CheckPending failed: {ex.Message}");
            return new UpdateCheckResult { Status = UpdateCheckStatus.Error, ErrorMessage = ex.Message };
        }
    }

    private async Task<AppUpdateInfo?> GetAppUpdateInfoAsync(CancellationToken cancellationToken)
    {
        var task = _appUpdateManager.AppUpdateInfo;
        var tcs = new TaskCompletionSource<AppUpdateInfo?>();

        task.AddOnSuccessListener(new OnSuccessListener<AppUpdateInfo>(info =>
        {
            tcs.TrySetResult(info);
        }));

        task.AddOnFailureListener(new OnFailureListener(ex =>
        {
            tcs.TrySetResult(null);
        }));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return tcs.Task.Result;
    }

    /// <summary>
    /// Flexible güncelleme durumunu dinlemek için listener kaydeder.
    /// Auto-complete yapılmaz — sadece log ve durum bildirimi.
    /// </summary>
    public void RegisterInstallStateListener()
    {
        UnregisterInstallStateListener();

        _installStateListener = new InstallStateUpdatedListener(state =>
        {
            var status = state.InstallStatus();
            System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] Install state: {status}");

            switch (status)
            {
                case InstallStatus.Downloaded:
                    // İndirme tamamlandı — CompleteUpdateAsync kullanıcı onayıyla çağrılmalı
                    System.Diagnostics.Debug.WriteLine("[GooglePlayUpdateService] Update downloaded, waiting for user confirmation");
                    break;

                case InstallStatus.Failed:
                    System.Diagnostics.Debug.WriteLine("[GooglePlayUpdateService] Update download failed");
                    break;

                case InstallStatus.Installing:
                    System.Diagnostics.Debug.WriteLine("[GooglePlayUpdateService] Update installing...");
                    break;

                case InstallStatus.Pending:
                    System.Diagnostics.Debug.WriteLine("[GooglePlayUpdateService] Update pending...");
                    break;
            }
        });

        _appUpdateManager.RegisterListener(_installStateListener);
    }

    public void UnregisterInstallStateListener()
    {
        if (_installStateListener != null)
        {
            try
            {
                _appUpdateManager.UnregisterListener(_installStateListener);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] UnregisterListener failed: {ex.Message}");
            }
            _installStateListener = null;
        }
    }

    public void Dispose()
    {
        UnregisterInstallStateListener();
        _appUpdateManager?.Dispose();
    }
}

internal class OnSuccessListener<T> : Java.Lang.Object, IOnSuccessListener where T : Java.Lang.Object
{
    private readonly Action<T> _onSuccess;

    public OnSuccessListener(Action<T> onSuccess)
    {
        _onSuccess = onSuccess;
    }

    public void OnSuccess(Java.Lang.Object result)
    {
        if (result is T typed)
            _onSuccess?.Invoke(typed);
    }
}

internal class OnFailureListener : Java.Lang.Object, IOnFailureListener
{
    private readonly Action<Java.Lang.Exception> _onFailure;

    public OnFailureListener(Action<Java.Lang.Exception> onFailure)
    {
        _onFailure = onFailure;
    }

    public void OnFailure(Java.Lang.Exception exception)
    {
        _onFailure?.Invoke(exception);
    }
}

internal class InstallStateUpdatedListener : Java.Lang.Object, IInstallStateUpdatedListener
{
    private readonly Action<AppUpdateState> _onStateUpdate;

    public InstallStateUpdatedListener(Action<AppUpdateState> onStateUpdate)
    {
        _onStateUpdate = onStateUpdate;
    }

    public void OnStateUpdate(Java.Lang.Object state)
    {
        if (state is AppUpdateState typed)
            _onStateUpdate?.Invoke(typed);
    }
}
