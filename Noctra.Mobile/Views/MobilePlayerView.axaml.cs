using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Mobile.Services;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobilePlayerView : UserControl
{
    /// <summary>
    /// EPG timeline'dan kanal seçildiğinde tetiklenir.
    /// MainView bu event'e abone olup kanalı oynatır.
    /// </summary>
    public event Action<Channel>? ChannelSelected;

    // ── Swipe (kaydırma) jest durumu ───────────────────────────────────────
    // Sağ yarı dikey = ses, sol yarı dikey = parlaklık, yatay = ileri/geri sarma.
    private const double SwipeThreshold = 14;   // yön kararı için minimum hareket (px)
    private bool _isSwiping;
    private bool _swipeDirectionDecided;
    private bool _swipeIsVertical;
    private bool _swipeIsLeftZone;
    private Point _swipeStart;
    private int _swipeStartVolume;
    private double _swipeStartBrightness;

    private IPlayerWindowService? _playerWindowService;
    private IVideoSurfaceService? _videoSurfaceService;
    private PlayerViewModel? _boundVm;
    private Rect _lastSurfaceRect;

    public MobilePlayerView()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged -= OnPlayerPropertyChanged;
        }

        _boundVm = DataContext as PlayerViewModel;

        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged += OnPlayerPropertyChanged;
        }
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.IsEpgPanelOpen))
        {
            return;
        }

        if (_boundVm?.IsEpgPanelOpen == true)
        {
            UpdateEpgVideoLayout();
        }
        else
        {
            // EPG kapandı -> video tekrar tam ekran.
            _lastSurfaceRect = default;
            GetVideoSurfaceService()?.SetBounds(0, 0, 0, 0);
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // EPG açıkken (rotasyon/boyut değişiminde) video slotunu native yüzeyle senkron tut.
        if (_boundVm?.IsEpgPanelOpen == true)
        {
            UpdateEpgVideoLayout();
        }
    }

    /// <summary>
    /// EPG split görünümünde üstteki şeffaf VideoSlot'un ekran (piksel) dikdörtgenini
    /// hesaplar ve native video yüzeyini oraya küçültür. Böylece masaüstündeki
    /// "video üstüne yarı saydam panel" yerine mobilde "video üstte küçülür, EPG altta" olur.
    /// </summary>
    private void UpdateEpgVideoLayout()
    {
        if (VideoSlot is null)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var totalWidth = Bounds.Width;
        var totalHeight = Bounds.Height;
        if (totalWidth <= 0 || totalHeight <= 0)
        {
            return;
        }

        // Video yüksekliği: 16:9, ancak ekranın yarısını geçmesin.
        var desiredHeight = Math.Min(totalWidth * 9.0 / 16.0, totalHeight * 0.5);
        if (Math.Abs(VideoSlot.Height - desiredHeight) > 0.5)
        {
            VideoSlot.Height = desiredHeight;
            return; // yükseklik değişti; yeni layout pass UpdateEpgVideoLayout'u tekrar tetikler
        }

        // VideoSlot'un pencereye göre konumunu al, piksel ölçeğine çevir.
        var topLeft = VideoSlot.TranslatePoint(new Point(0, 0), topLevel);
        if (topLeft is null)
        {
            return;
        }

        var scaling = topLevel.RenderScaling;
        var px = (int)Math.Round(topLeft.Value.X * scaling);
        var py = (int)Math.Round(topLeft.Value.Y * scaling);
        var pw = (int)Math.Round(VideoSlot.Bounds.Width * scaling);
        var ph = (int)Math.Round(VideoSlot.Bounds.Height * scaling);
        if (pw <= 0 || ph <= 0)
        {
            return;
        }

        var rect = new Rect(px, py, pw, ph);
        if (rect == _lastSurfaceRect)
        {
            return;
        }

        _lastSurfaceRect = rect;
        GetVideoSurfaceService()?.SetBounds(px, py, pw, ph);
    }

    private IVideoSurfaceService? GetVideoSurfaceService()
    {
        if (_videoSurfaceService is not null)
        {
            return _videoSurfaceService;
        }

        if (Application.Current is App { Services: not null } app)
        {
            _videoSurfaceService = app.Services
                .GetService<MobilePlatformServiceResolver>()?
                .GetVideoSurfaceService();
        }

        return _videoSurfaceService;
    }

    /// <summary>
    /// EPG timeline kanal satırına tıklandığında çağrılır.
    /// Seçilen kanalı ChannelSelected event'i ile iletir, EPG panelini kapatır.
    /// </summary>
    private void EpgRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
            return;

        if (sender is Control control && control.DataContext is EpgPanelRow row)
        {
            // EPG panelini kapat
            if (DataContext is PlayerViewModel playerVm)
            {
                playerVm.ToggleEpgPanelCommand.Execute(null);
            }

            // Kanal seçim event'ini fırlat
            ChannelSelected?.Invoke(row.Channel);
            e.Handled = true;
        }
    }

    private void OnLeftDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PlayerViewModel playerVm && playerVm.SkipBackwardCommand.CanExecute("10"))
        {
            playerVm.SkipBackwardCommand.Execute("10");
        }
        e.Handled = true;
    }

    private void OnRightDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PlayerViewModel playerVm && playerVm.SkipForwardCommand.CanExecute("10"))
        {
            playerVm.SkipForwardCommand.Execute("10");
        }
        e.Handled = true;
    }

    /// <summary>
    /// Video yüzeyine tek dokunuş: kontrol katmanını (bottom sheet) aç/kapat.
    /// </summary>
    private void OnPlayerBackgroundTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PlayerViewModel playerVm)
        {
            playerVm.ToggleControlsCommand.Execute(null);
        }
    }

    // ── Swipe jestleri ──────────────────────────────────────────────────────

    private void OnZonePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        // Kilitliyken jestler devre dışı.
        if (vm.IsLocked)
            return;

        _isSwiping = true;
        _swipeDirectionDecided = false;
        _swipeIsVertical = false;
        _swipeStart = e.GetCurrentPoint(this).Position;
        _swipeIsLeftZone = ReferenceEquals(sender, LeftZone);
        _swipeStartVolume = vm.Volume;
        _swipeStartBrightness = GetPlayerWindowService()?.GetBrightness() ?? 0.5;
    }

    private void OnZonePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSwiping || DataContext is not PlayerViewModel vm)
            return;

        var pos = e.GetCurrentPoint(this).Position;
        var dx = pos.X - _swipeStart.X;
        var dy = pos.Y - _swipeStart.Y;

        if (!_swipeDirectionDecided)
        {
            if (Math.Abs(dx) < SwipeThreshold && Math.Abs(dy) < SwipeThreshold)
                return;
            _swipeIsVertical = Math.Abs(dy) >= Math.Abs(dx);
            _swipeDirectionDecided = true;
        }

        if (!_swipeIsVertical)
            return; // yatay sarma release anında uygulanır

        var height = Bounds.Height > 1 ? Bounds.Height : 1;
        var fraction = -dy / height; // yukarı kaydırma = artış

        if (_swipeIsLeftZone)
        {
            var brightness = Math.Clamp(_swipeStartBrightness + fraction, 0.0, 1.0);
            GetPlayerWindowService()?.SetBrightness(brightness);
        }
        else
        {
            var volume = (int)Math.Clamp(_swipeStartVolume + (fraction * 100), 0, 100);
            vm.Volume = volume;
        }

        e.Handled = true;
    }

    private void OnZonePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isSwiping || DataContext is not PlayerViewModel vm)
        {
            _isSwiping = false;
            return;
        }

        var pos = e.GetCurrentPoint(this).Position;
        var dx = pos.X - _swipeStart.X;

        // Yatay kaydırma -> ileri/geri sarma (live içerikte komut zaten no-op).
        if (_swipeDirectionDecided && !_swipeIsVertical && Math.Abs(dx) >= SwipeThreshold)
        {
            var seconds = (int)Math.Clamp(Math.Abs(dx) / 6.0, 5, 90);
            var param = seconds.ToString(CultureInfo.InvariantCulture);

            if (dx > 0)
            {
                if (vm.SkipForwardCommand.CanExecute(param))
                    vm.SkipForwardCommand.Execute(param);
            }
            else if (vm.SkipBackwardCommand.CanExecute(param))
            {
                vm.SkipBackwardCommand.Execute(param);
            }

            e.Handled = true;
        }

        _isSwiping = false;
        _swipeDirectionDecided = false;
    }

    private IPlayerWindowService? GetPlayerWindowService()
    {
        if (_playerWindowService is not null)
            return _playerWindowService;

        if (Application.Current is App { Services: not null } app)
        {
            _playerWindowService = app.Services
                .GetService<MobilePlatformServiceResolver>()?
                .GetPlayerWindowService();
        }

        return _playerWindowService;
    }
}
