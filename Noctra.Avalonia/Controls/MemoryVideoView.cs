using System;
using System.Diagnostics;
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

    // Win32 interop for reliable foreground window detection
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private MediaPlayer? _mediaPlayer;
    private IPlatformHandle? _platformHandle;
    
    // Floating overlay window to solve HWND airspace issue
    private Window? _overlayWindow;
    private Window? _rootWindow;
    private bool _isAttached;
    private bool _overlayPositionUpdateQueued;
    private DispatcherTimer? _focusCheckTimer;
    private readonly uint _currentProcessId = (uint)Environment.ProcessId;
    private OverlayFocusController _focusController = null!;

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
        _focusController = new OverlayFocusController(
            isOurProcessActive:        IsOurProcessActive,
            isEffectivelyVisible:      () => this.IsEffectivelyVisible,
            isOverlayCurrentlyVisible: () => _overlayWindow?.IsVisible == true,
            showOverlay: () =>
            {
                if (_overlayWindow == null) CreateOverlayWindow();
                if (_overlayWindow != null)
                {
                    QueueOverlayPositionUpdate();
                    _overlayWindow.Show();
                }
            },
            hideOverlay: () => _overlayWindow?.Hide(),
            setTopmost: v =>
            {
                if (_overlayWindow != null && _overlayWindow.Topmost != v)
                    _overlayWindow.Topmost = v;
            }
        );

        LayoutUpdated += OnLayoutUpdated;
        // Foreground window polling — reliable focus detection on Windows
        _focusCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _focusCheckTimer.Tick += FocusCheckTimer_Tick;
        _focusCheckTimer.Start();

        if (_rootWindow != null)
        {
            _rootWindow.PositionChanged += Root_PositionChanged;
            _rootWindow.SizeChanged += Root_SizeChanged;
            _rootWindow.PropertyChanged += Root_PropertyChanged;
        }

        // Initial update
        QueueOverlayPositionUpdate();
        OnLayoutUpdated(this, EventArgs.Empty);
    }

    private bool IsOurProcessActive()
    {
        // Win32 foreground window detection (GetForegroundWindow) is Windows-only.
        // On Linux/macOS there is no native HWND airspace issue, so we always
        // report active to keep the overlay fully functional on those platforms.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;

        try
        {
            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out var fgPid);
            return fgPid == _currentProcessId;
        }
        catch { return false; }
    }

    private void DestroyOverlay()
    {
        LayoutUpdated -= OnLayoutUpdated;

        if (_focusCheckTimer != null)
        {
            _focusCheckTimer.Stop();
            _focusCheckTimer.Tick -= FocusCheckTimer_Tick;
            _focusCheckTimer = null;
        }

        if (_rootWindow != null)
        {
            _rootWindow.PositionChanged -= Root_PositionChanged;
            _rootWindow.SizeChanged -= Root_SizeChanged;
            _rootWindow.PropertyChanged -= Root_PropertyChanged;
        }

        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;
        }
    }

    private void Root_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsVisibleProperty)
        {
            _focusController?.OnLayoutChanged(this.IsEffectivelyVisible);
        }

        if (e.Property.Name is "WindowState" or "ClientSize")
        {
            QueueOverlayPositionUpdate();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        _focusController?.OnLayoutChanged(this.IsEffectivelyVisible);
        QueueOverlayPositionUpdate();

        // CPU/battery optimization: only poll focus while the video view is visible.
        // When the user navigates to another tab, stop the timer to avoid unnecessary 200ms polling.
        if (_focusCheckTimer != null)
        {
            bool shouldRun = this.IsEffectivelyVisible;
            if (shouldRun && !_focusCheckTimer.IsEnabled)
                _focusCheckTimer.Start();
            else if (!shouldRun && _focusCheckTimer.IsEnabled)
                _focusCheckTimer.Stop();
        }
    }

    private void FocusCheckTimer_Tick(object? sender, EventArgs e)
    {
        _focusController?.OnTimerTick();
    }


    private void Root_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        HandleWindowMovement();
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
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            CanResize = false,
            Title = "", // Empty title to help hide from Alt-Tab
            SizeToContent = SizeToContent.Manual,
            Topmost = false,
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
