namespace Noctra.Tests;

public sealed class DesktopDownloadsVirtualizationTests
{
    [Fact]
    public void DownloadCenterUsesVirtualizedListsForLargeCollections()
    {
        var view = ReadProjectFile("Noctra.Avalonia", "Views", "DownloadsView.axaml");

        Assert.Contains("<ListBox ItemsSource=\"{Binding ActiveDownloadingItems}\"", view, StringComparison.Ordinal);
        Assert.Contains("<ListBox ItemsSource=\"{Binding QueuedDownloadItems}\"", view, StringComparison.Ordinal);
        Assert.Contains("<ListBox ItemsSource=\"{Binding FailedDownloadItems}\"", view, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(view, "<VirtualizingStackPanel CacheLength=\"0.5\" />"));
        Assert.DoesNotContain(
            "<ItemsControl ItemsSource=\"{Binding ActiveDownloadingItems}\">",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<ItemsControl ItemsSource=\"{Binding QueuedDownloadItems}\">",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<ItemsControl ItemsSource=\"{Binding FailedDownloadItems}\">",
            view,
            StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(view, "SelectionChanged=\"ClearTransientSelection\""));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
