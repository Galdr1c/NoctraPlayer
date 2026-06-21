using System;
using System.Threading.Tasks;
using Android.App;
using Android.Views;
using WidgetFrameLayout = Android.Widget.FrameLayout;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidVideoSurfaceService : Java.Lang.Object, IVideoSurfaceService, ISurfaceHolderCallback
{
    private readonly AndroidActivityProvider _activityProvider;
    private SurfaceView? _surfaceView;
    private TaskCompletionSource<Surface>? _surfaceReady;

    // EPG split görünümü için video yüzeyi konum/boyutu (piksel).
    // _boundsW/_boundsH <= 0 ise tam ekran (MatchParent).
    private int _boundsX;
    private int _boundsY;
    private int _boundsW = -1;
    private int _boundsH = -1;

    public AndroidVideoSurfaceService(AndroidActivityProvider activityProvider)
    {
        _activityProvider = activityProvider;
    }

    public Task ShowAsync()
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                EnsureSurfaceView(activity);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    public void Hide()
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(() =>
        {
            if (_surfaceView?.Parent is ViewGroup parent)
            {
                parent.RemoveView(_surfaceView);
            }

            if (_surfaceView?.Holder is { } holder)
            {
                holder.RemoveCallback(this);
            }
            _surfaceView?.Dispose();
            _surfaceView = null;
            _surfaceReady = null;

            // Sonraki gösterimde tam ekran başlasın.
            _boundsW = -1;
            _boundsH = -1;
        });
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        _boundsX = x;
        _boundsY = y;
        _boundsW = width;
        _boundsH = height;

        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(ApplyBounds);
    }

    private void ApplyBounds()
    {
        if (_surfaceView is null)
        {
            return;
        }

        WidgetFrameLayout.LayoutParams layoutParams;
        if (_boundsW <= 0 || _boundsH <= 0)
        {
            // Tam ekran
            layoutParams = new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);
        }
        else
        {
            // Üst bölgeye küçültülmüş video (EPG split)
            layoutParams = new WidgetFrameLayout.LayoutParams(_boundsW, _boundsH)
            {
                LeftMargin = _boundsX,
                TopMargin = _boundsY,
            };
        }

        _surfaceView.LayoutParameters = layoutParams;
        _surfaceView.RequestLayout();
    }

    internal async Task<Surface?> WaitForSurfaceAsync()
    {
        await ShowAsync().ConfigureAwait(false);

        if (_surfaceView?.Holder?.Surface?.IsValid == true)
        {
            return _surfaceView.Holder.Surface;
        }

        _surfaceReady ??= new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        return await _surfaceReady.Task.ConfigureAwait(false);
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        if (holder.Surface?.IsValid == true)
        {
            _surfaceReady?.TrySetResult(holder.Surface);
        }
    }

    public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
    {
        if (holder.Surface?.IsValid == true)
        {
            _surfaceReady?.TrySetResult(holder.Surface);
        }
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _surfaceReady = new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void EnsureSurfaceView(Activity activity)
    {
        if (_surfaceView is not null)
        {
            return;
        }

        var content = activity.Window?.DecorView?.FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
        if (content is null)
        {
            throw new InvalidOperationException("Android content root is unavailable.");
        }

        _surfaceReady = new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        _surfaceView = new SurfaceView(activity);
        _surfaceView.SetZOrderMediaOverlay(false);
        if (_surfaceView.Holder is { } holder)
        {
            holder.AddCallback(this);
        }
        content.AddView(
            _surfaceView,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        // Daha önce EPG split için küçültülmüş bir konum ayarlandıysa onu yeniden uygula.
        ApplyBounds();
    }
}
