using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibVLCSharp.Shared;

namespace Noctra.Avalonia.Controls;

/// <summary>
/// NativeControlHost-based video view with floating overlay support.
/// VLC renders directly to a native HWND (artifact-free).
/// Use the <see cref="OverlayContent"/> property to place Avalonia controls on top.
/// </summary>
public class MemoryVideoView : NativeControlHost
{
    public static readonly StyledProperty<MediaPlayer?> MediaPlayerProperty =
        AvaloniaProperty.Register<MemoryVideoView, MediaPlayer?>(nameof(MediaPlayer));

    public static readonly StyledProperty<Control?> OverlayContentProperty =
        AvaloniaProperty.Register<MemoryVideoView, Control?>(nameof(OverlayContent));

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private MediaPlayer? _mediaPlayer;
    private IPlatformHandle? _platformHandle;
    
    // Floating overlay window to solve HWND airspace issue
    private Window? _overlayWindow;
    private Window? _rootWindow;
    private bool _isAttached;
    private bool _overlayPositionUpdateQueued;
    private OverlayVisibilityController _visibilityController = null!;

    public MediaPlayer? MediaPlayer
    {
        get => GetValue(MediaPlayerProperty);
        set => SetValue(MediaPlayerProperty, value);
    }

    public Control? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    static MemoryVideoView()
    {
        OverlayContentProperty.Changed.AddClassHandler<MemoryVideoView>((x, e) => x.OnOverlayContentChanged(e));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MediaPlayerProperty)
        {
            Detach();
            _mediaPlayer = change.NewValue as MediaPlayer;
            Attach();
        }
    }

    public event EventHandler? NativeHandleReady;

    public Task WaitForHandleReadyAsync()
    {
        if (_platformHandle != null) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        EventHandler? handler = null;
        handler = (s, e) =>
        {
            NativeHandleReady -= handler;
            tcs.TrySetResult();
        };
        NativeHandleReady += handler;
        return tcs.Task;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _platformHandle = base.CreateNativeControlCore(parent);
        Attach();
        NativeHandleReady?.Invoke(this, EventArgs.Empty);
        return _platformHandle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Detach();
        base.DestroyNativeControlCore(control);
        _platformHandle = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        _rootWindow = e.Root as Window;
        InitializeOverlay();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        DestroyOverlay();
        _rootWindow = null;
    }

    public void ForceRefresh()
    {
        Attach();
    }

    private void Attach()
    {
        if (_mediaPlayer == null || _platformHandle == null) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                _mediaPlayer.Hwnd = _platformHandle.Handle;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                _mediaPlayer.XWindow = (uint)_platformHandle.Handle;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                _mediaPlayer.NsObject = _platformHandle.Handle;
        }
        catch { }
    }

    private void Detach()
    {
        if (_mediaPlayer == null || _platformHandle == null) return;

        var player = _mediaPlayer;
        var handle = _platformHandle.Handle;

        // Note: We don't use Post here because we want to detach 
        // immediately before the control is destroyed or handle becomes invalid.
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (player.Hwnd == handle)
                {
                    player.Hwnd = IntPtr.Zero;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (player.XWindow == (uint)handle)
                {
                    player.XWindow = 0;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (player.NsObject == handle)
                {
                    player.NsObject = IntPtr.Zero;
                }
            }
        }
        catch { }
    }

    // ─── Overlay Management ───────────────────────────────────────

    private void OnOverlayContentChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_overlayWindow != null)
        {
            _overlayWindow.Content = e.NewValue as Control;
        }
    }

    private void InitializeOverlay()
    {
        _visibilityController = new OverlayVisibilityController(
            isOverlayCurrentlyVisible: () => _overlayWindow?.IsVisible == true,
            showOverlay: () =>
            {
                if (_overlayWindow == null)
                {
                    CreateOverlayWindow();
                    return;
                }

                QueueOverlayPositionUpdate();
                _overlayWindow.Show();
            },
            hideOverlay: () => _overlayWindow?.Hide(),
            isOverlayTopmost: () => _overlayWindow?.Topmost == true,
            setOverlayTopmost: value =>
            {
                if (_overlayWindow != null)
                    _overlayWindow.Topmost = value;
            }
        );

        LayoutUpdated += OnLayoutUpdated;
        if (_rootWindow != null)
        {
            _rootWindow.PositionChanged += Root_PositionChanged;
            _rootWindow.SizeChanged += Root_SizeChanged;
            _rootWindow.PropertyChanged += Root_PropertyChanged;
            _rootWindow.Activated += Root_Activated;
        }

        // Initial update
        QueueOverlayPositionUpdate();
        OnLayoutUpdated(this, EventArgs.Empty);
    }

    private void DestroyOverlay()
    {
        LayoutUpdated -= OnLayoutUpdated;

        if (_rootWindow != null)
        {
            _rootWindow.PositionChanged -= Root_PositionChanged;
            _rootWindow.SizeChanged -= Root_SizeChanged;
            _rootWindow.PropertyChanged -= Root_PropertyChanged;
            _rootWindow.Activated -= Root_Activated;
        }

        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;
        }
    }

    private void Root_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsVisibleProperty ||
            e.Property.Name is "WindowState" or "Topmost")
        {
            UpdateOverlayVisibility();
        }

        if (e.Property.Name is "WindowState" or "ClientSize")
        {
            QueueOverlayPositionUpdate();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        UpdateOverlayVisibility();
        QueueOverlayPositionUpdate();
    }

    private void UpdateOverlayVisibility()
    {
        bool isVisible = IsEffectivelyVisible
            && _rootWindow?.IsVisible == true
            && _rootWindow.WindowState != WindowState.Minimized;

        _visibilityController?.Update(isVisible, _rootWindow?.Topmost == true);
    }


    private void Root_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        HandleWindowMovement();
    }

    private void Root_Activated(object? sender, EventArgs e)
    {
        RestoreOverlayZOrder();
    }

    private void Root_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        HandleWindowMovement();
    }

    private void HandleWindowMovement()
    {
        if (_overlayWindow == null) return;
        QueueOverlayPositionUpdate();
    }

    private void CreateOverlayWindow()
    {
        if (_rootWindow == null) return;

        _overlayWindow = new Window
        {
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            ShowInTaskbar = false,
            CanResize = false,
            Title = "", // Empty title to help hide from Alt-Tab
            SizeToContent = SizeToContent.Manual,
            Topmost = _rootWindow.Topmost,
            Focusable = false,
            Content = OverlayContent
        };

        // Fix crash: If window is closed (e.g. via Alt+F4 or Task Switcher), reset reference
        _overlayWindow.Closed += (s, e) =>
        {
            _overlayWindow = null;
        };

        // Aggressive Alt-Tab hiding for Windows
        _overlayWindow.Opened += (s, e) =>
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _overlayWindow != null)
            {
                var handle = _overlayWindow.TryGetPlatformHandle()?.Handle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                {
                    int exStyle = GetWindowLong(handle.Value, GWL_EXSTYLE);
                    SetWindowLong(handle.Value, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
                }
            }
        };

        _overlayWindow.Show(_rootWindow);
        QueueOverlayPositionUpdate();
        RestoreOverlayZOrder();
    }

    private void RestoreOverlayZOrder()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            _overlayWindow?.IsVisible != true)
        {
            return;
        }

        var handle = _overlayWindow.TryGetPlatformHandle()?.Handle;
        if (!handle.HasValue || handle.Value == IntPtr.Zero)
            return;

        SetWindowPos(
            handle.Value,
            HWND_TOP,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void QueueOverlayPositionUpdate()
    {
        if (_overlayPositionUpdateQueued)
            return;

        _overlayPositionUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _overlayPositionUpdateQueued = false;
            UpdateOverlayPosition();
        }, DispatcherPriority.Render);
    }

    private void UpdateOverlayPosition()
    {
        if (_overlayWindow == null || !_isAttached || _rootWindow == null) return;

        try 
        {
            // PointToScreen returns physical pixels, but Window.Position expects
            // device-independent pixels (dips). Divide by RenderScaling to fix
            // HiDPI (125%/150%) and multi-monitor positioning issues.
            var physicalPos = this.PointToScreen(new Point(0, 0));
            var scaling = this.GetVisualRoot()?.RenderScaling ?? 1.0;
            var dipPos = new PixelPoint(
                (int)Math.Round(physicalPos.X / scaling),
                (int)Math.Round(physicalPos.Y / scaling)
            );

            var width = Math.Max(0, Bounds.Width);
            var height = Math.Max(0, Bounds.Height);

            if (width <= 0 || height <= 0)
                return;
            
            if (_overlayWindow.Position != dipPos)
                _overlayWindow.Position = dipPos;
                
            if (Math.Abs(_overlayWindow.Width - width) > 0.5)
                _overlayWindow.Width = width;
                
            if (Math.Abs(_overlayWindow.Height - height) > 0.5)
                _overlayWindow.Height = height;

            _overlayWindow.InvalidateMeasure();
            _overlayWindow.InvalidateArrange();

            if (_overlayWindow.Content is Control content)
            {
                content.Width = width;
                content.Height = height;
                content.InvalidateMeasure();
                content.InvalidateArrange();
            }
        }
        catch (Exception)
        {
            // Ignore layout measurement errors during transitions
        }
    }
}
