using System;
using System.Threading.Tasks;
using Android.App;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidDialogService : IDialogService
{
    private readonly AndroidActivityProvider _activityProvider;

    public AndroidDialogService(AndroidActivityProvider activityProvider)
    {
        _activityProvider = activityProvider;
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
            builder.SetPositiveButton("OK", (_, _) => completion.TrySetResult(true));
            builder.SetNegativeButton("Cancel", (_, _) => completion.TrySetResult(false));
            builder.SetOnCancelListener(
                new CancelListener(() => completion.TrySetResult(false)));
            var dialog = builder.Create()
                ?? throw new InvalidOperationException("Android confirmation dialog could not be created.");
            dialog.Show();
        });
        return completion.Task;
    }

    public Task ShowUpsellAsync() =>
        ShowAlertAsync("Noctra Premium", "This feature requires Noctra Premium.");

    public Task<bool> ShowAddProfileAsync() => Task.FromResult(false);

    public Task<bool> ShowEditProfileAsync(Profile profile) => Task.FromResult(false);

    public Task ShowGlobalSettingsAsync() => Task.CompletedTask;

    public Task<string?> ShowAvatarPickerAsync(string? currentAvatar) =>
        Task.FromResult<string?>(null);

    public Task ShowNotificationAsync(string title, string message) =>
        ShowAlertAsync(title, message);

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
            builder.SetPositiveButton("OK", (_, _) => completion.TrySetResult(null));
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
