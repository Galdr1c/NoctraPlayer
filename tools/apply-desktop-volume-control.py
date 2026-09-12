from pathlib import Path
import re

ROOT = Path('.')


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding='utf-8')


def write(path: str, content: str) -> None:
    (ROOT / path).write_text(content.replace('\r\n', '\n'), encoding='utf-8')

# -----------------------------------------------------------------------------
# Shared transport: keep mute common, add an optional pointer-only volume reveal.
# Mobile keeps the default false capability and therefore remains visually/behaviorally
# unchanged; desktop opts in through PlayerChromeView.
# -----------------------------------------------------------------------------
transport_xaml_path = 'Noctra.UI/Views/Player/PlayerTransportBar.axaml'
transport_xaml = read(transport_xaml_path)

if 'x:Name="Root"' not in transport_xaml:
    transport_xaml = transport_xaml.replace(
        'x:Class="Noctra.UI.Views.Player.PlayerTransportBar"\n             x:DataType="vm:PlayerViewModel"',
        'x:Class="Noctra.UI.Views.Player.PlayerTransportBar"\n             x:Name="Root"\n             x:DataType="vm:PlayerViewModel"',
        1,
    )

old_mute = '''      <Button Grid.Column="3" Classes="playerTransport" Command="{Binding ToggleMuteCommand}"
              AutomationProperties.Name="{Binding MuteAccessibilityName}">
        <Panel Width="24" Height="24">
          <icons:MaterialIcon Kind="VolumeOff" Width="24" Height="24" Foreground="{DynamicResource PlayerOverlayTextBrush}" IsVisible="{Binding IsMuted}" />
          <icons:MaterialIcon Kind="VolumeHigh" Width="24" Height="24" Foreground="{DynamicResource PlayerOverlayTextBrush}"
                              IsVisible="{Binding IsMuted, Converter={StaticResource InverseBoolConverter}}" />
        </Panel>
      </Button>
'''
new_mute = '''      <!-- Mobile keeps the single mute action. Desktop opts into the pointer-volume
           reveal through ShowPointerVolumeControl on PlayerChromeView. -->
      <Grid x:Name="VolumeCluster"
            Grid.Column="3"
            ColumnDefinitions="Auto,Auto"
            PointerEntered="VolumeCluster_PointerEntered"
            PointerExited="VolumeCluster_PointerExited"
            GotFocus="VolumeCluster_GotFocus"
            LostFocus="VolumeCluster_LostFocus"
            PointerWheelChanged="VolumeCluster_PointerWheelChanged">
        <Button x:Name="MuteButton"
                Grid.Column="0"
                Classes="playerTransport"
                Command="{Binding ToggleMuteCommand}"
                AutomationProperties.Name="{Binding MuteAccessibilityName}">
          <Panel Width="24" Height="24">
            <icons:MaterialIcon Kind="VolumeOff" Width="24" Height="24"
                                Foreground="{DynamicResource PlayerOverlayTextBrush}"
                                IsVisible="{Binding IsMuted}" />
            <icons:MaterialIcon Kind="VolumeHigh" Width="24" Height="24"
                                Foreground="{DynamicResource PlayerOverlayTextBrush}"
                                IsVisible="{Binding IsMuted, Converter={StaticResource InverseBoolConverter}}" />
          </Panel>
        </Button>

        <Border x:Name="PointerVolumeReveal"
                Grid.Column="1"
                Width="0"
                Height="40"
                Opacity="0"
                IsHitTestVisible="False"
                ClipToBounds="True"
                VerticalAlignment="Center"
                Margin="2,0,0,0"
                Padding="10,0"
                CornerRadius="20"
                Background="{DynamicResource PlayerIconHoverBrush}">
          <Border.Transitions>
            <Transitions>
              <DoubleTransition Property="Width" Duration="0:0:0.18" Easing="CubicEaseOut" />
              <DoubleTransition Property="Opacity" Duration="0:0:0.14" Easing="CubicEaseOut" />
            </Transitions>
          </Border.Transitions>
          <Slider x:Name="PointerVolumeSlider"
                  Width="112"
                  Minimum="0"
                  Maximum="100"
                  SmallChange="1"
                  LargeChange="5"
                  Value="{Binding Volume, Mode=TwoWay}"
                  VerticalAlignment="Center" />
        </Border>
      </Grid>
'''
if old_mute not in transport_xaml:
    raise RuntimeError('Shared transport mute block did not match current main')
