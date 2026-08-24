namespace Noctra.Tests;

public sealed class MobileWatermarkPositionTests
{
    [Fact]
    public void WatermarkUsesStableBottomAndHidesOnlyBehindDetailSheet()
    {
        var code = ReadProjectFile(
            "Noctra.Mobile", "Views", "MobilePlayerView.axaml.cs");
        var view = ReadProjectFile(
            "Noctra.Mobile", "Views", "MobilePlayerView.axaml");

        Assert.Contains(
            "const double stableBottom = 72",
            code,
            StringComparison.Ordinal);
        Assert.DoesNotContain("normalBottom", code, StringComparison.Ordinal);
        Assert.DoesNotContain("controlsVisibleBottom", code, StringComparison.Ordinal);
        Assert.DoesNotContain("detailPanelBottom", code, StringComparison.Ordinal);
        Assert.Contains(
            "MobileWatermark.IsVisible = !isDetailPanelOpen",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "Margin=\"0,0,24,72\"",
            view,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
