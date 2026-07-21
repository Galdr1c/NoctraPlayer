using Android.App;
using Android.Content;
using Google.Android.Play.Core.AppUpdate;
using Google.Android.Play.Core.AppUpdate.Install;
using Google.Android.Play.Core.AppUpdate.Install.States;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Google Play In-App Updates API kullanarak güncelleme kontrolü yapan Android servisi.
/// Flexible (arka plan) ve Immediate (tam ekran) olmak üzere iki güncelleme akışı sunar.
/// 
/// Kullanım: Uygulama başlatıldığında veya ayarlar ekranında "Güncellemeleri Denetle" butonuna basıldığında
/// CheckAsync() çağrılır. Güncelleme mevcutsa StartUpdateAsync() ile başlatılır.
/// 
/// Not: Bu servis sadece Google Play Store üzerinden yüklenmiş uygulamalarda çalışır.
/// Debug veya sideload build'lerde NoOpUpdateService kullanılır.
/// </summary>
public sealed class GooglePlayUpdateService : IAppUpdateService, IDisposable
{
    private readonly IAppUpdateManager _appUpdateManager;
    private readonly Activity _activity;
    private readonly int _requestCode;

    private const int UpdateRequestCode = 17362;

    public GooglePlayUpdateService(Activity activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _appUpdateManager = AppUpdateManagerFactory.Create(_activity);
    }

    /// <summary>
    /// Google Play'de güncelleme varsa bilgi döndürür.
    /// Flexible güncelleme mevcutsa ve izin veriliyorsa döner.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var task = _appUpdateManager.AppUpdateInfo;
            
            // Task completion source ile await ediyoruz
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
                return new UpdateCheckResult { IsUpdateAvailable = false };
            }

            var info = tcs.Task.Result;
            if (info == null)
            {
                return new UpdateCheckResult { IsUpdateAvailable = false };
            }

            var updateAvailable = info.UpdateAvailability() == UpdateAvailability.UpdateAvailable
                               || info.UpdateAvailability() == UpdateAvailability.DeveloperTriggeredUpdateInProgress;

            if (!updateAvailable)
            {
                return new UpdateCheckResult { IsUpdateAvailable = false };
            }

            // Flexible güncelleme tercih ediliyor (arka plan indirme)
            var flexibleAllowed = info.IsUpdateTypeAllowed(AppUpdateType.Flexible);

            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersion = info.AvailableVersionCode().ToString(),
                IsMandatory = info.UpdateAvailability() == UpdateAvailability.DeveloperTriggeredUpdateInProgress,
                // Flexible allowed ise arka plana, değilse hiçbir şey yapma
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] Check failed: {ex.Message}");
            return new UpdateCheckResult { IsUpdateAvailable = false };
        }
    }

    /// <summary>
    /// Google Play In-App Updates akışını başlatır.
    /// Flexible mod: Arka plana indirir, kullanıcı uygulamayı kullanmaya devam eder.
    /// İndirme tamamlandığında CompleteUpdateAsync() çağrılmalı.
    /// </summary>
    public async Task<bool> StartUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
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
                return false;
            }

            var info = tcs.Task.Result;
            if (info == null)
            {
                return false;
            }

            // Flexible güncelleme tercih ediliyor (arka plan indirme)
            if (info.IsUpdateTypeAllowed(AppUpdateType.Flexible))
            {
                await _appUpdateManager.StartUpdateFlowForResult(
                    info,
                    AppUpdateType.Flexible,
                    _activity,
                    UpdateRequestCode);

                return true;
            }

            // Immediate güncelleme (zorunlu durumlarda)
            if (info.IsUpdateTypeAllowed(AppUpdateType.Immediate))
            {
                await _appUpdateManager.StartUpdateFlowForResult(
                    info,
                    AppUpdateType.Immediate,
                    _activity,
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
    /// Flexible güncelleme indirme tamamlandığında çağrılmalı.
    /// Bu metot günclemeyi yükler ve uygulamayı yeniden başlatır.
    /// </summary>
    public async Task CompleteUpdateAsync()
    {
        try
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

            await tcs.Task;

            var info = tcs.Task.Result;
            if (info == null) return;

            if (info.InstallStatus() == InstallStatus.Downloaded)
            {
                // İndirme tamamlandı, yüklemeyi başlat
                await _appUpdateManager.CompleteUpdate();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GooglePlayUpdateService] CompleteUpdate failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Flexible güncelleme durumunu dinlemek için listener kaydeder.
    /// İndirme tamamlandığında CompleteUpdateAsync() otomatik olarak çağrılır.
    /// </summary>
    public void RegisterInstallStateListener()
    {
        _appUpdateManager.RegisterListener(new InstallStateUpdatedListener(state =>
        {
            if (state.InstallStatus() == InstallStatus.Downloaded)
            {
                // İndirme tamamlandı - kullanıcıya bildir ve yükle
                System.Diagnostics.Debug.WriteLine("[GooglePlayUpdateService] Update downloaded, completing...");
                _ = CompleteUpdateAsync();
            }
            else if (state.InstallStatus() == InstallStatus.Failed)
            {
                System.Diagnostics.Debug.WriteLine("[GooglePlayUpdateService] Update download failed");
            }
        }));
    }

    public void Dispose()
    {
        _appUpdateManager?.Dispose();
    }
}

/// <summary>
/// Play Core API için basit bir OnSuccessListener implementasyonu
/// </summary>
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

/// <summary>
/// Play Core API için basit bir OnFailureListener implementasyonu
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

/// <summary>
/// Play Core API için InstallStateUpdatedListener implementasyonu
/// </summary>
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
