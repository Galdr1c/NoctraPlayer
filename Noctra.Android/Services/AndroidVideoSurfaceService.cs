using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Graphics;
using Android.Views;
using WidgetFrameLayout = Android.Widget.FrameLayout;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidVideoSurfaceService : Java.Lang.Object, IVideoSurfaceService, TextureView.ISurfaceTextureListener
{
    private readonly AndroidActivityProvider _activityProvider;
    private readonly object _surfaceLock = new();
    private TextureView? _textureView;
    private TaskCompletionSource<Surface>? _surfaceReady;

    // EPG split görünümü için video yüzeyi konum/boyutu (piksel).
    // _boundsW/_boundsH <= 0 ise tam ekran (MatchParent).
    private int _boundsX;
    private int _boundsY;
    private int _boundsW = -1;
    private int _boundsH = -1;

    // Video layout state (aspect ratio / crop geometry)
    private string? _aspectRatio;
    private string? _cropGeometry;
    private int _videoWidth;
    private int _videoHeight;

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
                EnsureTextureView(activity);
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
            if (_textureView?.Parent is ViewGroup parent)
            {
                parent.RemoveView(_textureView);
            }

            if (_textureView is not null)
            {
                _textureView.SurfaceTextureListener = null;
            }
            _textureView?.Dispose();
            _textureView = null;

            lock (_surfaceLock)
            {
                _surfaceReady = null;
            }

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

    public void SetVideoLayout(string? aspectRatio, string? cropGeometry)
    {
        _aspectRatio = aspectRatio;
        _cropGeometry = cropGeometry;

        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(ApplyVideoTransform);
    }

    /// <summary>
    /// Sets the native video dimensions (from stream metadata) so the transform
    /// matrix can be calculated before the first frame arrives on the TextureView.
    /// </summary>
    public void SetVideoSize(int width, int height)
    {
        _videoWidth = width;
        _videoHeight = height;

        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(ApplyVideoTransform);
    }

    private void ApplyBounds()
    {
        if (_textureView is null)
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

        _textureView.LayoutParameters = layoutParams;
        _textureView.RequestLayout();
    }

    /// <summary>
    /// Computes and applies a Matrix transform on the TextureView to achieve the
    /// desired aspect ratio / crop behaviour.
    /// </summary>
    private void ApplyVideoTransform()
    {
        if (_textureView is null)
        {
            return;
        }

        var viewW = _textureView.Width;
        var viewH = _textureView.Height;
        if (viewW <= 0 || viewH <= 0 || _videoWidth <= 0 || _videoHeight <= 0)
        {
            return;
        }

        var matrix = CalculateTransformMatrix(
            viewW, viewH,
            _videoWidth, _videoHeight,
            _aspectRatio, _cropGeometry);

        _textureView.SetTransform(matrix);
    }

    /// <summary>
    /// Builds a Matrix that maps the video's natural rectangle into the view's
    /// rectangle, honouring the requested aspect-ratio and crop-geometry strings.
    /// <para>
    /// <b>aspectRatio</b> – forces the display aspect ratio (e.g. "16:9").
    /// Null means "use the video's own aspect ratio".<br/>
    /// <b>cropGeometry</b> – when set (e.g. "16:9"), the video is centre-cropped
    /// so the visible area matches this ratio. Null means no cropping (fit or
    /// stretch depending on <paramref name="aspectRatio"/>).
    /// </para>
    /// </summary>
    internal static Matrix CalculateTransformMatrix(
        int viewW, int viewH,
        int videoW, int videoH,
        string? aspectRatio,
        string? cropGeometry)
    {
        var matrix = new Matrix();

        if (videoW <= 0 || videoH <= 0 || viewW <= 0 || viewH <= 0)
        {
            return matrix;
        }

        // 1. Determine the effective video display aspect ratio.
        float darW, darH;
        if (aspectRatio is { Length: > 0 } && TryParseAspect(aspectRatio, out var aw, out var ah))
        {
            darW = aw;
            darH = ah;
        }
        else
        {
            darW = videoW;
            darH = videoH;
        }

        var videoAr = darW / darH;
        var viewAr = (float)viewW / viewH;

        // 2. Crop geometry specified → centre-cover to that ratio.
        if (cropGeometry is { Length: > 0 } && TryParseAspect(cropGeometry, out var cw, out var ch))
        {
            var cropAr = cw / ch;

            // Centre-cover: scale uniformly so the video *covers* the view, then
            // the view is sized to the crop AR by the caller (EPG split etc.).
            float scale;
            if (cropAr > viewAr)
            {
                // Crop area is wider than view → scale on width
                scale = (float)viewH * cropAr / viewW;
            }
            else
            {
                // Crop area is taller than view → scale on height
                scale = (float)viewW / (cropAr * viewH);
            }

            matrix.PostScale(scale, scale, viewW / 2f, viewH / 2f);
            return matrix;
        }

        // 3. No crop → fit the video inside the view while preserving the
        //    effective aspect ratio (letterbox / pillarbox as needed).
        if (Math.Abs(videoAr - viewAr) < 0.001f)
        {
            // Aspect ratios match → identity (video fills view perfectly).
            return matrix;
        }

        if (videoAr > viewAr)
        {
            // Video is wider than the view → fit to width, pillarbox top/bottom.
            // The identity matrix already maps texture X to view X correctly
            // (both are full-width). We only need to correct the Y axis so the
            // video isn't stretched vertically.
            var sy = viewAr / videoAr;
            matrix.PostScale(1f, sy, viewW / 2f, viewH / 2f);
        }
        else
        {
            // Video is taller than the view → fit to height, letterbox sides.
            var sx = videoAr / viewAr;
            matrix.PostScale(sx, 1f, viewW / 2f, viewH / 2f);
        }

        return matrix;
    }

    private static bool TryParseAspect(string aspect, out float w, out float h)
    {
        w = h = 0;
        if (string.IsNullOrWhiteSpace(aspect))
        {
            return false;
        }

        var parts = aspect.Split(':');
        if (parts.Length == 2 &&
            float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out w) &&
            float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out h) &&
            w > 0 && h > 0)
        {
            return true;
        }

        return false;
    }

    internal async Task<Surface?> WaitForSurfaceAsync()
    {
        await ShowAsync().ConfigureAwait(false);

        lock (_surfaceLock)
        {
            if (_textureView?.IsAttachedToWindow == true &&
                _textureView?.SurfaceTexture is { } st &&
                st.IsReleased == false)
            {
                return new Surface(st);
            }

            _surfaceReady ??= new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var tcs = _surfaceReady!;
        return await tcs.Task.ConfigureAwait(false);
    }

    // ── TextureView.ISurfaceTextureListener ──────────────────────────────────

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        TaskCompletionSource<Surface>? tcs;
        lock (_surfaceLock)
        {
            _surfaceReady ??= new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _surfaceReady;
        }

        var surfaceObj = new Surface(surface);
        tcs.TrySetResult(surfaceObj);

        // İlk boyut bilgisi geldiğinde transform'u uygula.
        _activityProvider.CurrentActivity?.RunOnUiThread(ApplyVideoTransform);
    }

    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
    {
        // Boyut değiştiğinde transform'u yeniden hesapla.
        _activityProvider.CurrentActivity?.RunOnUiThread(ApplyVideoTransform);
    }

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        lock (_surfaceLock)
        {
            _surfaceReady = new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return true; // Uygulamanın SurfaceTexture'ı serbest bırakmasına izin ver.
    }

    public void OnSurfaceTextureUpdated(SurfaceTexture surface)
    {
        // İlk kare geldiğinde view boyutlarıyla transform'u uygula.
        _activityProvider.CurrentActivity?.RunOnUiThread(ApplyVideoTransform);
    }

    private void EnsureTextureView(Activity activity)
    {
        if (_textureView is not null)
        {
            return;
        }

        var content = activity.Window?.DecorView?.FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
        if (content is null)
        {
            throw new InvalidOperationException("Android content root is unavailable.");
        }

        lock (_surfaceLock)
        {
            _surfaceReady = new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _textureView = new TextureView(activity);
        _textureView.SurfaceTextureListener = this;

        content.AddView(
            _textureView,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        // Daha önce EPG split için küçültülmüş bir konum ayarlandıysa onu yeniden uygula.
        ApplyBounds();
    }
}
