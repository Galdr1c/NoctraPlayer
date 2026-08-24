namespace Noctra.Tests;

public sealed class WatermarkPositionTests
{
    [Fact]
    public void WatermarkViewKeepsTheBrandingPositionStable()
    {
        var view = ReadProjectFile("Noctra.Avalonia", "Views", "WatermarkView.axaml");
        var mobileView = ReadProjectFile("Noctra.Mobile", "Views", "MobileWatermarkView.axaml");
        var viewModel = ReadProjectFile("Noctra.Core", "ViewModels", "WatermarkViewModel.cs");

        Assert.DoesNotContain("<UserControl.RenderTransform>", view, StringComparison.Ordinal);
        Assert.DoesNotContain("TranslateX", view, StringComparison.Ordinal);
        Assert.DoesNotContain("TranslateY", view, StringComparison.Ordinal);
        Assert.Contains(
            "FontSize=\"{DynamicResource FWatermark}\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "FontSize=\"{DynamicResource FWatermark}\"",
            mobileView,
            StringComparison.Ordinal);
        Assert.Contains("Opacity=\"{Binding Opacity}\"", view, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"{Binding Opacity}\"", mobileView, StringComparison.Ordinal);
        Assert.Contains("private double _opacity = 0.24", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("_shiftTimer", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("TranslateX", mobileView, StringComparison.Ordinal);
        Assert.DoesNotContain("TranslateY", mobileView, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
