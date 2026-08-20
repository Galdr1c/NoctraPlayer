namespace Noctra.Tests;

public sealed class PlayerDispatcherBackpressureContractTests
{
    [Fact]
    public void BackgroundPlayerCallbacks_DoNotSynchronouslyBlockOnUiDispatcher()
    {
        var playback = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "Player", "PlayerPlaybackController.cs"));
        var quality = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "Player", "PlayerQualityMonitor.cs"));
        var stall = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "Player", "PlayerStallDetector.cs"));
        var main = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "MainViewModel.cs"));
        var playerViewModel = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "PlayerViewModel.cs"));

        Assert.DoesNotContain(
            "DispatcherService.Invoke(",
            MethodBody(playback, "OnVideoPlayerServicePlayingChanged"),
            StringComparison.Ordinal);
        Assert.Contains(
            "IsPlayerCallbackCurrent",
            MethodBody(playback, "OnVideoPlayerServicePlayingChanged"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DispatcherService.Invoke(",
            MethodBody(playback, "OnVideoPlayerServiceBufferingChanged"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DispatcherService.Invoke(",
            MethodBody(playback, "OnVideoPlayerServiceVolumeChanged"),
            StringComparison.Ordinal);
        Assert.Contains(
            "_positionDispatchVersion",
            MethodBody(playback, "public void OnVideoPlayerServicePositionChanged"),
            StringComparison.Ordinal);
        Assert.Contains(
            "IsPlayerCallbackCurrent",
            MethodBody(playback, "public void OnVideoPlayerServicePositionChanged"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DispatcherService.Invoke(",
            MethodBody(quality, "OnVideoPlayerServiceQualityDetected"),
            StringComparison.Ordinal);
        Assert.Contains(
            "IsPlayerCallbackCurrent",
            MethodBody(quality, "OnVideoPlayerServiceQualityDetected"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_dispatcherService.Invoke(",
            MethodBody(playerViewModel, "private void OnVideoPlayerServicePlaybackEnded"),
            StringComparison.Ordinal);
        Assert.Contains(
            "IsPlayerCallbackCurrent",
            MethodBody(playerViewModel, "private void OnVideoPlayerServicePlaybackEnded"),
            StringComparison.Ordinal);
        Assert.Contains(
            "IsPlayerCallbackCurrent",
            MethodBody(playerViewModel, "private void OnVideoPlayerServiceErrorOccurred"),
            StringComparison.Ordinal);
        Assert.Contains(
            "IsPlayerCallbackCurrent",
            MethodBody(playerViewModel, "private void OnVideoPlayerServiceCuesChanged"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherService.Invoke(", stall, StringComparison.Ordinal);

        Assert.Contains("BeginInvoke", playback, StringComparison.Ordinal);
        Assert.Contains("dispatchVersion", playback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "await _vm.DispatcherService.InvokeAsync",
            MethodBody(quality, "RefreshTracksWithRetryAsync"),
            StringComparison.Ordinal);
        Assert.Contains("BeginInvoke", stall, StringComparison.Ordinal);
        Assert.Contains("InvokeAsync", stall, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoPlayerService.Stop", stall, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoPlayerService.PlayAsync", stall, StringComparison.Ordinal);
        Assert.Contains("PlaybackController.PlayChannelAsync", stall, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", stall, StringComparison.Ordinal);
        Assert.Contains("await EnsurePlaybackHealthAsync", stall, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_dispatcherService.Invoke(",
            MethodBody(main, "public async Task LoadMoreChannelsAsync"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_dispatcherService.Invoke(",
            MethodBody(main, "private async Task EnrichChannelsWithEpgAsyncCore"),
            StringComparison.Ordinal);
        Assert.Contains(
            "await _dispatcherService.InvokeAsync",
            MethodBody(main, "public async Task LoadMoreChannelsAsync"),
            StringComparison.Ordinal);

        var playChannel = MethodBody(playback, "public async Task PlayChannelAsync");
        var invalidateIndex = playChannel.IndexOf("InvalidatePlayerCallbacks", StringComparison.Ordinal);
        var acceptIndex = playChannel.IndexOf("AcceptPlayerCallbacks", StringComparison.Ordinal);
        Assert.True(invalidateIndex >= 0);
        Assert.True(acceptIndex > invalidateIndex);
    }

    private static string MethodBody(string source, string methodName)
    {
        var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Method not found: {methodName}");

        var braceStart = source.IndexOf('{', methodStart);
        Assert.True(braceStart >= 0, $"Method body not found: {methodName}");

        var depth = 0;
        for (var index = braceStart; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (depth == 0)
            {
                return source[braceStart..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Unterminated method body: {methodName}");
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
