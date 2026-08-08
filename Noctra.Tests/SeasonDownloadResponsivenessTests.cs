namespace Noctra.Tests;

public sealed class SeasonDownloadResponsivenessTests
{
    [Fact]
    public void SeasonDownload_QueuesOutsideUiCommandBeforeReturning()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var method = Slice(source, "private Task DownloadSelectedSeason()", "private async Task QueueSeasonDownloadsInBackgroundAsync");

        Assert.Contains("Task.Run", method, StringComparison.Ordinal);
        Assert.Contains("QueueSeasonDownloadsInBackgroundAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("await _contentDownloadService.QueueDownloadAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SeasonDownload_ReportsProgressThroughDispatcher()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var worker = Slice(source, "private async Task QueueSeasonDownloadsInBackgroundAsync", "private async Task PlayEpisodeSafeAsync");

        Assert.Contains("_dispatcherService.BeginInvoke", worker, StringComparison.Ordinal);
        Assert.Contains("IsDownloadInProgress = false", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadCancellation_DoesNotAwaitAStalledNetworkWorkerForever()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "Services", "ContentDownloadService.cs"));
        var method = Slice(source, "private async Task WaitForActiveWorkerAsync", "private static bool IsDatabaseBusyException");

        Assert.True(
            method.Contains("Task.WhenAny", StringComparison.Ordinal) ||
            method.Contains("workerTask.WaitAsync", StringComparison.Ordinal),
            "Worker shutdown must be bounded by a timeout.");
        Assert.DoesNotContain("await workerTask;", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadCenterRefresh_IsRateLimitedAndOnlyRunsForVisibleDownloadsView()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var subscription = Slice(source, "_contentDownloadService.DownloadsChanged +=", "_contentDownloadService.DownloadCompleted +=");

        Assert.Contains("ActiveView == AppView.Downloads", subscription, StringComparison.Ordinal);
        Assert.Contains("IsDownloadCenterVisible", subscription, StringComparison.Ordinal);

        var refresh = Slice(source, "private async Task ProcessDownloadCenterRefreshAsync()", "private async Task RefreshDownloadsFromServiceAsync");
        Assert.Contains("DownloadsRefreshMinIntervalMs", refresh, StringComparison.Ordinal);
        Assert.Contains("_lastDownloadCenterRefreshUtc", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadActionsRunNetworkAndDatabaseWorkOffUiThread()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var cancel = Slice(source, "private Task CancelDownloadAsync", "private async Task RetryDownloadAsync");
        var toggle = Slice(source, "private Task TogglePauseDownloadAsync", "private async Task StopAllDownloadsAsync");

        Assert.Contains("RunDownloadActionInBackgroundAsync", cancel, StringComparison.Ordinal);
        Assert.Contains("RunDownloadActionInBackgroundAsync", toggle, StringComparison.Ordinal);

        var helper = Slice(source, "private async Task RunDownloadActionInBackgroundAsync", "private async Task StopAllDownloadsAsync");
        Assert.Contains("Task.Run(action)", helper, StringComparison.Ordinal);
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find method start: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Could not find method end: {end}");
        return source[startIndex..endIndex];
    }

    private static string ProjectFile(params string[] segments)
        => Path.Combine(new[] { FindRepositoryRoot() }.Concat(segments).ToArray());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
