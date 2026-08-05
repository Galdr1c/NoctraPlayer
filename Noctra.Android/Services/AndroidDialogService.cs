using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using System.Threading;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidDialogService : IDialogService
{
    private readonly AndroidActivityProvider _activityProvider;
    private readonly ILocalizationService _localizationService;
    private readonly AndroidNotificationPermissionService _notificationPermissionService;
    private static int _nextNotificationId = 7000;

    public AndroidDialogService(
        AndroidActivityProvider activityProvider,
        ILocalizationService localizationService,
        AndroidNotificationPermissionService notificationPermissionService)
    {
        _activityProvider = activityProvider;
        _localizationService = localizationService;
        _notificationPermissionService = notificationPermissionService;
    }

    public Task ShowMessageAsync(string title, string message) =>
        ShowAlertAsync(title, message);

    public Task ShowLegalDocumentAsync(string title, string message) =>
        ShowAlertAsync(title, message);

    public Task ShowErrorAsync(string title, string message, Exception? ex = null) =>
        ShowAlertAsync(title, ex is null ? message : $"{message}\n\n{ex.Message}");

    public Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var activity = GetActivity();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            using var builder = new AlertDialog.Builder(activity);
            builder.SetTitle(title);
            builder.SetMessage(message);
            builder.SetPositiveButton(
                _localizationService.GetString("Dialog.Ok"),
                (_, _) => completion.TrySetResult(true));
            builder.SetNegativeButton(
                _localizationService.GetString("Dialog.Cancel"),
                (_, _) => completion.TrySetResult(false));
            builder.SetOnCancelListener(
                new CancelListener(() => completion.TrySetResult(false)));
            var dialog = builder.Create()
                ?? throw new InvalidOperationException("Android confirmation dialog could not be created.");
            dialog.Show();
        });
        return completion.Task;
    }

    public Task ShowUpsellAsync() =>
        ShowAlertAsync(
            _localizationService.GetString("Upsell.Title"),
            _localizationService.GetString("Android.Dialog.PremiumRequired"));

    public Task<bool> ShowAddProfileAsync() => Task.FromResult(false);

    public Task<bool> ShowEditProfileAsync(Profile profile, ProfileAccessGrant? grant = null) => Task.FromResult(false);

    public Task ShowGlobalSettingsAsync() => Task.CompletedTask;

    public Task<string?> ShowAvatarPickerAsync(string? currentAvatar) =>
        Task.FromResult<string?>(null);

    public async Task ShowNotificationAsync(string title, string message)
    {
        var activity = GetActivity();
        if (!await _notificationPermissionService.EnsureNotificationPermissionAsync())
        {
            activity.RunOnUiThread(() =>
                Toast.MakeText(activity.ApplicationContext, $"{title}: {message}", ToastLength.Long)?.Show());
            return;
        }

        const string channelId = "noctra_downloads";
        var manager = activity.GetSystemService(Context.NotificationService) as NotificationManager
            ?? throw new InvalidOperationException("Android NotificationManager is unavailable.");

        using (var channel = new NotificationChannel(
                   channelId,
                   "Downloads",
                   NotificationImportance.Default)
        {
            Description = "Noctra download completion notifications"
        })
        {
            manager.CreateNotificationChannel(channel);
        }

        var packageName = activity.PackageName ?? "studio.kynora.noctra";
        var launchIntent = activity.PackageManager?.GetLaunchIntentForPackage(packageName);
        PendingIntent? contentIntent = null;
        if (launchIntent is not null)
        {
            launchIntent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            contentIntent = PendingIntent.GetActivity(
                activity,
                0,
                launchIntent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }

        using var builder = new Notification.Builder(activity, channelId);
        builder
            .SetSmallIcon(Resource.Drawable.ic_notification_noctra)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetStyle(new Notification.BigTextStyle().BigText(message))
            .SetAutoCancel(true)
            .SetOnlyAlertOnce(true);

        if (contentIntent is not null)
        {
            builder.SetContentIntent(contentIntent);
        }

        manager.Notify(Interlocked.Increment(ref _nextNotificationId), builder.Build());
        contentIntent?.Dispose();
    }

    private Task ShowAlertAsync(string title, string message)
    {
        var activity = GetActivity();
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            using var builder = new AlertDialog.Builder(activity);
            builder.SetTitle(title);
            builder.SetMessage(message);
            builder.SetPositiveButton(
                _localizationService.GetString("Dialog.Ok"),
                (_, _) => completion.TrySetResult(null));
            builder.SetOnCancelListener(
                new CancelListener(() => completion.TrySetResult(null)));
            var dialog = builder.Create()
                ?? throw new InvalidOperationException("Android alert dialog could not be created.");
            dialog.Show();
        });
        return completion.Task;
    }

    private Activity GetActivity() =>
        _activityProvider.CurrentActivity
        ?? throw new InvalidOperationException("No active Android activity is available.");

    private sealed class CancelListener : Java.Lang.Object, global::Android.Content.IDialogInterfaceOnCancelListener
    {
        private readonly Action _onCancel;

        public CancelListener(Action onCancel)
        {
            _onCancel = onCancel;
        }

        public void OnCancel(global::Android.Content.IDialogInterface? dialog)
        {
            _onCancel();
        }
    }
}
