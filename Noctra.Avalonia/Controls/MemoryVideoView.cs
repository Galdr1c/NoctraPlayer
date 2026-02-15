using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace Noctra.Avalonia.Controls;

public class MemoryVideoView : Control
{
    public static readonly StyledProperty<MediaPlayer?> MediaPlayerProperty =
        AvaloniaProperty.Register<MemoryVideoView, MediaPlayer?>(nameof(MediaPlayer));

    private static readonly byte[] Rv32 = [0x52, 0x56, 0x33, 0x32];

    private readonly object _sync = new();
    private readonly MediaPlayer.LibVLCVideoFormatCb _formatCallback;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanupCallback;
    private readonly MediaPlayer.LibVLCVideoLockCb _lockCallback;
    private readonly MediaPlayer.LibVLCVideoUnlockCb _unlockCallback;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCallback;

    private int _frameUpdateScheduled;
    private WriteableBitmap? _bitmap;
    private MediaPlayer? _mediaPlayer;
    private IntPtr _videoBuffer;
    private int _width;
    private int _height;

    public MemoryVideoView()
    {
        // Keep delegate references alive for native callbacks.
        _formatCallback = VideoFormat;
        _cleanupCallback = CleanupVideo;
        _lockCallback = LockVideo;
        _unlockCallback = UnlockVideo;
        _displayCallback = DisplayVideo;
    }

    public MediaPlayer? MediaPlayer
    {
        get => GetValue(MediaPlayerProperty);
        set => SetValue(MediaPlayerProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MediaPlayerProperty)
        {
            DetachPlayer();
            AttachPlayer(change.NewValue as MediaPlayer);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DetachPlayer();
    }

    private void AttachPlayer(MediaPlayer? player)
    {
        if (player == null) return;

        _mediaPlayer = player;
        _mediaPlayer.SetVideoFormatCallbacks(_formatCallback, _cleanupCallback);
        _mediaPlayer.SetVideoCallbacks(_lockCallback, _unlockCallback, _displayCallback);
    }

    private void DetachPlayer()
    {
        _mediaPlayer?.SetVideoFormatCallbacks(
            (MediaPlayer.LibVLCVideoFormatCb)null!,
            (MediaPlayer.LibVLCVideoCleanupCb)null!);
        _mediaPlayer?.SetVideoCallbacks(
            (MediaPlayer.LibVLCVideoLockCb)null!,
            (MediaPlayer.LibVLCVideoUnlockCb)null!,
            (MediaPlayer.LibVLCVideoDisplayCb)null!);

        Interlocked.Exchange(ref _frameUpdateScheduled, 0);
        ReleaseBuffer();
        _bitmap = null;
        _mediaPlayer = null;
    }

    private uint VideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        try
        {
            var requestedWidth = (int)width;
            var requestedHeight = (int)height;
            if (requestedWidth <= 0 || requestedHeight <= 0)
            {
                return 0;
            }

            // "RV32" = BGRA 32bpp
            Marshal.Copy(Rv32, 0, chroma, 4);

            var pitch = (uint)(requestedWidth * 4);
            pitches = pitch;
            lines = (uint)requestedHeight;

            lock (_sync)
            {
                if (_videoBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_videoBuffer);
                }

                _width = requestedWidth;
                _height = requestedHeight;
                _videoBuffer = Marshal.AllocHGlobal((int)(pitch * requestedHeight));
            }

            Dispatcher.UIThread.Post(() =>
            {
                _bitmap = new WriteableBitmap(
                    new PixelSize(requestedWidth, requestedHeight),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);
                InvalidateVisual();
            }, DispatcherPriority.Render);

            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private void CleanupVideo(ref IntPtr opaque)
    {
        ReleaseBuffer();
    }

    private IntPtr LockVideo(IntPtr opaque, IntPtr planes)
    {
        var buffer = _videoBuffer;
        if (buffer == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        Marshal.WriteIntPtr(planes, buffer);
        return buffer;
    }

    private void UnlockVideo(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        // no-op
    }

    private void DisplayVideo(IntPtr opaque, IntPtr picture)
    {
        if (Interlocked.Exchange(ref _frameUpdateScheduled, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                lock (_sync)
                {
                    if (_bitmap == null || _videoBuffer == IntPtr.Zero || _width <= 0 || _height <= 0)
                    {
                        return;
                    }

                    using var fb = _bitmap.Lock();
                    unsafe
                    {
                        var dest = fb.Address.ToPointer();
                        var src = _videoBuffer.ToPointer();
                        var destBytes = (long)fb.RowBytes * fb.Size.Height;
                        var srcBytes = (long)_width * _height * 4;
                        var bytes = Math.Min(destBytes, srcBytes);
                        Buffer.MemoryCopy(src, dest, destBytes, bytes);
                    }
                }

                InvalidateVisual();
            }
            catch
            {
                // Keep player alive even if one frame copy fails.
            }
            finally
            {
                Interlocked.Exchange(ref _frameUpdateScheduled, 0);
            }
        }, DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_bitmap != null)
        {
            context.DrawImage(_bitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }
    }

    private void ReleaseBuffer()
    {
        lock (_sync)
        {
            if (_videoBuffer == IntPtr.Zero)
            {
                return;
            }

            Marshal.FreeHGlobal(_videoBuffer);
            _videoBuffer = IntPtr.Zero;
        }
    }
}