transport_xaml = transport_xaml.replace(old_mute, new_mute, 1)
write(transport_xaml_path, transport_xaml)

transport_cs_path = 'Noctra.UI/Views/Player/PlayerTransportBar.axaml.cs'
transport_cs = '''using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Noctra.ViewModels;

namespace Noctra.UI.Views.Player;

public partial class PlayerTransportBar : UserControl
{
    public static readonly StyledProperty<bool> ShowPointerVolumeControlProperty =
        AvaloniaProperty.Register<PlayerTransportBar, bool>(nameof(ShowPointerVolumeControl));

    private const double ExpandedPointerVolumeWidth = 132d;
    private const int PointerWheelVolumeStep = 5;
    private static readonly TimeSpan PointerVolumeCloseDelay = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _pointerVolumeCloseTimer;

    public PlayerTransportBar()
    {
        InitializeComponent();

        _pointerVolumeCloseTimer = new DispatcherTimer
        {
            Interval = PointerVolumeCloseDelay
        };
        _pointerVolumeCloseTimer.Tick += (_, _) =>
        {
            _pointerVolumeCloseTimer.Stop();
            SetPointerVolumeOpen(false);
        };
    }

    public bool ShowPointerVolumeControl
    {
        get => GetValue(ShowPointerVolumeControlProperty);
        set => SetValue(ShowPointerVolumeControlProperty, value);
    }

    public void FocusPrimaryAction()
        => PlayPauseButton.Focus(NavigationMethod.Directional);

    private void VolumeCluster_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (!ShowPointerVolumeControl)
            return;

        _pointerVolumeCloseTimer.Stop();
        SetPointerVolumeOpen(true);
    }

    private void VolumeCluster_PointerExited(object? sender, PointerEventArgs e)
        => SchedulePointerVolumeClose();

    private void VolumeCluster_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (!ShowPointerVolumeControl)
            return;

        _pointerVolumeCloseTimer.Stop();
        SetPointerVolumeOpen(true);
    }

    private void VolumeCluster_LostFocus(object? sender, RoutedEventArgs e)
        => SchedulePointerVolumeClose();

    private void VolumeCluster_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!ShowPointerVolumeControl || DataContext is not PlayerViewModel vm)
            return;

        var direction = Math.Sign(e.Delta.Y);
        if (direction == 0)
            return;

        vm.Volume = Math.Clamp(vm.Volume + (direction * PointerWheelVolumeStep), 0, 100);
        vm.UserInteractionCommand.Execute(null);

        _pointerVolumeCloseTimer.Stop();
        SetPointerVolumeOpen(true);
        e.Handled = true;
    }

    private void SchedulePointerVolumeClose()
    {
        if (!ShowPointerVolumeControl)
        {
            SetPointerVolumeOpen(false);
            return;
        }

        _pointerVolumeCloseTimer.Stop();
        _pointerVolumeCloseTimer.Start();
    }

    private void SetPointerVolumeOpen(bool isOpen)
    {
        var shouldOpen = isOpen && ShowPointerVolumeControl;
        PointerVolumeReveal.Width = shouldOpen ? ExpandedPointerVolumeWidth : 0d;
        PointerVolumeReveal.Opacity = shouldOpen ? 1d : 0d;
        PointerVolumeReveal.IsHitTestVisible = shouldOpen;
    }
}
'''
write(transport_cs_path, transport_cs)

