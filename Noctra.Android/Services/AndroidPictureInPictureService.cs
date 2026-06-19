using System;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Util;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidPictureInPictureService : IPictureInPictureService
{
    private readonly AndroidActivityProvider _activityProvider;

    public AndroidPictureInPictureService(AndroidActivityProvider activityProvider)
    {
        _activityProvider = activityProvider;
    }

    public event EventHandler<PictureInPictureModeChangedEventArgs>? PictureInPictureModeChanged;

    public bool IsSupported => Build.VERSION.SdkInt >= BuildVersionCodes.O;

    public Task<bool> EnterPictureInPictureAsync()
    {
        var activity = _activityProvider.CurrentActivity;
        if (!IsSupported || activity is null)
        {
            return Task.FromResult(false);
        }

        var builder = new PictureInPictureParams.Builder();
        builder.SetAspectRatio(new Rational(16, 9));
        var parameters = builder.Build();
        if (parameters is null)
        {
            return Task.FromResult(false);
        }

        var entered = activity.EnterPictureInPictureMode(parameters);
        return Task.FromResult(entered);
    }

    public void NotifyPictureInPictureModeChanged(bool isInPictureInPictureMode)
    {
        PictureInPictureModeChanged?.Invoke(
            this,
            new PictureInPictureModeChangedEventArgs(isInPictureInPictureMode));
    }
}
