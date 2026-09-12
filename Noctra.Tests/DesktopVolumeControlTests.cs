namespace Noctra.Tests;

public sealed class DesktopVolumeControlTests
{
    [Fact]
    public void SharedTransport_ProvidesOptionalPointerVolumeReveal()
    {
        var xaml = Read("Noctra.UI", "Views", "Player", "PlayerTransportBar.axaml");
        var code = Read("Noctra.UI", "Views", "Player", "PlayerTransportBar.axaml.cs");
        var chrome = Read("Noctra.UI", "Views", "Player", "PlayerChromeView.axaml.cs");

        Assert.Contains("x:Name=\"VolumeCluster\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PointerVolumeReveal\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding Volume, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DoubleTransition Property=\"Width\"", xaml, StringComparison.Ordinal);
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
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
