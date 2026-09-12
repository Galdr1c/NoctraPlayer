using System.Xml.Linq;

namespace Noctra.Tests;

public sealed class DesktopSeriesDetailDownloadToastTests
{
    [Fact]
    public void SeriesDetail_HostsNonInteractiveDownloadFeedbackAboveItsContent()
    {
        var document = XDocument.Load(ProjectFile("Noctra.Avalonia", "MainWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var detail = document.Descendants()
            .Single(element => (string?)element.Attribute(x + "Name") == "SeriesDetailOverlay");
        var toast = detail.Descendants()
            .SingleOrDefault(element => (string?)element.Attribute(x + "Name") == "SeriesDetailDownloadToast");

        Assert.NotNull(toast);
        Assert.Equal("False", (string?)toast.Attribute("IsHitTestVisible"));
        Assert.Contains("Snackbar", (string?)toast.Attribute("Classes"));
        Assert.Contains("IsSeriesDetailDownloadToastVisible", (string?)toast.Attribute("IsVisible"));
        Assert.Contains("SeriesDetailDownloadToastText", toast.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesDetail_DownloadFeedbackUsesMainViewModelAndStopsWhenDetailCloses()
    {
        var sourcePath = ProjectFile("Noctra.Avalonia", "MainWindow.SeriesDetailDownloadToast.cs");
        Assert.True(File.Exists(sourcePath), "Desktop Series Detail must observe download feedback.");
        var source = File.ReadAllText(sourcePath);
        var window = File.ReadAllText(ProjectFile("Noctra.Avalonia", "MainWindow.axaml.cs"));

        Assert.Contains("nameof(MainViewModel.DownloadStatusMessage)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.IsDownloadInProgress)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.IsSeriesDetailVisible)", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(2600)", source, StringComparison.Ordinal);
        Assert.Contains("IsSeriesDetailDownloadToastVisible = false", source, StringComparison.Ordinal);
        Assert.Contains("UpdateSeriesDetailDownloadToast(e.PropertyName)", window, StringComparison.Ordinal);
        Assert.Contains("StopSeriesDetailDownloadToast()", window, StringComparison.Ordinal);
    }

    private static string ProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
