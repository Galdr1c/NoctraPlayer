namespace Noctra.Tests;

public sealed class MobileEdgeToEdgeLayoutContractTests
{
    [Fact]
    public void MainView_NormalizesSafeAreaBeforeApplyingTopInsetOnEdgeToEdgeHosts()
    {
        var source = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("GetContentSafeArea", source, StringComparison.Ordinal);
        Assert.Contains("var contentSafe = GetContentSafeArea(safe);", source, StringComparison.Ordinal);
        Assert.Contains("_headerBasePadding.Top + contentSafe.Top", source, StringComparison.Ordinal);
        Assert.Contains("ProfilesOverlay.Padding = new Thickness(", source, StringComparison.Ordinal);
        Assert.Contains("contentSafe.Top", source, StringComparison.Ordinal);
        Assert.Contains("new Thickness(safe.Left, top, safe.Right, bottom)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidActivity_DoesNotForceHardCodedShellBackdrop()
    {
        var source = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        Assert.DoesNotContain(
            "content.SetBackgroundColor(Color.Rgb(10, 10, 10));",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_UsesTransparentTopLevelOnlyDuringNativeVideoPlayback()
    {
        var source = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("ApplyTopLevelComposition(PlayerHost.IsVisible);", source, StringComparison.Ordinal);
        Assert.Contains("WindowTransparencyLevel.None", source, StringComparison.Ordinal);
        Assert.Contains("ResolveShellBackgroundBrush", source, StringComparison.Ordinal);
        Assert.Contains("\"Bg0Brush\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("topLevel.Background = Brushes.Black", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidSystemChrome_FollowsThemeAndPlayerState()
    {
        var contract = ReadProjectFile(
            "Noctra.Core", "Services", "Interfaces", "IPlayerWindowService.cs");
        var android = ReadProjectFile(
            "Noctra.Android", "Services", "AndroidPlayerWindowService.cs");

        Assert.Contains("SetSystemBarsTheme(bool isDarkTheme)", contract, StringComparison.Ordinal);
        Assert.Contains("SetSystemBarsTheme(bool isDarkTheme)", android, StringComparison.Ordinal);
        Assert.Contains("SetSystemBarsAppearance", android, StringComparison.Ordinal);
        Assert.Contains("Color.Rgb(250, 250, 250)", android, StringComparison.Ordinal);
        Assert.Contains("Color.Rgb(10, 10, 10)", android, StringComparison.Ordinal);
        Assert.Contains("_isPlayerOverlayActive", android, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeOrientationAndPlayerTransitions_ReapplySystemChrome()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        var android = ReadProjectFile(
            "Noctra.Android", "Services", "AndroidPlayerWindowService.cs");

        Assert.Contains("nameof(ActualThemeVariant)", mainView, StringComparison.Ordinal);
        Assert.Contains("SetSystemBarsTheme", mainView, StringComparison.Ordinal);
        Assert.Contains("ApplyTopLevelComposition(PlayerHost.IsVisible);", mainView, StringComparison.Ordinal);
        Assert.Contains("ApplySystemChrome", android, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindSolutionRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "NoctraPlayer.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate NoctraPlayer.sln.");
    }
}
