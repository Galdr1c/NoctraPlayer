namespace Noctra.Tests;

public sealed class PlayerTrackRefreshTests
{
    [Fact]
    public void TrackRefresh_GatesCollectionReplacementWhenProviderTracksAreUnchanged()
    {
        var source = ReadProjectFile("Noctra.Core", "ViewModels", "Player", "PlayerQualityMonitor.cs");

        Assert.Contains("TrackOptionsMatch(_vm.AudioTracks, audioTracks)", source, StringComparison.Ordinal);
        Assert.Contains("TrackOptionsMatch(_vm.SubtitleTracks, subtitleTracks)", source, StringComparison.Ordinal);
        Assert.Contains("if (audioChanged)", source, StringComparison.Ordinal);
        Assert.Contains("if (subtitleChanged)", source, StringComparison.Ordinal);
        Assert.Contains("if (audioChanged &&", source, StringComparison.Ordinal);
        Assert.Contains("_vm.SelectedAudioTrack >= 0", source, StringComparison.Ordinal);
        Assert.Contains("if (subtitleChanged &&", source, StringComparison.Ordinal);
        Assert.Contains("_vm.SelectedSubtitleTrack >= -1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningAudioSheet_DoesNotSynchronouslyRebuildTrackCollections()
    {
        var source = ReadProjectFile("Noctra.Core", "ViewModels", "Player", "PlayerOverlayManager.cs");
        var start = source.IndexOf("public void OpenAudioSettings()", StringComparison.Ordinal);
        var end = source.IndexOf("    public void OpenQualitySettings()", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Could not locate OpenAudioSettings.");
        var method = source[start..end];

        Assert.DoesNotContain("_vm.UpdateMediaInfo();", method, StringComparison.Ordinal);
        Assert.Contains("_ = _vm.RefreshTracksWithRetryAsync();", method, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackRefreshRetries_CancelPreviousRunWhenAnewRunStarts()
    {
        var source = ReadProjectFile("Noctra.Core", "ViewModels", "Player", "PlayerQualityMonitor.cs");

        Assert.Contains("_trackRefreshCts", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _trackRefreshCts", source, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(delay, cts.Token)", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", source, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Noctra.Core", "Noctra.Core.csproj")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");

        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
