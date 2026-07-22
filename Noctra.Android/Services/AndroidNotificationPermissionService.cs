using Android.Content.PM;
using Android.OS;
using System.Threading.Tasks;

namespace Noctra.Android.Services;

public sealed class AndroidNotificationPermissionService
{
    public const int RequestCode = 24051;
    private const string PostNotificationsPermission = "android.permission.POST_NOTIFICATIONS";

    private readonly AndroidActivityProvider _activityProvider;
    private readonly object _gate = new();
    private TaskCompletionSource<bool>? _pendingRequest;

    public AndroidNotificationPermissionService(AndroidActivityProvider activityProvider)
    {
        _activityProvider = activityProvider;
    }

    public Task<bool> EnsureNotificationPermissionAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            return Task.FromResult(true);
        }

        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return Task.FromResult(false);
        }

        if (activity.CheckSelfPermission(PostNotificationsPermission) == Permission.Granted)
        {
            return Task.FromResult(true);
        }

        lock (_gate)
        {
            if (_pendingRequest is not null)
            {
                return _pendingRequest.Task;
            }

            _pendingRequest = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            activity.RunOnUiThread(() => activity.RequestPermissions(
                [PostNotificationsPermission],
                RequestCode));
            return _pendingRequest.Task;
        }
    }

    public bool TryHandleRequestPermissionsResult(
        int requestCode,
        Permission[] grantResults)
    {
        if (requestCode != RequestCode)
        {
            return false;
        }

        TaskCompletionSource<bool>? pending;
        lock (_gate)
        {
            pending = _pendingRequest;
            _pendingRequest = null;
        }

        pending?.TrySetResult(
            grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        return true;
    }
}
