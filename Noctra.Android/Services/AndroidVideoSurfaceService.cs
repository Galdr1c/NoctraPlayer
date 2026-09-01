using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using WidgetFrameLayout = Android.Widget.FrameLayout;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidVideoSurfaceService : Java.Lang.Object,
    IVideoSurfaceService,
    ISurfaceHolderCallback,
    TextureView.ISurfaceTextureListener,
    View.IOnLayoutChangeListener
{
    internal static int NativeBackdropSurfaceViewId { get; } = View.GenerateViewId();
    internal static int NativeVideoSurfaceViewId { get; } = View.GenerateViewId();

    private readonly AndroidActivityProvider _activityProvider;
    private readonly AndroidVideoSurfaceRendererSelection _rendererSelection;
    private readonly object _surfaceLock = new();
    private SurfaceView? _backdropSurfaceView;
    private SurfaceView? _videoSurfaceView;
    private BackdropSurfaceCallback? _backdropSurfaceCallback;
    private View? _textureFallbackBackdropView;
    private TextureView? _textureFallbackView;
    private Surface? _currentSurface;
    private bool _ownsCurrentSurface;
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
    private bool _isApplyingBounds;
    private bool _isPictureInPictureMode;
    private int _configurationGeneration;

    internal event EventHandler<Surface>? SurfaceAvailable;
    internal event EventHandler? SurfaceDestroyed;
    internal string RendererName => _rendererSelection.Renderer.ToString();

    public AndroidVideoSurfaceService(AndroidActivityProvider activityProvider)
    {
        _activityProvider = activityProvider;
        _rendererSelection = AndroidVideoSurfaceRendererPolicy.Select();

        Log.Info(
            "NoctraVideoSurface",
            $"Renderer={_rendererSelection.Renderer} " +
            $"Fallback={_rendererSelection.Fallback} " +
            $"Rule={_rendererSelection.Rule ?? "None"} " +
            $"Manufacturer={Build.Manufacturer ?? "Unknown"} " +
            $"Model={Build.Model ?? "Unknown"} Api={(int)Build.VERSION.SdkInt}");
    }

    internal static bool IsNativeSurfaceView(SurfaceView surfaceView)
        => surfaceView.Id is var id &&
           (id == NativeBackdropSurfaceViewId || id == NativeVideoSurfaceViewId);

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
                if (_rendererSelection.Renderer == AndroidVideoSurfaceRenderer.SurfaceView)
                {
                    EnsureSurfaceViews(activity);
                }
                else
                {
                    EnsureTextureFallbackView(activity);
                }

                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    public void Hide() => _ = HideAsync();

    internal Task ConcealVideoAsync()
        => RunOnUiThreadAsync(ConcealVideoViews);

    internal Task HideAsync()
        => RunOnUiThreadAsync(() =>
        {
            DetachRendererViews(notifySurfaceDestroyed: true);

            // Sonraki gösterimde tam ekran başlasın.
            _boundsX = 0;
            _boundsY = 0;
            _boundsW = -1;
            _boundsH = -1;
            _isPictureInPictureMode = false;
            ResetInteractionTransformState();
        });

    private Task RunOnUiThreadAsync(Action action)
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return Task.CompletedTask;
        }

        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        var activity = _activityProvider.CurrentActivity;
        if (_isPictureInPictureMode || activity?.IsInPictureInPictureMode == true)
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

        QueueBoundsReapply();
    }

    internal void SetPictureInPictureMode(bool isInPictureInPictureMode)
    {
        _isPictureInPictureMode = isInPictureInPictureMode;

        // PiP giriş/çıkışında eski EPG veya geçiş geometrisi taşınmamalı.
        _boundsX = 0;
        _boundsY = 0;
        _boundsW = -1;
        _boundsH = -1;
        QueueBoundsReapply();
    }

    internal void NotifyHostConfigurationChanged()
    {
        // Activity rotation is handled in-place (ConfigChanges). Keep the black
        // backdrop visible while Android and Avalonia settle on the new EPG slot;
        // otherwise the previous orientation's native crop is briefly exposed.
        BeginConfigurationTransition();
    }

    private void QueueBoundsReapply()
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        activity.RunOnUiThread(() => ApplyBounds());
    }

    private void BeginConfigurationTransition()
    {
        var activity = _activityProvider.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _configurationGeneration);
        activity.RunOnUiThread(() =>
        {
            ConcealVideoViews();
            ApplyBounds();

            var decorView = activity.Window?.DecorView;
            if (decorView is null)
            {
                return;
            }

            decorView.PostOnAnimation(new Java.Lang.Runnable(() =>
            {
                if (generation != Volatile.Read(ref _configurationGeneration))
                {
                    return;
                }

                decorView.PostOnAnimation(new Java.Lang.Runnable(() =>
                {
                    if (generation != Volatile.Read(ref _configurationGeneration))
                    {
                        return;
                    }

                    ApplyBounds();
                    RevealVideoViews();
                }));
            }));
        });
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

        activity.RunOnUiThread(() =>
        {
            ApplyBounds();
            ApplyVideoTransform();
        });
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
    /// matrix/buffer scaling can be calculated before the first frame arrives.
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

        activity.RunOnUiThread(() =>
        {
            ApplyBounds();
            ApplyVideoTransform();
        });
    }

    private void ApplyBounds(int measuredHostWidth = 0, int measuredHostHeight = 0)
    {
        if (_isApplyingBounds)
        {
            return;
        }

        _isApplyingBounds = true;
        try
        {
            ApplyBoundsCore(measuredHostWidth, measuredHostHeight);
        }
        finally
        {
            _isApplyingBounds = false;
        }
    }

    private void ApplyBoundsCore(int measuredHostWidth, int measuredHostHeight)
    {
        ApplyBackdropBounds();

        View? videoView = _rendererSelection.Renderer == AndroidVideoSurfaceRenderer.SurfaceView
            ? _videoSurfaceView
            : _textureFallbackView;
        if (videoView is null)
        {
            return;
        }

        var activity = _activityProvider.CurrentActivity;
        var forceImmediatePipLayout =
            _isPictureInPictureMode || activity?.IsInPictureInPictureMode == true;
        var content = activity?.Window?.DecorView?
            .FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
        var windowBounds = activity?.WindowManager?.CurrentWindowMetrics.Bounds;
        var rootW = measuredHostWidth > 0
            ? measuredHostWidth
            : (content?.Width > 0 ? content.Width : windowBounds?.Width()) ?? 0;
        var rootH = measuredHostHeight > 0
            ? measuredHostHeight
            : (content?.Height > 0 ? content.Height : windowBounds?.Height()) ?? 0;

        WidgetFrameLayout.LayoutParams layoutParams;
        if (_boundsW <= 0 || _boundsH <= 0)
        {
            if (rootW <= 0 || rootH <= 0)
            {
                // Root henüz ölçülmediyse ilk layout turunu MatchParent ile geçir.
                layoutParams = new WidgetFrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent);
            }
            else
            {
                layoutParams = CreateVideoLayoutParams(0, 0, rootW, rootH);
            }
        }
        else
        {
            // Native content root sınırlarına göre kırp (clipping)
            var targetX = _boundsX;
            var targetY = _boundsY;
            var targetW = _boundsW;
            var targetH = _boundsH;

            if (rootW > 0 && rootH > 0)
            {
                var left = Math.Clamp(targetX, 0, rootW);
                var top = Math.Clamp(targetY, 0, rootH);
                var right = Math.Clamp(targetX + targetW, 0, rootW);
                var bottom = Math.Clamp(targetY + targetH, 0, rootH);

                targetX = left;
                targetY = top;
                targetW = Math.Max(0, right - left);
                targetH = Math.Max(0, bottom - top);
            }
            else
            {
                targetX = Math.Max(0, targetX);
                targetY = Math.Max(0, targetY);
            }

            if (targetW <= 0 || targetH <= 0)
            {
                // Geçici geçersiz geometry — son geçerli native layout'u koru.
                // Bir sonraki LayoutUpdated zaten doğru rectangle'ı gönderecektir.
                return;
            }
            else
            {
                // Üst bölgeye küçültülmüş video (EPG split)
                layoutParams = CreateVideoLayoutParams(
                    targetX,
                    targetY,
                    targetW,
                    targetH);
            }
        }

        var videoLayoutChanged = ApplyLayoutParameters(videoView, layoutParams);
        if (forceImmediatePipLayout &&
            (videoView.Left != layoutParams.LeftMargin ||
             videoView.Top != layoutParams.TopMargin ||
             videoView.Width != layoutParams.Width ||
             videoView.Height != layoutParams.Height))
        {
            // Some Android OEMs can pause the PiP Activity's normal traversal while its
            // outer SurfaceView is resized. Apply the already calculated child
            // frame immediately so the media surface cannot lag behind the PiP.
            videoView.Layout(
                layoutParams.LeftMargin,
                layoutParams.TopMargin,
                layoutParams.LeftMargin + layoutParams.Width,
                layoutParams.TopMargin + layoutParams.Height);
        }

        if (videoLayoutChanged &&
            _videoSurfaceView?.Holder is { } videoHolder)
        {
            // SurfaceView's producer buffer may retain the previous EPG/rotation
            // size on some Android OEMs. Match it to the new view bounds only after a real
            // layout change; repeated calls would recreate the surface needlessly.
            videoHolder.SetSizeFromLayout();
        }
        ApplyVideoTransform();
    }

    private WidgetFrameLayout.LayoutParams CreateVideoLayoutParams(
        int targetX,
        int targetY,
        int targetWidth,
        int targetHeight)
    {
        var rect = _rendererSelection.Renderer == AndroidVideoSurfaceRenderer.SurfaceView
            ? VideoSurfaceLayoutCalculator.Calculate(
                targetX,
                targetY,
                targetWidth,
                targetHeight,
                _videoWidth,
                _videoHeight,
                _pixelWidthHeightRatio,
                _scaleMode)
            : new VideoSurfaceRect(targetX, targetY, targetWidth, targetHeight);

        return new WidgetFrameLayout.LayoutParams(rect.Width, rect.Height)
        {
            LeftMargin = rect.X,
            TopMargin = rect.Y,
        };
    }

    private void ApplyBackdropBounds()
    {
        View? backdrop = _rendererSelection.Renderer == AndroidVideoSurfaceRenderer.SurfaceView
            ? _backdropSurfaceView
            : _textureFallbackBackdropView;
        if (backdrop is null)
        {
            return;
        }

        ApplyLayoutParameters(
            backdrop,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
    }

    private static bool ApplyLayoutParameters(
        View view,
        WidgetFrameLayout.LayoutParams requested)
    {
        if (view.LayoutParameters is WidgetFrameLayout.LayoutParams current &&
            current.Width == requested.Width &&
            current.Height == requested.Height &&
            current.LeftMargin == requested.LeftMargin &&
            current.TopMargin == requested.TopMargin)
        {
            return false;
        }

        view.LayoutParameters = requested;
        view.RequestLayout();
        return true;
    }

    /// <summary>
    /// Computes and applies a Matrix transform on the TextureView to achieve the
    /// desired scale mode behaviour for the specified view dimensions.
    /// </summary>
    private void ApplyVideoTransform(int viewW, int viewH)
    {
        if (viewW <= 0 || viewH <= 0)
        {
            return;
        }

        if (_rendererSelection.Renderer == AndroidVideoSurfaceRenderer.SurfaceView)
        {
            ApplySurfaceViewInteractionTransform(viewW, viewH);
            return;
        }

        if (_textureFallbackView is null || _videoWidth <= 0 || _videoHeight <= 0)
        {
            return;
        }

        var matrix = CalculateTransformMatrix(
            viewW, viewH,
            _videoWidth, _videoHeight,
            _pixelWidthHeightRatio,
            _scaleMode);

        ApplyInteractionTransform(matrix, viewW, viewH);
        _textureFallbackView.SetTransform(matrix);
    }

    private void ApplyVideoTransform()
    {
        View? videoView = _rendererSelection.Renderer == AndroidVideoSurfaceRenderer.SurfaceView
            ? _videoSurfaceView
            : _textureFallbackView;
        if (videoView is null)
        {
            return;
        }

        ApplyVideoTransform(videoView.Width, videoView.Height);
    }

    private void ApplySurfaceViewInteractionTransform(int viewW, int viewH)
    {
        if (_videoSurfaceView is null)
        {
            return;
        }

        var maxPanX = viewW * (_userZoom - 1f) / 2f;
        var maxPanY = viewH * (_userZoom - 1f) / 2f;
        _userPanX = Math.Clamp(_userPanX, -maxPanX, maxPanX);
        _userPanY = Math.Clamp(_userPanY, -maxPanY, maxPanY);

        _videoSurfaceView.PivotX = viewW / 2f;
        _videoSurfaceView.PivotY = viewH / 2f;
        _videoSurfaceView.ScaleX = _userZoom;
        _videoSurfaceView.ScaleY = _userZoom;
        _videoSurfaceView.TranslationX = _userPanX;
        _videoSurfaceView.TranslationY = _userPanY;
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
            if (IsSurfaceUsable(_currentSurface))
            {
                return _currentSurface;
            }

            ReleaseSurfaceLocked();

            if (_rendererSelection.Renderer == AndroidVideoSurfaceRenderer.SurfaceView &&
                _videoSurfaceView?.IsAttachedToWindow == true &&
                _videoSurfaceView.Holder?.Surface is { } holderSurface &&
                IsSurfaceUsable(holderSurface))
            {
                _currentSurface = holderSurface;
                _ownsCurrentSurface = false;
                return _currentSurface;
            }

            if (_textureFallbackView?.IsAttachedToWindow == true &&
                _textureFallbackView.SurfaceTexture is { } texture &&
                texture.IsReleased == false)
            {
                _currentSurface = new Surface(texture);
                _ownsCurrentSurface = true;
                return _currentSurface;
            }

            _surfaceReady ??= new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var tcs = _surfaceReady!;
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return completed == tcs.Task ? await tcs.Task.ConfigureAwait(false) : null;
    }

    // ── SurfaceView / SurfaceHolder lifecycle ────────────────────────────────

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        if (holder.Surface is not { } surface || !IsSurfaceUsable(surface))
        {
            return;
        }

        PublishSurface(surface, ownsSurface: false);
        ApplyBounds();
        ApplyVideoTransform();
    }

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
    {
        if (!IsSurfaceUsable(_currentSurface) && holder.Surface is { } surface && IsSurfaceUsable(surface))
        {
            PublishSurface(surface, ownsSurface: false);
        }

        ApplyBounds();
        ApplyVideoTransform(width, height);
    }

    void ISurfaceHolderCallback.SurfaceDestroyed(ISurfaceHolder holder)
        => ClearCurrentSurface(notifySurfaceDestroyed: true, prepareNextSurface: true);

    // ── TextureView.ISurfaceTextureListener ──────────────────────────────────

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        PublishSurface(new Surface(surface), ownsSurface: true);

        // İlk boyut bilgisi geldiğinde transform'u uygula.
        _activityProvider.CurrentActivity?.RunOnUiThread(() => ApplyVideoTransform(width, height));
    }

    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
    {
        // Boyut değiştiğinde transform'u yeniden hesapla.
        _activityProvider.CurrentActivity?.RunOnUiThread(() => ApplyVideoTransform(width, height));
    }

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        ClearCurrentSurface(notifySurfaceDestroyed: true, prepareNextSurface: true);
        return true; // Uygulamanın SurfaceTexture'ı serbest bırakmasına izin ver.
    }

    private void NotifySurfaceDestroyed()
        => SurfaceDestroyed?.Invoke(this, EventArgs.Empty);

    public void OnSurfaceTextureUpdated(SurfaceTexture surface)
    {
        // Video ölçekleme matrisi yalnızca boyut/mode/view geometrisi değiştiğinde
        // güncellenir; her karede hesaplamak gereksiz UI-thread yüküdür.
    }

    // ── View.IOnLayoutChangeListener ─────────────────────────────────────────

    public void OnLayoutChange(
        View? v,
        int left,
        int top,
        int right,
        int bottom,
        int oldLeft,
        int oldTop,
        int oldRight,
        int oldBottom)
    {
        var width = right - left;
        var height = bottom - top;

        if (v?.Id == NativeBackdropSurfaceViewId)
        {
            // PiP free-resize sırasında WindowMetrics OEM'e göre bir frame geriden
            // gelebilir. MatchParent backdrop'un callback ölçüsü, dış PiP kabının
            // gerçekten uygulanmış boyutudur; video rect'ini doğrudan bundan üret.
            if (width > 0 && height > 0)
            {
                ApplyBounds(width, height);
            }

            return;
        }

        if (width > 0 && height > 0)
        {
            ApplyVideoTransform(width, height);
        }
    }

    private void PublishSurface(Surface surface, bool ownsSurface)
    {
        TaskCompletionSource<Surface> ready;
        lock (_surfaceLock)
        {
            ReleaseSurfaceLocked();
            _currentSurface = surface;
            _ownsCurrentSurface = ownsSurface;
            _surfaceReady ??= new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
            ready = _surfaceReady;
        }

        ready.TrySetResult(surface);
        SurfaceAvailable?.Invoke(this, surface);
    }

    private void ClearCurrentSurface(bool notifySurfaceDestroyed, bool prepareNextSurface)
    {
        bool hadSurface;
        lock (_surfaceLock)
        {
            hadSurface = _currentSurface is not null;
            ReleaseSurfaceLocked();
            _surfaceReady = prepareNextSurface
                ? new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously)
                : null;
        }

        if (notifySurfaceDestroyed && hadSurface)
        {
            NotifySurfaceDestroyed();
        }
    }

    private void ReleaseSurfaceLocked()
    {
        if (_ownsCurrentSurface)
        {
            _currentSurface?.Dispose();
        }

        // A SurfaceHolder owns SurfaceView surfaces. Disposing that wrapper here
        // would invalidate the producer/consumer queue behind the live view.
        _currentSurface = null;
        _ownsCurrentSurface = false;
    }

    private static bool IsSurfaceUsable(Surface? surface)
    {
        try
        {
            return surface?.IsValid == true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachRendererViews(notifySurfaceDestroyed: false);
        }

        base.Dispose(disposing);
    }

    private void EnsureSurfaceViews(Activity activity)
    {
        if (_backdropSurfaceView?.Parent is not null && _videoSurfaceView?.Parent is not null)
        {
            return;
        }

        DetachRendererViews(notifySurfaceDestroyed: true);
        var content = GetContentRoot(activity);

        lock (_surfaceLock)
        {
            _surfaceReady = new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _backdropSurfaceView = CreateNativeSurfaceView(
            activity,
            NativeBackdropSurfaceViewId,
            isMediaOverlay: false);
        _backdropSurfaceView.AddOnLayoutChangeListener(this);
        _backdropSurfaceCallback = new BackdropSurfaceCallback(this);
        var backdropHolder = _backdropSurfaceView.Holder
            ?? throw new InvalidOperationException("Android backdrop SurfaceHolder is unavailable.");
        backdropHolder.AddCallback(_backdropSurfaceCallback);

        _videoSurfaceView = CreateNativeSurfaceView(
            activity,
            NativeVideoSurfaceViewId,
            isMediaOverlay: true);
        _videoSurfaceView.AddOnLayoutChangeListener(this);
        var videoHolder = _videoSurfaceView.Holder
            ?? throw new InvalidOperationException("Android video SurfaceHolder is unavailable.");
        videoHolder.AddCallback(this);

        // Surface z-order is determined before attachment: black media surface,
        // then video media-overlay surface, then Avalonia's translucent controls.
        content.AddView(
            _backdropSurfaceView,
            content.ChildCount,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        content.AddView(
            _videoSurfaceView,
            content.ChildCount,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        ApplyBackdropBounds();
        ApplyBounds();

        Log.Info(
            "NoctraVideoSurface",
            "Renderer=SurfaceView Fallback=None Surface=Opaque " +
            "Layering=Backdrop<VideoMediaOverlay<AvaloniaControls");
    }

    private void EnsureTextureFallbackView(Activity activity)
    {
        if (_textureFallbackView?.Parent is not null)
        {
            return;
        }

        DetachRendererViews(notifySurfaceDestroyed: true);
        var content = GetContentRoot(activity);

        lock (_surfaceLock)
        {
            _surfaceReady = new TaskCompletionSource<Surface>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _textureFallbackBackdropView = new View(activity);
        ConfigurePassiveView(_textureFallbackBackdropView);
        _textureFallbackBackdropView.SetBackgroundColor(Color.Black);
        content.AddView(
            _textureFallbackBackdropView,
            content.ChildCount,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        _textureFallbackView = new TextureView(activity);
        ConfigurePassiveView(_textureFallbackView);
        _textureFallbackView.SurfaceTextureListener = this;
        _textureFallbackView.AddOnLayoutChangeListener(this);
        content.AddView(
            _textureFallbackView,
            content.ChildCount,
            new WidgetFrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        ApplyBackdropBounds();
        ApplyBounds();

        Log.Info(
            "NoctraVideoSurface",
            $"Renderer=TextureView Fallback={_rendererSelection.Fallback} " +
            $"Rule={_rendererSelection.Rule ?? "Unknown"}");
    }

    private static SurfaceView CreateNativeSurfaceView(
        Activity activity,
        int id,
        bool isMediaOverlay)
    {
        var surfaceView = new SurfaceView(activity)
        {
            Id = id,
        };
        ConfigurePassiveView(surfaceView);
        surfaceView.SetZOrderMediaOverlay(isMediaOverlay);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            surfaceView.SetSurfaceLifecycle(SurfaceViewLifecycle.FollowsAttachment);
        }

        var holder = surfaceView.Holder
            ?? throw new InvalidOperationException("Android native SurfaceHolder is unavailable.");
        holder.SetFormat(Format.Opaque);
        return surfaceView;
    }

    private static void ConfigurePassiveView(View view)
    {
        view.Clickable = false;
        view.Focusable = false;
        view.ImportantForAccessibility = ImportantForAccessibility.No;
    }

    private static ViewGroup GetContentRoot(Activity activity)
        => activity.Window?.DecorView?
               .FindViewById(global::Android.Resource.Id.Content) as ViewGroup
           ?? throw new InvalidOperationException("Android content root is unavailable.");

    private void DetachRendererViews(bool notifySurfaceDestroyed)
    {
        ConcealVideoViews();

        // Decoder eski Surface'e yazmayı views kaldırılmadan önce bırakır.
        ClearCurrentSurface(
            notifySurfaceDestroyed,
            prepareNextSurface: false);

        if (_videoSurfaceView is not null)
        {
            _videoSurfaceView.RemoveOnLayoutChangeListener(this);
        }

        if (_backdropSurfaceView is not null)
        {
            _backdropSurfaceView.RemoveOnLayoutChangeListener(this);
        }

        if (_videoSurfaceView?.Holder is { } videoHolder)
        {
            videoHolder.RemoveCallback(this);
        }

        if (_backdropSurfaceView?.Holder is { } backdropHolder &&
            _backdropSurfaceCallback is not null)
        {
            backdropHolder.RemoveCallback(_backdropSurfaceCallback);
        }

        if (_textureFallbackView is not null)
        {
            _textureFallbackView.SurfaceTextureListener = null;
            _textureFallbackView.RemoveOnLayoutChangeListener(this);
        }

        if (_videoSurfaceView?.Parent is ViewGroup videoParent)
        {
            videoParent.RemoveView(_videoSurfaceView);
        }

        if (_backdropSurfaceView?.Parent is ViewGroup backdropParent)
        {
            backdropParent.RemoveView(_backdropSurfaceView);
        }

        if (_textureFallbackView?.Parent is ViewGroup textureParent)
        {
            textureParent.RemoveView(_textureFallbackView);
        }

        if (_textureFallbackBackdropView?.Parent is ViewGroup textureBackdropParent)
        {
            textureBackdropParent.RemoveView(_textureFallbackBackdropView);
        }

        _videoSurfaceView?.Dispose();
        _videoSurfaceView = null;
        _backdropSurfaceView?.Dispose();
        _backdropSurfaceView = null;
        _backdropSurfaceCallback?.Dispose();
        _backdropSurfaceCallback = null;
        _textureFallbackView?.Dispose();
        _textureFallbackView = null;
        _textureFallbackBackdropView?.Dispose();
        _textureFallbackBackdropView = null;

    }

    private void ConcealVideoViews()
    {
        if (_videoSurfaceView is not null)
        {
            _videoSurfaceView.Visibility = ViewStates.Invisible;
        }

        if (_textureFallbackView is not null)
        {
            _textureFallbackView.Visibility = ViewStates.Invisible;
        }
    }

    private void RevealVideoViews()
    {
        if (_videoSurfaceView is not null)
        {
            _videoSurfaceView.Visibility = ViewStates.Visible;
        }

        if (_textureFallbackView is not null)
        {
            _textureFallbackView.Visibility = ViewStates.Visible;
        }
    }

    private void DrawBackdropBlack(ISurfaceHolder holder)
    {
        Canvas? canvas = null;
        try
        {
            canvas = holder.LockCanvas();
            canvas?.DrawColor(Color.Black);
        }
        catch (Exception ex)
        {
            Log.Warn("NoctraVideoSurface", $"Native black backdrop draw failed: {ex.Message}");
        }
        finally
        {
            if (canvas is not null)
            {
                holder.UnlockCanvasAndPost(canvas);
            }
        }
    }

    private void OnBackdropSurfaceChanged(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var backdrop = _backdropSurfaceView;
        _backdropSurfaceView?.Post(() =>
        {
            if (ReferenceEquals(backdrop, _backdropSurfaceView))
            {
                ApplyBounds(width, height);
            }
        });
    }

    private sealed class BackdropSurfaceCallback : Java.Lang.Object, ISurfaceHolderCallback
    {
        private readonly AndroidVideoSurfaceService _owner;

        internal BackdropSurfaceCallback(AndroidVideoSurfaceService owner)
        {
            _owner = owner;
        }

        public void SurfaceCreated(ISurfaceHolder holder)
            => _owner.DrawBackdropBlack(holder);

        public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
        {
            _owner.DrawBackdropBlack(holder);
            _owner.OnBackdropSurfaceChanged(width, height);
        }

        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
        }
    }
}
