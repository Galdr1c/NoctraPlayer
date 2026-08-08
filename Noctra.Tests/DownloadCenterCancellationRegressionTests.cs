namespace Noctra.Tests;

public sealed class DownloadCenterCancellationRegressionTests
{
    [Fact]
    public void RefreshFailurePreservesLastKnownDownloadRows()
    {
        var viewModel = ReadProjectFile("Noctra.Core", "ViewModels", "MainViewModel.cs");
        var refreshStart = viewModel.IndexOf(
            "private async Task RefreshDownloadsFromServiceCoreAsync",
            StringComparison.Ordinal);
        Assert.True(refreshStart >= 0);
        var catchStart = viewModel.IndexOf("catch (Exception ex)", refreshStart, StringComparison.Ordinal);
        var catchEnd = viewModel.IndexOf(
            "ShowDownloadsEmptyState =",
            catchStart,
            StringComparison.Ordinal);
        Assert.True(catchStart > refreshStart);
        Assert.True(catchEnd > catchStart);

        var failurePath = viewModel[catchStart..catchEnd];
        Assert.DoesNotContain("SetItems(", failurePath, StringComparison.Ordinal);
        Assert.Contains("_logger?.LogDebug", failurePath, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadDeletionRetriesTransientSqliteBusyFailures()
    {
        var service = ReadProjectFile("Noctra.Core", "Services", "ContentDownloadService.cs");

        Assert.Contains("RemoveDownloadArtifactsAndRecordWithRetryAsync", service, StringComparison.Ordinal);
        Assert.Contains("IsDatabaseBusyException", service, StringComparison.Ordinal);
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
