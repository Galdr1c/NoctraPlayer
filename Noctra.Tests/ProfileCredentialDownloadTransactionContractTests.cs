namespace Noctra.Tests;

public sealed class ProfileCredentialDownloadTransactionContractTests
{
    [Fact]
    public void CredentialDownloadInvalidationRunsAfterProfileCommit()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "ProfileService.cs"));
        var methodStart = source.IndexOf(
            "private async Task<Profile?> SaveProfileCoreAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private static async Task DeleteProfilePlaylistContentAsync",
            methodStart,
            StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        var commitIndex = method.IndexOf(
            "await transaction.CommitAsync()",
            StringComparison.Ordinal);
        var invalidateIndex = method.IndexOf(
            "FailActiveDownloadsForProfileAsync",
            StringComparison.Ordinal);

        Assert.True(commitIndex >= 0, "Profile transaction commit was not found.");
        Assert.True(invalidateIndex >= 0, "Credential download invalidation was not found.");
        Assert.True(
            commitIndex < invalidateIndex,
            "Download invalidation must not open a second writer while the profile transaction is active.");
    }

    [Fact]
    public void CredentialDownloadFailureDoesNotRollbackCommittedProfile()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "ProfileService.cs"));
        var methodStart = source.IndexOf(
            "private async Task<Profile?> SaveProfileCoreAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private static async Task DeleteProfilePlaylistContentAsync",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        var invalidateIndex = method.IndexOf(
            "FailActiveDownloadsForProfileAsync",
            StringComparison.Ordinal);
        var catchIndex = method.IndexOf(
            "Download invalidation failed after profile commit",
            StringComparison.Ordinal);

        Assert.True(invalidateIndex >= 0);
        Assert.True(
            catchIndex > invalidateIndex,
            "Post-commit download invalidation failures must be isolated and logged.");
    }

    [Fact]
    public void DownloadServiceProtectsCredentialFailureFromWorkerStatusOverwrite()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "ContentDownloadService.cs"));

        Assert.Contains("_credentialFailureRequestedIds", source, StringComparison.Ordinal);
        Assert.Contains("_downloadStateGate", source, StringComparison.Ordinal);
        Assert.Contains(
            "await _downloadStateGate.WaitAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "credentialFailureRequested",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "WaitForActiveWorkerAsync(downloadId",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DownloadStatus.Failed",
            source,
            StringComparison.Ordinal);

        var autoResumeStart = source.IndexOf(
            "private async Task TryAutoResumeAfterTransientInterruptionAsync",
            StringComparison.Ordinal);
        var resumeCall = source.IndexOf(
            "await ResumeDownloadAsync(downloadId",
            autoResumeStart,
            StringComparison.Ordinal);
        Assert.True(autoResumeStart >= 0 && resumeCall > autoResumeStart);
        var autoResumeBlock = source[autoResumeStart..resumeCall];
        Assert.Contains(
            "_credentialFailureRequestedIds.ContainsKey(downloadId)",
            autoResumeBlock,
            StringComparison.Ordinal);

        var pauseStart = source.IndexOf(
            "public async Task PauseDownloadAsync",
            StringComparison.Ordinal);
        var resumeStart = source.IndexOf(
            "public async Task ResumeDownloadAsync",
            pauseStart,
            StringComparison.Ordinal);
        Assert.True(pauseStart >= 0 && resumeStart > pauseStart);
        var pauseBlock = source[pauseStart..resumeStart];
        Assert.Contains("await _downloadStateGate.WaitAsync", pauseBlock, StringComparison.Ordinal);
        Assert.Contains(
            "_credentialFailureRequestedIds.ContainsKey(downloadId)",
            pauseBlock,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialInvalidationCancelsWorkersOutsideStateGate()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "ContentDownloadService.cs"));
        var methodStart = source.IndexOf(
            "public async Task FailActiveDownloadsForProfileAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "public Task DeleteProfileDownloadsAsync",
            methodStart,
            StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        var gateReleaseIndex = method.IndexOf(
            "_downloadStateGate.Release()",
            StringComparison.Ordinal);
        var cancelIndex = method.IndexOf(
            "cts.Cancel()",
            StringComparison.Ordinal);

        Assert.True(gateReleaseIndex >= 0, "The state gate release was not found.");
        Assert.True(cancelIndex > gateReleaseIndex,
            "Worker cancellation must happen after the state gate is released.");
        Assert.Contains(
            "catch (ObjectDisposedException)",
            method,
            StringComparison.Ordinal);
        Assert.Contains(
            "Download cancellation callback failed",
            method,
            StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
