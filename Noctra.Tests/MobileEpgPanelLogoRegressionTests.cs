namespace Noctra.Tests;

public sealed class MobileEpgPanelLogoRegressionTests
{
    [Fact]
    public void EpgPanel_ReactivatesDescendantImagesAfterInitialGuideRebuild()
    {
        var source = ReadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerEpgPanel.axaml.cs");

        var openStart = source.IndexOf(
            "public async Task OpenAsync()",
            StringComparison.Ordinal);
        var rebuild = openStart >= 0
            ? source.IndexOf(
                "Guide.Rebuild(DateTime.Now)",
                openStart,
                StringComparison.Ordinal)
            : -1;
        var refresh = rebuild >= 0
            ? source.IndexOf(
                "QueueImageLoadRefresh();",
                rebuild,
                StringComparison.Ordinal)
            : -1;
        var helperStart = source.IndexOf(
            "private void QueueImageLoadRefresh()",
            StringComparison.Ordinal);
        var helperEnd = helperStart >= 0
            ? source.IndexOf(
                "private void OnDataContextChanged",
                helperStart,
                StringComparison.Ordinal)
            : -1;

        Assert.True(openStart >= 0);
        Assert.True(rebuild > openStart);
        Assert.True(refresh > rebuild);
        Assert.True(helperStart >= 0 && helperEnd > helperStart);

        var helper = source[helperStart..helperEnd];
        Assert.Contains(
            "RemoteImage.SetDescendantLoadsActive(EpgModeRoot, true)",
            helper,
            StringComparison.Ordinal);
        Assert.Contains(
            "DispatcherPriority.Loaded",
            helper,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
