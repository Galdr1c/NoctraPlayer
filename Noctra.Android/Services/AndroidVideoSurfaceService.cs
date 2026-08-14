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
    private View? _backdropView;
    private TextureView? _textureView;
    private Surface? _currentSurface;
    private TaskCompletionSource<Surface>? _surfaceReady;

    // EPG split görünümü için video yüzeyi konum/boyutu (piksel).
    // _boundsW/_boundsH <= 0 ise tam ekran (MatchParent).
    private int _boundsX;
    private int _boundsY;
    private int _boundsW = -1;
    private int _boundsH = -1;

    // Video layout state (scale mode / video dimensions)
    private Noctra.Models.VideoScaleMode _scaleMode = Noctra.Models.VideoScaleMode.Fit;
    private int _videoWidth;
    private int _videoHeight;
    private float _pixelWidthHeightRatio = 1f;
    private float _userZoom = 1f;
    private float _userPanX;
    private float _userPanY;

    internal event EventHandler<Surface>? SurfaceAvailable;
    internal event EventHandler? SurfaceDestroyed;

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

            if (_backdropView?.Parent is ViewGroup backdropParent)
            {
                backdropParent.RemoveView(_backdropView);
            }

            if (_textureView is not null)
            {
                _textureView.SurfaceTextureListener = null;
            }
            _textureView?.Dispose();
            _textureView = null;
            _backdropView?.Dispose();
            _backdropView = null;

            lock (_surfaceLock)
            {
                ReleaseSurface();
                _surfaceReady = null;
            }

            // Sonraki gösterimde tam ekran başlasın.
            SurfaceDestroyed?.Invoke(this, EventArgs.Empty);
            _boundsW = -1;
            _boundsH = -1;
            ResetInteractionTransformState();
        });
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity?.IsInPictureInPictureMode == true)
        {
            // PiP resizes the Avalonia tree through transient 1x1 measurements.
            // The native video surface must fill the PiP activity window instead
            // of accepting those placeholder dimensions.
            _boundsX = 0;
            _boundsY = 0;
            _boundsW = -1;
            _boundsH = -1;
        }
        else
        {
            _boundsX = x;
            _boundsY = y;
            _boundsW = width;
            _boundsH = height;
        }

        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(ApplyBounds);
    }

    public void SetVideoLayout(Noctra.Models.VideoScaleMode scaleMode)
    {
        _scaleMode = scaleMode;
        ResetInteractionTransformState();

        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(ApplyVideoTransform);
    }

    public void SetInteractionTransform(float zoom, float panX, float panY)
    {
        _userZoom = Math.Clamp(zoom, 1f, 3f);
        _userPanX = panX;
        _userPanY = panY;

        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(ApplyVideoTransform);
    }

    public void ResetInteractionTransform()
    {
        ResetInteractionTransformState();

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
    /// <paramref name="pixelWidthHeightRatio"/> accounts for anamorphic content
    /// (non-square pixels); the display aspect ratio is
    /// (width × ratio) / height.
    /// </summary>
    public void SetVideoSize(int width, int height, float pixelWidthHeightRatio = 1f)
    {
        _videoWidth = width;
        _videoHeight = height;
        _pixelWidthHeightRatio = pixelWidthHeightRatio > 0 ? pixelWidthHeightRatio : 1f;

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
    /// desired scale mode behaviour.
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
            _pixelWidthHeightRatio,
            _scaleMode);

        ApplyInteractionTransform(matrix, viewW, viewH);
        _textureView.SetTransform(matrix);
    }

    private void ApplyInteractionTransform(Matrix matrix, int viewW, int viewH)
    {
        if (_userZoom <= 1.001f)
        {
            return;
        }

        var maxPanX = viewW * (_userZoom - 1f) / 2f;
        var maxPanY = viewH * (_userZoom - 1f) / 2f;
        _userPanX = Math.Clamp(_userPanX, -maxPanX, maxPanX);
        _userPanY = Math.Clamp(_userPanY, -maxPanY, maxPanY);

        matrix.PostScale(_userZoom, _userZoom, viewW / 2f, viewH / 2f);
        matrix.PostTranslate(_userPanX, _userPanY);
    }

    private void ResetInteractionTransformState()
    {
        _userZoom = 1f;
        _userPanX = 0f;
        _userPanY = 0f;
    }

    /// <summary>
    /// Builds a Matrix that maps the video's natural rectangle into the view's
    /// rectangle, honouring the requested scale mode.
    /// <para>
    /// <b>Fit</b> – preserves the video's display aspect ratio and fits it inside
    /// the view (letterbox/pillarbox).<br/>
    /// <b>Fill</b> – preserves the display aspect ratio and covers the view
    /// completely (centre-crop: the video is scaled up until it fills the view,
    /// overflowing content is cropped).<br/>
    /// <b>Stretch</b> – ignores the aspect ratio entirely and maps the video onto
    /// the full view rectangle (distortion is expected).
    /// </para>
    /// <paramref name="pixelWidthHeightRatio"/> is applied to the source width so
    /// anamorphic content (non-square pixels) gets its correct display ratio.
    /// </summary>
    internal static Matrix CalculateTransformMatrix(
        int viewW, int viewH,
        int videoW, int videoH,
        float pixelWidthHeightRatio,
        Noctra.Models.VideoScaleMode scaleMode)
    {
        var matrix = new Matrix();

        if (videoW <= 0 || videoH <= 0 || viewW <= 0 || viewH <= 0)
        {
            return matrix;
        }

        // Display aspect ratio of the source (accounts for anamorphic pixels).
        var darW = videoW * (pixelWidthHeightRatio > 0 ? pixelWidthHeightRatio : 1f);
        var videoAr = darW / videoH;
        var viewAr = (float)viewW / viewH;

        if (scaleMode == Noctra.Models.VideoScaleMode.Stretch)
        {
            // Identity: TextureView maps the texture onto the full view rectangle,
            // which is exactly what "stretch" means.
            return matrix;
        }

        if (Math.Abs(videoAr - viewAr) < 0.001f)
        {
            // Aspect ratios match → identity (video fills view perfectly).
            return matrix;
        }

        if (scaleMode == Noctra.Models.VideoScaleMode.Fill)
        {
            // Centre-cover: scale uniformly so the video *covers* the view, then
            // the overflowing part is cropped by the view bounds.
            if (videoAr > viewAr)
            {
                // Video is wider than the view → scale to width, crop left/right.
                matrix.PostScale(videoAr / viewAr, 1f, viewW / 2f, viewH / 2f);
            }
            else
            {
                // Video is taller than the view → scale to height, crop top/bottom.
                matrix.PostScale(1f, viewAr / videoAr, viewW / 2f, viewH / 2f);
            }

            return matrix;
        }

        // Fit: fit the video inside the view while preserving the effective
        // aspect ratio (letterbox / pillarbox as needed).
        if (videoAr > viewAr)
        {
            // Video is wider than the view → fit to width, pillarbox top/bottom.
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

    internal async Task<Surface?> WaitForSurfaceAsync(TimeSpan timeout)
    {
        await ShowAsync().ConfigureAwait(false);

        lock (_surfaceLock)
        {
            if (_currentSurface is not null)
            {
                return _currentSurface;
            }

            if (_textureView?.IsAttachedToWindow == true &&
                _textureView?.SurfaceTexture is { } st &&
                st.IsReleased == false)
            {
                ReplaceSurface(st);
                return _currentSurface;
            }

            _surfaceReady ??= new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var tcs = _surfaceReady!;
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);
        return completed == tcs.Task ? await tcs.Task.ConfigureAwait(false) : null;
    }

    // ── TextureView.ISurfaceTextureListener ──────────────────────────────────

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        TaskCompletionSource<Surface>? tcs;
        Surface surfaceObj;
        lock (_surfaceLock)
        {
            ReplaceSurface(surface);
            _surfaceReady ??= new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _surfaceReady;
            surfaceObj = _currentSurface!;
        }

        tcs.TrySetResult(surfaceObj);
        SurfaceAvailable?.Invoke(this, surfaceObj);

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
            ReleaseSurface();
            _surfaceReady = new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        NotifySurfaceDestroyed();
        return true; // Uygulamanın SurfaceTexture'ı serbest bırakmasına izin ver.
    }

    private void NotifySurfaceDestroyed()
        => SurfaceDestroyed?.Invoke(this, EventArgs.Empty);

    public void OnSurfaceTextureUpdated(SurfaceTexture surface)
    {
        // Video ölçekleme matrisi yalnızca boyut/mode/view geometrisi değiştiğinde
        // güncellenir; her karede hesaplamak gereksiz UI-thread yüküdür.
    }

    private void ReplaceSurface(SurfaceTexture texture)
    {
        _currentSurface?.Dispose();
        _currentSurface = new Surface(texture);
    }

    private void ReleaseSurface()
    {
        _currentSurface?.Dispose();
        _currentSurface = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_surfaceLock)
            {
                ReleaseSurface();
                _surfaceReady = null;
            }

            if (_textureView is not null)
            {
                _textureView.SurfaceTextureListener = null;
                _textureView?.Dispose();
                _textureView = null;
            }

            _backdropView?.Dispose();
            _backdropView = null;
        }

        base.Dispose(disposing);
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

        // TextureView does not support background drawables. A separate black
        // regular view supplies the letterbox/pillarbox colour without touching
        // the decoded-video surface.
        _backdropView = new View(activity);
        _backdropView.SetBackgroundColor(Color.Black);
        _backdropView.Clickable = false;
        _backdropView.Focusable = false;
        _backdropView.ImportantForAccessibility = ImportantForAccessibility.No;
        content.AddView(
            _backdropView,
            content.ChildCount,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        _textureView = new TextureView(activity);
        _textureView.SurfaceTextureListener = this;
        _textureView.Clickable = false;
        _textureView.Focusable = false;
        _textureView.ImportantForAccessibility = ImportantForAccessibility.No;

        // TextureView is the last regular Android view so its decoded pixels are
        // preserved in the window buffer. Avalonia's translucent SurfaceView is
        // composed above that window and keeps the player controls on top.
        content.AddView(
            _textureView,
            content.ChildCount,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        // Daha önce EPG split için küçültülmüş bir konum ayarlandıysa onu yeniden uygula.
        ApplyBounds();
    }
}
