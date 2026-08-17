using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Mobile.Localization;
using Noctra.Mobile.Services;
using Noctra.Mobile.ViewModels;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobilePlayerEpgPanel : UserControl
{
    public static readonly StyledProperty<MobileEpgGuidePresentation?> GuideProperty =
        AvaloniaProperty.Register<MobilePlayerEpgPanel, MobileEpgGuidePresentation?>(nameof(Guide));

    private readonly DispatcherTimer _liveTimer;
    private readonly DispatcherTimer _resizeTimer;
    private PlayerViewModel? _player;
    private ScrollViewer? _timelineVerticalScroll;
    private ScrollViewer? _channelVerticalScroll;
    private CancellationTokenSource? _openCts;
    private IInputElement? _focusBeforeOpen;
    private bool _synchronizingVerticalScroll;
    private bool _isRemoteMode;
    private bool _isOpen;
    private bool _isLocalizationSubscribed;
    private MobileInputModeService? _inputModeService;

    public MobilePlayerEpgPanel()
    {
        InitializeComponent();

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _liveTimer.Tick += LiveTimer_Tick;
        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _resizeTimer.Tick += ResizeTimer_Tick;

        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public event Action<Channel>? ChannelSelected;

    public MobileEpgGuidePresentation? Guide
    {
        get => GetValue(GuideProperty);
        private set => SetValue(GuideProperty, value);
    }

    public Control? VideoSlotControl => VideoSlot;

    /// <summary>
    /// Re-runs the responsive guide layout after the host window changes size.
    /// The Android TextureView is positioned from <see cref="VideoSlot"/>;
    /// invalidating both the panel and its hero grid prevents a stale
    /// landscape-sized slot from surviving a return to portrait orientation.
    /// </summary>
    public void RefreshLayoutForSurface()
    {
        ApplyAdaptiveLayout();
        InvalidateMeasure();
        InvalidateArrange();
        EpgModeRoot.InvalidateMeasure();
        EpgModeRoot.InvalidateArrange();
        EpgHeroGrid.InvalidateMeasure();
        EpgHeroGrid.InvalidateArrange();
    }

    public async Task OpenAsync()
    {
        if (_player is null && DataContext is PlayerViewModel player)
        {
            BindPlayer(player);
        }

        if (_player is null)
        {
            return;
        }

        _isOpen = true;
        CapturePreviousFocus();
        SetRemoteMode(_inputModeService?.IsRemote == true);
        ApplyAdaptiveLayout();

        var openCts = new CancellationTokenSource();
        var openToken = openCts.Token;
        var previousCts = Interlocked.Exchange(ref _openCts, openCts);
        previousCts?.Cancel();

        var now = DateTime.Now;
        var timelineWidth = Math.Max(320, Bounds.Width - (Guide?.ChannelRailWidth ?? 132) - 32);
        var window = Guide!.ConfigureWindow(now, timelineWidth, _isRemoteMode);

        try
        {
            await _player.LoadEpgPanelAsync(window.Start, window.End);
            openToken.ThrowIfCancellationRequested();
            Guide.Rebuild(DateTime.Now);
            ApplyAdaptiveLayout();
            WireVerticalScrollers();
            _liveTimer.Start();
            QueueInitialFocusAndScroll();
        }
        catch (OperationCanceledException) when (openToken.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.CompareExchange(ref _openCts, null, openCts);
            openCts.Dispose();
        }
    }

    public void CloseGuide()
    {
        _isOpen = false;
        _liveTimer.Stop();
        _resizeTimer.Stop();
        var openCts = Interlocked.Exchange(ref _openCts, null);
        openCts?.Cancel();
        UnwireVerticalScrollers();

        if (_focusBeforeOpen is Control previousControl && previousControl.IsEffectivelyVisible)
        {
            Dispatcher.UIThread.Post(() => previousControl.Focus(), DispatcherPriority.Input);
        }

        _focusBeforeOpen = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        BindPlayer(DataContext as PlayerViewModel);
    }

    private void BindPlayer(PlayerViewModel? player)
    {
        if (ReferenceEquals(_player, player))
        {
            return;
        }

        if (_player is not null)
        {
            _player.PropertyChanged -= Player_PropertyChanged;
        }

        _player = player;
        if (_player is null)
        {
            Guide = null;
            return;
        }

        _player.PropertyChanged += Player_PropertyChanged;
        Guide = new MobileEpgGuidePresentation(
            _player,
            key => LocalizationSource.Instance[key]);
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Guide is null)
        {
            return;
        }

        if (e.PropertyName == nameof(PlayerViewModel.CurrentChannel))
        {
            Guide.RefreshPlayingChannel(DateTime.Now);
            return;
        }

        if (e.PropertyName == nameof(PlayerViewModel.EpgGuideState)
            && _isOpen
            && _openCts is null
            && _player?.EpgGuideState == EpgGuideLoadState.Ready
            && _player?.HasEpgRows == true)
        {
            Guide.Rebuild(DateTime.Now);
            ApplyAdaptiveLayout();
            WireVerticalScrollers();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        BindPlayer(DataContext as PlayerViewModel);
        AttachInputModeService();
        if (!_isLocalizationSubscribed)
        {
            LocalizationSource.Instance.PropertyChanged += LocalizationSource_PropertyChanged;
            _isLocalizationSubscribed = true;
        }

        WireVerticalScrollers();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        CloseGuide();
        BindPlayer(null);
        DetachInputModeService();
        if (_isLocalizationSubscribed)
        {
            LocalizationSource.Instance.PropertyChanged -= LocalizationSource_PropertyChanged;
            _isLocalizationSubscribed = false;
        }
    }

    private void AttachInputModeService()
    {
        if (_inputModeService is null && Application.Current is App { Services: not null } app)
        {
            _inputModeService = app.Services.GetService<MobileInputModeService>();
        }

        if (_inputModeService is null)
        {
            return;
        }

        _inputModeService.ModeChanged -= InputModeService_ModeChanged;
        _inputModeService.ModeChanged += InputModeService_ModeChanged;
        SetRemoteMode(_inputModeService.IsRemote);
    }

    private void DetachInputModeService()
    {
        if (_inputModeService is not null)
        {
            _inputModeService.ModeChanged -= InputModeService_ModeChanged;
        }
    }

    private void InputModeService_ModeChanged(object? sender, MobileInputMode mode)
        => SetRemoteMode(mode == MobileInputMode.Remote);

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyAdaptiveLayout();
        if (!_isOpen || Guide is null)
        {
            return;
        }

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ResizeTimer_Tick(object? sender, EventArgs e)
    {
        _resizeTimer.Stop();
        if (!_isOpen || Guide is null)
        {
            return;
        }

        var timelineWidth = Math.Max(320, Bounds.Width - Guide.ChannelRailWidth - 32);
        Guide.ConfigureScale(DateTime.Now, timelineWidth, _isRemoteMode);
        Guide.Rebuild(DateTime.Now);
        QueueScrollToNow();
    }

    private void ApplyAdaptiveLayout()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var wide = Bounds.Width >= 900 || Bounds.Width > Bounds.Height * 1.35;
        Classes.Set("wide", wide);
        Classes.Set("compact", !wide);
        Classes.Set("remote", _isRemoteMode);

        if (Guide is not null && GuideGrid.ColumnDefinitions.Count > 0)
        {
            GuideGrid.ColumnDefinitions[0].Width = new GridLength(Guide.ChannelRailWidth);
        }

        if (wide)
        {
            EpgHeroGrid.ColumnDefinitions = new ColumnDefinitions("2*,3*");
            EpgHeroGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(VideoSlot, 0);
            Grid.SetRow(VideoSlot, 0);
            Grid.SetColumn(ProgramDetailPanel, 1);
            Grid.SetRow(ProgramDetailPanel, 0);

            var minimumHeroHeight = Bounds.Height < 500 ? 136 : 190;
            // Yatay modda video kolonu panel genişliğinin 2/5'i kadardır;
            // yüksekliği 16:9'a göre hesaplayarak slot'un kısa kalmasını ve
            // videonun iki yanında pillarbox (siyah bant) oluşmasını engelle.
            // Üst sınır rehber zaman çizelgesine yer bırakacak şekilde sınırlanır.
            var videoColumnWidth = Bounds.Width * 2.0 / 5.0;
            var heroHeight = Math.Clamp(
                videoColumnWidth * 9.0 / 16.0,
                minimumHeroHeight,
                Math.Max(minimumHeroHeight, Bounds.Height * 0.5));
            VideoSlot.Height = heroHeight;
            ProgramDetailPanel.Height = heroHeight;
        }
        else
        {
            EpgHeroGrid.ColumnDefinitions = new ColumnDefinitions("*");
            EpgHeroGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(VideoSlot, 0);
            Grid.SetRow(VideoSlot, 0);
            Grid.SetColumn(ProgramDetailPanel, 0);
            Grid.SetRow(ProgramDetailPanel, 1);

            VideoSlot.Height = Math.Min(Bounds.Width * 9.0 / 16.0, Bounds.Height * 0.36);
            ProgramDetailPanel.Height = double.NaN;
        }

        WatchSelectedText.IsVisible = wide || Bounds.Width >= 600;
    }

    private void SetRemoteMode(bool remoteMode)
    {
        if (_isRemoteMode == remoteMode && Guide is not null)
        {
            return;
        }

        _isRemoteMode = remoteMode;
        Classes.Set("remote", remoteMode);
        if (Guide is null || !_isOpen)
        {
            return;
        }

        var railWidth = remoteMode ? 184d : 132d;
        var timelineWidth = Math.Max(320, Bounds.Width - railWidth - 32);
        Guide.ConfigureScale(DateTime.Now, timelineWidth, remoteMode);
        Guide.Rebuild(DateTime.Now);
        ApplyAdaptiveLayout();
    }

    private void CapturePreviousFocus()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        _focusBeforeOpen = topLevel?.FocusManager?.GetFocusedElement();
    }

    private void LiveTimer_Tick(object? sender, EventArgs e)
    {
        if (_isOpen)
        {
            Guide?.UpdateLive(DateTime.Now);
        }
    }

    private void LocalizationSource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isOpen)
        {
            Guide?.UpdateLive(DateTime.Now);
        }
    }

    private void EpgPanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_inputModeService is not null)
        {
            _inputModeService.SetMode(MobileInputMode.Touch);
        }
        else
        {
            SetRemoteMode(false);
        }
    }

    private void EpgPanel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.None)
        {
            if (_inputModeService is not null)
            {
                _inputModeService.SetMode(MobileInputMode.Remote);
            }
            else
            {
                SetRemoteMode(true);
            }
        }

        if (e.Key is Key.Escape or Key.Back)
        {
            ClosePanel();
            e.Handled = true;
            return;
        }

        if (e.Source is Button { Tag: MobileEpgProgramItem program })
        {
            HandleProgramKey(Guide?.SelectedProgram ?? program, e);
            return;
        }

        if (e.Source is Button { Tag: MobileEpgRow row })
        {
            var activeRow = Guide?.Rows.FirstOrDefault(candidate => candidate.Channel.Id == row.Channel.Id) ?? row;
            HandleChannelKey(activeRow, e);
        }
    }

    private void HandleProgramKey(MobileEpgProgramItem program, KeyEventArgs e)
    {
        if (Guide is null)
        {
            return;
        }

        MobileEpgProgramItem? target = e.Key switch
        {
            Key.Left => Guide.FindHorizontal(program, -1),
            Key.Right => Guide.FindHorizontal(program, 1),
            Key.Up => Guide.FindVertical(program, -1),
            Key.Down => Guide.FindVertical(program, 1),
            _ => null
        };

        if (target is not null)
        {
            Guide.Select(target);
            FocusProgram(target);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Space)
        {
            ActivateChannel(program.Channel);
            e.Handled = true;
        }
    }

    private void HandleChannelKey(MobileEpgRow row, KeyEventArgs e)
    {
        if (Guide is null)
        {
            return;
        }

        if (e.Key == Key.Right)
        {
            var program = Guide.SelectChannel(row, DateTime.Now);
            if (program is not null)
            {
                FocusProgram(program);
            }
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Space)
        {
            ActivateChannel(row.Channel);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        var index = Guide.Rows.IndexOf(row) + (e.Key == Key.Up ? -1 : 1);
        if (index < 0 || index >= Guide.Rows.Count)
        {
            return;
        }

        var targetRow = Guide.Rows[index];
        EpgChannelList.ScrollIntoView(targetRow);
        Dispatcher.UIThread.Post(
            () => FindTaggedButton(targetRow)?.Focus(),
            DispatcherPriority.Input);
        e.Handled = true;
    }

    private void ProgramButton_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MobileEpgProgramItem item })
        {
            Guide?.Select(item);
            ScrollProgramIntoView(item);
        }
    }

    private void ProgramButton_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Button button
            || button.Tag is not MobileEpgProgramItem item
            || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        var timelineX = item.Left + e.GetCurrentPoint(button).Position.X;
        Guide?.Select(FindProgramAtTimelinePosition(item, timelineX) ?? item);
        e.Handled = true;
    }

    private MobileEpgProgramItem? FindProgramAtTimelinePosition(
        MobileEpgProgramItem source,
        double timelineX)
    {
        var row = Guide?.Rows.FirstOrDefault(candidate =>
            candidate.Channel.Id == source.Channel.Id);
        if (row is null || row.Programs.Count == 0)
        {
            return null;
        }

        var containing = row.Programs.FirstOrDefault(program =>
            timelineX >= program.Left
            && timelineX < program.Left + program.Width);
        if (containing is not null)
        {
            return containing;
        }

        return row.Programs
            .OrderBy(program => DistanceToRange(timelineX, program.Left, program.Left + program.Width))
            .FirstOrDefault();
    }

    private static double DistanceToRange(double value, double start, double end)
    {
        if (value < start)
        {
            return start - value;
        }

        return value > end ? value - end : 0;
    }

    private void ChannelButton_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MobileEpgRow row })
        {
            Guide?.SelectChannel(row, DateTime.Now);
        }
    }

    private void ChannelButton_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Button { Tag: MobileEpgRow row }
            || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        Guide?.SelectChannel(row, DateTime.Now);
        e.Handled = true;
    }

    private void WatchSelectedButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Guide?.SelectedChannel is { } channel)
        {
            ActivateChannel(channel);
        }
    }

    private async void NowButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Guide is null || _player is null)
        {
            return;
        }

        var now = DateTime.Now;
        if (now < Guide.WindowStart || now > Guide.WindowEnd)
        {
            var timelineWidth = Math.Max(320, Bounds.Width - Guide.ChannelRailWidth - 32);
            var window = Guide.ConfigureWindow(now, timelineWidth, _isRemoteMode);
            await _player.LoadEpgPanelAsync(window.Start, window.End);
            Guide.Rebuild(now);
        }

        var selection = Guide.FindInitialSelection(now);
        Guide.Select(selection);
        QueueScrollToNow();
        if (_isRemoteMode && selection is not null)
        {
            FocusProgram(selection);
        }
    }

    private void ActivateChannel(Channel channel)
    {
        ClosePanel();
        ChannelSelected?.Invoke(channel);
    }

    private void ClosePanel()
    {
        if (_player?.IsEpgPanelOpen == true)
        {
            _player.ToggleEpgPanelCommand.Execute(null);
        }
    }

    private void QueueInitialFocusAndScroll()
    {
        Dispatcher.UIThread.Post(() =>
        {
            QueueScrollToNow();
            if (Guide?.SelectedProgram is { } selected)
            {
                FocusProgram(selected);
                return;
            }

            var channelRow = Guide?.Rows.FirstOrDefault(row => row.IsPlayingChannel)
                             ?? Guide?.Rows.FirstOrDefault();
            if (channelRow is not null)
            {
                FocusChannel(channelRow);
                return;
            }

            if (RetryGuideButton.IsEffectivelyVisible)
            {
                RetryGuideButton.Focus();
            }
            else
            {
                CloseGuideButton.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private void QueueScrollToNow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Guide is null)
            {
                return;
            }

            var viewport = EpgTimelineHorizontalScroll.Viewport.Width;
            var target = Math.Max(0, Guide.NowLineLeft - viewport * 0.35);
            EpgTimelineHorizontalScroll.Offset = new Vector(target, 0);
            EpgTimeHeaderScroll.Offset = new Vector(target, 0);
        }, DispatcherPriority.Loaded);
    }

    private void FocusProgram(MobileEpgProgramItem item)
    {
        var row = Guide?.Rows.FirstOrDefault(candidate => candidate.Channel.Id == item.Channel.Id);
        if (row is null)
        {
            return;
        }

        EpgTimelineList.ScrollIntoView(row);
        ScrollProgramIntoView(item);
        Dispatcher.UIThread.Post(
            () => FindTaggedButton(item)?.Focus(),
            DispatcherPriority.Input);
    }

    private void FocusChannel(MobileEpgRow row)
    {
        EpgChannelList.ScrollIntoView(row);
        Dispatcher.UIThread.Post(
            () => FindTaggedButton(row)?.Focus(),
            DispatcherPriority.Input);
    }

    private void ScrollProgramIntoView(MobileEpgProgramItem item)
    {
        var viewport = EpgTimelineHorizontalScroll.Viewport.Width;
        var current = EpgTimelineHorizontalScroll.Offset.X;
        var left = item.Left;
        var right = item.Left + item.Width;
        var target = current;

        if (left < current + 20)
        {
            target = Math.Max(0, left - 20);
        }
        else if (right > current + viewport - 20)
        {
            target = Math.Max(0, right - viewport + 20);
        }

        EpgTimelineHorizontalScroll.Offset = new Vector(target, 0);
    }

    private Button? FindTaggedButton(object tag)
        => this.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => ReferenceEquals(button.Tag, tag));

    private void EpgTimelineHorizontalScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        EpgTimeHeaderScroll.Offset = new Vector(EpgTimelineHorizontalScroll.Offset.X, 0);
        WireVerticalScrollers();
    }

    private void WireVerticalScrollers()
    {
        var timeline = EpgTimelineList
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        var channel = EpgChannelList
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        if (ReferenceEquals(timeline, _timelineVerticalScroll)
            && ReferenceEquals(channel, _channelVerticalScroll))
        {
            return;
        }

        UnwireVerticalScrollers();
        _timelineVerticalScroll = timeline;
        _channelVerticalScroll = channel;
        if (_timelineVerticalScroll is not null)
        {
            _timelineVerticalScroll.ScrollChanged += TimelineVerticalScroll_ScrollChanged;
        }

        if (_channelVerticalScroll is not null)
        {
            _channelVerticalScroll.ScrollChanged += ChannelVerticalScroll_ScrollChanged;
        }
    }

    private void UnwireVerticalScrollers()
    {
        if (_timelineVerticalScroll is not null)
        {
            _timelineVerticalScroll.ScrollChanged -= TimelineVerticalScroll_ScrollChanged;
        }

        if (_channelVerticalScroll is not null)
        {
            _channelVerticalScroll.ScrollChanged -= ChannelVerticalScroll_ScrollChanged;
        }

        _timelineVerticalScroll = null;
        _channelVerticalScroll = null;
    }

    private void TimelineVerticalScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        SynchronizeVerticalOffset(_timelineVerticalScroll, _channelVerticalScroll);
    }

    private void ChannelVerticalScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        SynchronizeVerticalOffset(_channelVerticalScroll, _timelineVerticalScroll);
    }

    private void SynchronizeVerticalOffset(ScrollViewer? source, ScrollViewer? target)
    {
        if (_synchronizingVerticalScroll || source is null || target is null)
        {
            return;
        }

        try
        {
            _synchronizingVerticalScroll = true;
            target.Offset = new Vector(target.Offset.X, source.Offset.Y);
        }
        finally
        {
            _synchronizingVerticalScroll = false;
        }
    }
}