# -----------------------------------------------------------------------------
# Propagate the optional capability through the shared chrome.
# -----------------------------------------------------------------------------
chrome_xaml_path = 'Noctra.UI/Views/Player/PlayerChromeView.axaml'
chrome_xaml = read(chrome_xaml_path)
old_transport = '<player:PlayerTransportBar x:Name="TransportBar" VerticalAlignment="Bottom" />'
new_transport = '''<player:PlayerTransportBar x:Name="TransportBar"
                               VerticalAlignment="Bottom"
                               ShowPointerVolumeControl="{Binding #Root.ShowPointerVolumeControl}" />'''
if old_transport not in chrome_xaml:
    raise RuntimeError('PlayerChromeView transport host did not match current main')
chrome_xaml = chrome_xaml.replace(old_transport, new_transport, 1)
write(chrome_xaml_path, chrome_xaml)

chrome_cs_path = 'Noctra.UI/Views/Player/PlayerChromeView.axaml.cs'
chrome_cs = read(chrome_cs_path)
needle = '''    public static readonly StyledProperty<bool> ShowPiPActionProperty =
        AvaloniaProperty.Register<PlayerChromeView, bool>(nameof(ShowPiPAction), true);
'''
replacement = needle + '''
    public static readonly StyledProperty<bool> ShowPointerVolumeControlProperty =
        AvaloniaProperty.Register<PlayerChromeView, bool>(nameof(ShowPointerVolumeControl));
'''
if needle not in chrome_cs:
    raise RuntimeError('PlayerChromeView property insertion point not found')
chrome_cs = chrome_cs.replace(needle, replacement, 1)
needle = '''    public bool ShowPiPAction
    {
        get => GetValue(ShowPiPActionProperty);
        set => SetValue(ShowPiPActionProperty, value);
    }
'''
replacement = needle + '''
    public bool ShowPointerVolumeControl
    {
        get => GetValue(ShowPointerVolumeControlProperty);
        set => SetValue(ShowPointerVolumeControlProperty, value);
    }
'''
if needle not in chrome_cs:
    raise RuntimeError('PlayerChromeView CLR property insertion point not found')
chrome_cs = chrome_cs.replace(needle, replacement, 1)
write(chrome_cs_path, chrome_cs)

# -----------------------------------------------------------------------------
# Desktop opts in. Mobile never sets this capability, so its swipe volume path stays
# untouched and the reveal remains collapsed.
# -----------------------------------------------------------------------------
adapter_path = 'Noctra.Avalonia/Views/VideoOverlayView.SharedPresentation.cs'
adapter = read(adapter_path)
old_init = '''        _sharedPlayerChrome = new PlayerChromeView
        {
            ShowLockAction = false,
            ShowPiPAction = true
        };
'''
new_init = '''        _sharedPlayerChrome = new PlayerChromeView
        {
            ShowLockAction = false,
            ShowPiPAction = true,
            ShowPointerVolumeControl = true
        };
'''
if old_init not in adapter:
    raise RuntimeError('Desktop shared chrome initializer did not match current main')
adapter = adapter.replace(old_init, new_init, 1)
write(adapter_path, adapter)

