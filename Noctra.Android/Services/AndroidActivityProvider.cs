using System;
using Android.App;

namespace Noctra.Android.Services;

public sealed class AndroidActivityProvider
{
    private WeakReference<Activity>? _currentActivity;

    public Activity? CurrentActivity
    {
        get
        {
            return _currentActivity is not null &&
                _currentActivity.TryGetTarget(out var activity) &&
                !activity.IsFinishing &&
                !activity.IsDestroyed
                    ? activity
                    : null;
        }
    }

    public void SetCurrent(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _currentActivity = new WeakReference<Activity>(activity);
    }

    public void Clear(Activity activity)
    {
        if (_currentActivity is not null &&
            _currentActivity.TryGetTarget(out var current) &&
            ReferenceEquals(current, activity))
        {
            _currentActivity = null;
        }
    }
}
