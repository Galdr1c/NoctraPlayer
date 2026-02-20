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

    private MediaPlayer? _mediaPlayer;
    private IPlatformHandle? _platformHandle;
    
    // Floating overlay window to solve HWND airspace issue
    private Window? _overlayWindow;
    private Window? _rootWindow;
    private bool _isAttached;
    private bool _isRootActive = true;
    private DispatcherTimer? _debounceTimer;

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
        LayoutUpdated += OnLayoutUpdated;
        
        // Initialize debounce timer for resize/move operations
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounceTimer.Tick += DebounceTimer_Tick;

        if (_rootWindow != null)
        {
            _rootWindow.PositionChanged += Root_PositionChanged;
            _rootWindow.SizeChanged += Root_SizeChanged;
            _rootWindow.Activated += Root_Activated;
            _rootWindow.Deactivated += Root_Deactivated;
            _rootWindow.PropertyChanged += Root_PropertyChanged;
        }

        // Initial update
        OnLayoutUpdated(this, EventArgs.Empty);
    }

    private void DestroyOverlay()
    {
        LayoutUpdated -= OnLayoutUpdated;

        if (_debounceTimer != null)
        {
            _debounceTimer.Stop();
            _debounceTimer.Tick -= DebounceTimer_Tick;
            _debounceTimer = null;
        }

        if (_rootWindow != null)
        {
            _rootWindow.PositionChanged -= Root_PositionChanged;
            _rootWindow.SizeChanged -= Root_SizeChanged;
            _rootWindow.Activated -= Root_Activated;
            _rootWindow.Deactivated -= Root_Deactivated;
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
            UpdateOverlayState(this.IsEffectivelyVisible);
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // Don't update if we are currently debouncing (resizing/moving)
        if (_debounceTimer != null && _debounceTimer.IsEnabled) return;

        // Check visibility and update overlay
        UpdateOverlayState(this.IsEffectivelyVisible);
    }

    private void Root_Activated(object? sender, EventArgs e)
    {
        _isRootActive = true;
        if (_overlayWindow != null && this.IsEffectivelyVisible)
        {
            // Ana pencerenin Topmost durumuna göre senkronize et
            _overlayWindow.Topmost = _rootWindow?.Topmost ?? true;
            if (!_overlayWindow.IsVisible) _overlayWindow.Show();
        }
    }

    private void Root_Deactivated(object? sender, EventArgs e)
    {
        _isRootActive = false;
        
        // Değişiklik: Eğer ana pencere (MainWindow) PiP modundayken Topmost ise, 
        // Overlay penceresi de Topmost kalmaya devam ETMELİDİR. 
        // Aksi takdirde inaktifken ilk tıklama boşa gider.
        if (_overlayWindow != null && _rootWindow != null && !_rootWindow.Topmost)
        {
            _overlayWindow.Topmost = false; 
        }
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
        
        // Değişiklik: _overlayWindow.Hide() KODUNU KALDIRDIK.
        // Pencere boyutlanırken overlay'i gizlemek yerine pozisyonunu senkronize olarak güncelliyoruz.
        UpdateOverlayPosition();
            
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        
        // İsteğe bağlı ekstra güvenlik: Hareket bittiğinde görünürlüğü kesinleştir.
        if (this.IsEffectivelyVisible && _overlayWindow != null && !_overlayWindow.IsVisible)
        {
             UpdateOverlayState(true);
        }
    }

    private void UpdateOverlayState(bool visible)
    {
        if (visible)
        {
            if (_overlayWindow == null)
            {
                CreateOverlayWindow();
            }
            
            if (_overlayWindow != null)
            {
                UpdateOverlayPosition();
                _overlayWindow.Show();
            }
        }
        else
        {
            _overlayWindow?.Hide();
        }
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
            Title = "VideoOverlay",
            SizeToContent = SizeToContent.Manual,
            Topmost = true, 
            Focusable = false, 
            Content = OverlayContent
        };

        _overlayWindow.Show(_rootWindow);
    }

    private void UpdateOverlayPosition()
    {
        if (_overlayWindow == null || !_isAttached || _rootWindow == null) return;

        try 
        {
            var topLeft = this.PointToScreen(new Point(0, 0));
            
            if (_overlayWindow.Position != topLeft)
                _overlayWindow.Position = topLeft;
                
            if (_overlayWindow.Width != Bounds.Width)
                _overlayWindow.Width = Bounds.Width;
                
            if (_overlayWindow.Height != Bounds.Height)
                _overlayWindow.Height = Bounds.Height;

             if (_isRootActive && !_overlayWindow.Topmost)
             {
                 _overlayWindow.Topmost = true;
             }
        }
        catch (Exception)
        {
            // Ignore layout measurement errors during transitions
        }
    }
}
