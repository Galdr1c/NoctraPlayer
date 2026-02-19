using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LibVLCSharp.Shared;
using Material.Icons;

using Avalonia.Threading;
using Noctra.Avalonia.Controls;

namespace Noctra.Avalonia.Views;

public partial class PiPWindow : Window
{
    private MediaPlayer? _player;
    public event EventHandler? ReturnRequested;
    public MemoryVideoView VideoSurface => PiPVideoSurface;

    public PiPWindow()
    {
        InitializeComponent();
    }

    private void ControlsOverlay_PointerEntered(object? sender, PointerEventArgs e)
    {
        ControlsOverlay.Opacity = 1;
    }

    private void ControlsOverlay_PointerExited(object? sender, PointerEventArgs e)
    {
        ControlsOverlay.Opacity = 0;
    }

    public void AttachPlayer(MediaPlayer player)
    {
        _player = player;
        PiPVideoSurface.MediaPlayer = _player;
        UpdatePlayPauseIcon();
        
        // Subscribe to events to keep UI in sync
        if (_player != null)
        {
            _player.Playing += OnPlayerStateChanged;
            _player.Paused += OnPlayerStateChanged;
            _player.Stopped += OnPlayerStateChanged;
            
            // Explicit Play kick just in case
            if (_player.IsPlaying) _player.Play();
        }
    }

    public void DetachPlayer()
    {
        if (_player != null)
        {
            _player.Playing -= OnPlayerStateChanged;
            _player.Paused -= OnPlayerStateChanged;
            _player.Stopped -= OnPlayerStateChanged;
        }

        PiPVideoSurface.MediaPlayer = null;
        _player = null;
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdatePlayPauseIcon);
    }
    
    private void UpdatePlayPauseIcon()
    {
        if (_player == null) return;
        PlayPauseIcon.Kind = _player.IsPlaying ? MaterialIconKind.Pause : MaterialIconKind.Play;
    }

    private void DragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void PlayPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_player == null) return;

        if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }
        UpdatePlayPauseIcon();
    }

    private void ReturnToMain_Click(object? sender, RoutedEventArgs e)
    {
        ReturnRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
         Close();
    }
}
