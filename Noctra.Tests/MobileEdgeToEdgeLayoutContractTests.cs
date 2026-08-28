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
    public void AndroidActivity_PaintsOpaqueBackdropBehindTransparentAvaloniaSurface()
    {
        var source = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        Assert.Contains("content.SetBackgroundColor(Color.Rgb(10, 10, 10));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainView_UsesTransparentTopLevelOnlyDuringNativeVideoPlayback()
    {
        var source = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("ApplyTopLevelComposition(PlayerHost.IsVisible);", source, StringComparison.Ordinal);
        Assert.Contains("WindowTransparencyLevel.None", source, StringComparison.Ordinal);
        Assert.Contains("topLevel.Background = Brushes.Black", source, StringComparison.Ordinal);
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
