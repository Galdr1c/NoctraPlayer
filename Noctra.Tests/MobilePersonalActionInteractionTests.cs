namespace Noctra.Tests;

public sealed class MobilePersonalActionInteractionTests
{
    [Fact]
    public void SeriesDetailPersonalActions_DoNotBindLongRunningCommandsToButtonEnabledState()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml");

        AssertButtonUsesTappedHandler(view, "MyListToggleButton", "MyListToggleButton_Tapped");
        AssertButtonUsesTappedHandler(view, "FavoriteToggleButton", "FavoriteToggleButton_Tapped");
    }

    [Fact]
    public void PersonalActionPressedState_KeepsSurfaceTransparent()
    {
        var seriesView = ReadProjectFile("Noctra.Mobile", "Views", "MobileSeriesDetailView.axaml");
        var liveCard = ReadProjectFile("Noctra.Mobile", "Controls", "MobileLiveTvCard.axaml");

        AssertPressedTemplateBackgroundIsTransparent(seriesView, "Button.detailAction:pressed");
        AssertPressedTemplateBackgroundIsTransparent(liveCard, "Button.FavoriteBtn:pressed");
    }

    private static void AssertButtonUsesTappedHandler(
        string view,
        string name,
        string handler)
    {
        var start = view.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        var end = view.IndexOf("</Button>", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, $"Could not locate {name}.");
        var button = view[start..end];

        Assert.DoesNotContain("Command=", button, StringComparison.Ordinal);
        Assert.Contains($"Tapped=\"{handler}\"", button, StringComparison.Ordinal);
    }

    private static void AssertPressedTemplateBackgroundIsTransparent(string view, string selector)
    {
        var start = view.IndexOf($"<Style Selector=\"{selector} /template/ ContentPresenter\"", StringComparison.Ordinal);
        var end = view.IndexOf("</Style>", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, $"Could not locate pressed style for {selector}.");
        var style = view[start..end];
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\"", style, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Noctra.Mobile", "Noctra.Mobile.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
