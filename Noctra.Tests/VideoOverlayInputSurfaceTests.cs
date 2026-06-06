namespace Noctra.Tests;

public sealed class VideoOverlayInputSurfaceTests
{
    [Fact]
    public void MouseCaptureLayer_UsesNonZeroAlphaBackground()
    {
        var mainWindow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Noctra.Avalonia",
            "MainWindow.axaml"));

        var marker = "x:Name=\"MouseCaptureLayer\"";
        var markerIndex = mainWindow.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, "MouseCaptureLayer was not found.");

        var tagEnd = mainWindow.IndexOf("/>", markerIndex, StringComparison.Ordinal);
        Assert.True(tagEnd >= 0, "MouseCaptureLayer tag was not closed.");

        var mouseCaptureTag = mainWindow[markerIndex..tagEnd];

        Assert.Contains("Background=\"#01000000\"", mouseCaptureTag, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", mouseCaptureTag, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindow_UsesNonZeroAlphaBackground()
    {
        var memoryVideoView = LoadProjectFile(
            "Noctra.Avalonia",
            "Controls",
            "MemoryVideoView.cs");

        Assert.Contains("Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))",
            memoryVideoView, StringComparison.Ordinal);
        Assert.DoesNotContain("Background = Brushes.Transparent",
            memoryVideoView, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindow_RestoresNativeZOrderWhenOwnerReactivates()
    {
        var memoryVideoView = LoadProjectFile(
            "Noctra.Avalonia",
            "Controls",
            "MemoryVideoView.cs");

        Assert.Contains("_rootWindow.Activated += Root_Activated",
            memoryVideoView, StringComparison.Ordinal);
        Assert.Contains("_rootWindow.Activated -= Root_Activated",
            memoryVideoView, StringComparison.Ordinal);
        Assert.Contains("private void Root_Activated", memoryVideoView, StringComparison.Ordinal);
        Assert.Contains("RestoreOverlayZOrder();", memoryVideoView, StringComparison.Ordinal);
        Assert.Contains("SetWindowPos(", memoryVideoView, StringComparison.Ordinal);
    }

    private static string LoadProjectFile(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine(
            new[] { FindRepositoryRoot() }.Concat(pathParts).ToArray()));
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
