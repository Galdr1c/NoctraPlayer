using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Gms.Tasks;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;
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

    public event EventHandler<UpdateStateChangedEventArgs>? UpdateStateChanged;

    public GooglePlayUpdateService(AndroidActivityProvider activityProvider)
    {
        _activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
        var activity = _activityProvider.CurrentActivity
            ?? throw new InvalidOperationException("Android activity is not available.");
        _appUpdateManager = AppUpdateManagerFactory.Create(activity);
    }

    private Activity GetActivity()
    {
        return _activityProvider.CurrentActivity
            ?? throw new InvalidOperationException("Android activity is not available.");
    }

    /// <summary>
    /// Google Play'de güncelleme varsa bilgi döndürür.
    /// Unsupported: Play Store dışından yüklenmiş / sideload
    /// Error: Ağ veya API hatası (timeout dahil)
    /// UpdateAvailable: Yeni sürüm mevcut
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            var (info, isTimeout) = await GetAppUpdateInfoAsync(cancellationToken);

            if (info == null)
            {
                var status = isTimeout
                    ? UpdateCheckStatus.Error
                    : UpdateCheckStatus.Unsupported;
                return new UpdateCheckResult
                {
                    Status = status,
                    ErrorMessage = isTimeout ? "Play Store API timed out" : null
                };
            }

            var availability = info.UpdateAvailability();

            if (availability == UpdateAvailability.UpdateAvailable)
            {
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

            return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] Check failed: {ex.Message}");
            return new UpdateCheckResult { Status = UpdateCheckStatus.Error, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Güncelleme indirmesini başlatır.
    /// IsMandatory == true ise Immediate (tam ekran) kullanılır.
    /// IsMandatory == false ise Flexible (arka plan) kullanılır.
    /// </summary>
    public async Task<bool> StartUpdateAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            var (info, _) = await GetAppUpdateInfoAsync(cancellationToken);
            if (info == null) return false;

            var activity = GetActivity();

            // Zorunlu güncelleme ise Immediate kullan
            if (_lastCheckResult?.IsMandatory == true && info.IsUpdateTypeAllowed(AppUpdateType.Immediate))
            {
                RegisterInstallStateListener();

                var options = AppUpdateOptions
                    .NewBuilder(AppUpdateType.Immediate)
                    .Build();

                _appUpdateManager.StartUpdateFlow(info, activity, options);
                UpdateStateChanged?.Invoke(this, new UpdateStateChangedEventArgs(UpdateCheckStatus.Downloading, 0));
                return true;
            }

            // Flexible update
            if (info.IsUpdateTypeAllowed(AppUpdateType.Flexible))
            {
                RegisterInstallStateListener();

                var options = AppUpdateOptions
                    .NewBuilder(AppUpdateType.Flexible)
                    .Build();

                _appUpdateManager.StartUpdateFlow(info, activity, options);
                UpdateStateChanged?.Invoke(this, new UpdateStateChangedEventArgs(UpdateCheckStatus.Downloading, 0));
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
    public async Task<bool> CompleteUpdateAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            var (info, _) = await GetAppUpdateInfoAsync(cancellationToken);
            if (info == null) return false;

            if (info.InstallStatus() == InstallStatus.Downloaded)
            {
                _appUpdateManager.CompleteUpdate();
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
    public async Task<UpdateCheckResult> CheckPendingUpdateAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            var (info, isTimeout) = await GetAppUpdateInfoAsync(cancellationToken);
            if (info == null)
            {
                return new UpdateCheckResult
                {
                    Status = isTimeout ? UpdateCheckStatus.Error : UpdateCheckStatus.Unsupported,
                    ErrorMessage = isTimeout ? "Play Store API timed out" : null
                };
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

    /// <summary>
    /// AppUpdateInfo alır. Binding'deki GetAppUpdateInfo() Java Task döndürür,
    /// IOnSuccessListener ile C# Task'e çevirip await ediyoruz.
    /// Timeout ve hata ayrımı yapar.
    /// </summary>
    private async Task<(AppUpdateInfo?, bool isTimeout)> GetAppUpdateInfoAsync(System.Threading.CancellationToken cancellationToken)
    {
        using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            // GetAppUpdateInfo() Java Task döndürür.
            // TaskCompletionSource ile C# Task'e çeviriyoruz.
            var tcs = new TaskCompletionSource<AppUpdateInfo?>();

            _appUpdateManager.GetAppUpdateInfo()
                .AddOnSuccessListener(new OnSuccessListener<AppUpdateInfo>(result =>
                {
                    tcs.TrySetResult(result);
                }))
                .AddOnFailureListener(new OnFailureListener(ex =>
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"Play Store API error: {ex.Message}", ex));
                }));

            var info = await tcs.Task.WaitAsync(linkedCts.Token);
            return (info, false);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return (null, true);
        }
        catch (OperationCanceledException)
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Flexible güncelleme durumunu dinlemek için listener kaydeder.
    /// State değişikliklerini UpdateStateChanged event'i ile ViewModel'e iletir.
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
                    UpdateStateChanged?.Invoke(this, new UpdateStateChangedEventArgs(UpdateCheckStatus.Downloaded));
                    break;

                case InstallStatus.Downloading:
                    var bytesDownloaded = state.BytesDownloaded();
                    var totalBytes = state.TotalBytesToDownload();
                    UpdateStateChanged?.Invoke(this, new UpdateStateChangedEventArgs(bytesDownloaded, totalBytes));
                    break;

                case InstallStatus.Failed:
                    UpdateStateChanged?.Invoke(this, new UpdateStateChangedEventArgs(UpdateCheckStatus.Error, "Download failed"));
                    break;

                case InstallStatus.Installing:
                    UpdateStateChanged?.Invoke(this, new UpdateStateChangedEventArgs(UpdateCheckStatus.Downloading, 90));
                    break;

                case InstallStatus.Pending:
                    UpdateStateChanged?.Invoke(this, new UpdateStateChangedEventArgs(UpdateCheckStatus.Downloading, 0));
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

/// <summary>
/// InstallStateUpdatedListener: IInstallStateUpdatedListener implementasyonu.
/// Binding'deki InstallState tipini kullanır.
/// </summary>
internal class InstallStateUpdatedListener : Java.Lang.Object, IInstallStateUpdatedListener
{
    private readonly Action<InstallState> _onStateUpdate;

    public InstallStateUpdatedListener(Action<InstallState> onStateUpdate)
    {
        _onStateUpdate = onStateUpdate;
    }

    public void OnStateUpdate(InstallState? state)
    {
        if (state != null)
            _onStateUpdate?.Invoke(state);
    }
}

/// <summary>
/// Android.Gms.Tasks IOnSuccessListener wrapper — Java Task'in success callback'ini
/// C# Action ile bağlar.
/// </summary>
internal class OnSuccessListener<T> : Java.Lang.Object, IOnSuccessListener where T : Java.Lang.Object
{
    private readonly Action<T> _onSuccess;

    public OnSuccessListener(Action<T> onSuccess)
    {
        _onSuccess = onSuccess;
    }

    public void OnSuccess(Java.Lang.Object? result)
    {
        if (result is T typed)
            _onSuccess?.Invoke(typed);
    }
}

/// <summary>
/// Android.Gms.Tasks IOnFailureListener wrapper — Java Task'in failure callback'ini
/// C# Action ile bağlar.
/// </summary>
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