# -----------------------------------------------------------------------------
# Desktop wheel: normal player surface changes volume by 5. Scrollable UI (EPG,
# sheets/lists/sliders) keeps its native scrolling behavior. Existing Up/Down ±2
# keyboard handling and property-driven toast remain unchanged.
# -----------------------------------------------------------------------------
overlay_cs_path = 'Noctra.Avalonia/Views/VideoOverlayView.axaml.cs'
overlay_cs = read(overlay_cs_path)
old_wheel = '''    private void OverlayRoot_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Oynatıcı üzerindeki tekerlek kaydırmasının paylaşılan kontrollere
        // (ör. zaman çizelgesi) ulaşmasını engeller.
        e.Handled = true;
    }
'''
new_wheel = '''    private const int PointerWheelVolumeStep = 5;

    private void OverlayRoot_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_playerViewModel == null)
            return;

        if (ShouldPreserveWheelForScrollableContent(e.Source as Visual))
            return;

        var direction = Math.Sign(e.Delta.Y);
        if (direction == 0)
        {
            e.Handled = true;
            return;
        }

        _playerViewModel.Volume = Math.Clamp(
            _playerViewModel.Volume + (direction * PointerWheelVolumeStep),
            0,
            100);
        _playerViewModel.UserInteractionCommand.Execute(null);
        e.Handled = true;
    }

    private static bool ShouldPreserveWheelForScrollableContent(Visual? source)
    {
        for (var current = source; current != null; current = current.GetVisualParent())
        {
            if (current is ScrollViewer or ListBox or ComboBox or Slider)
                return true;
        }

        return false;
    }
'''
if old_wheel not in overlay_cs:
    raise RuntimeError('Desktop wheel handler did not match current main')
overlay_cs = overlay_cs.replace(old_wheel, new_wheel, 1)
write(overlay_cs_path, overlay_cs)

# -----------------------------------------------------------------------------
# Focused architecture/source contract regression.
# -----------------------------------------------------------------------------
test_path = ROOT / 'Noctra.Tests/DesktopVolumeControlTests.cs'
test_path.write_text('''namespace Noctra.Tests;

public sealed class DesktopVolumeControlTests
{
    [Fact]
    public void SharedTransport_ProvidesOptionalPointerVolumeReveal()
    {
        var xaml = Read("Noctra.UI", "Views", "Player", "PlayerTransportBar.axaml");
        var code = Read("Noctra.UI", "Views", "Player", "PlayerTransportBar.axaml.cs");
        var chrome = Read("Noctra.UI", "Views", "Player", "PlayerChromeView.axaml.cs");

        Assert.Contains("x:Name=\\"VolumeCluster\\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\\"PointerVolumeReveal\\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\\"{Binding Volume, Mode=TwoWay}\\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DoubleTransition Property=\\"Width\\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowPointerVolumeControlProperty", code, StringComparison.Ordinal);
        Assert.Contains("PointerWheelVolumeStep = 5", code, StringComparison.Ordinal);
        Assert.Contains("ShowPointerVolumeControlProperty", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopHost_EnablesPointerVolumeAndPreservesScrollableWheelInput()
    {
        var adapter = Read("Noctra.Avalonia", "Views", "VideoOverlayView.SharedPresentation.cs");
        var overlay = Read("Noctra.Avalonia", "Views", "VideoOverlayView.axaml.cs");

        Assert.Contains("ShowPointerVolumeControl = true", adapter, StringComparison.Ordinal);
        Assert.Contains("PointerWheelVolumeStep = 5", overlay, StringComparison.Ordinal);
        Assert.Contains("ShouldPreserveWheelForScrollableContent", overlay, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer or ListBox or ComboBox or Slider", overlay, StringComparison.Ordinal);
        Assert.Contains("Volume + 2", overlay, StringComparison.Ordinal);
        Assert.Contains("Volume - 2", overlay, StringComparison.Ordinal);
        Assert.Contains("ShowVolumeToast();", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayer_KeepsSwipeVolumeAndDoesNotOptIntoPointerReveal()
    {
        var mobilePlayer = Read("Noctra.Mobile", "Views", "MobilePlayerView.axaml.cs");
        var mobileXaml = Read("Noctra.Mobile", "Views", "MobilePlayerView.axaml");

        Assert.Contains("_swipeStartVolume", mobilePlayer, StringComparison.Ordinal);
        Assert.Contains("vm.Volume = volume", mobilePlayer, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowPointerVolumeControl", mobilePlayer, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowPointerVolumeControl", mobileXaml, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(parts));
}
''', encoding='utf-8')

print('Desktop pointer volume control migration applied.')
