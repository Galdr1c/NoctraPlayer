using Noctra.Services;
using Xunit;

namespace Noctra.Tests;

public sealed class VideoPlayerServiceTrackRestoreTests
{
    private static IReadOnlyList<(int Id, string? Name)> Tracks(params (int Id, string? Name)[] tracks)
        => tracks;

    [Fact]
    public void ResolveTrackId_MatchesByName_CaseInsensitive()
    {
        var current = Tracks(
            (0, "Disable"),
            (1, "Türkçe"),
            (2, "English"));

        Assert.Equal(2, VideoPlayerService.ResolveTrackId("english", 5, current));
        Assert.Equal(1, VideoPlayerService.ResolveTrackId("TÜRKÇE", 5, current));
    }

    [Fact]
    public void ResolveTrackId_NameNotFound_FallsBackToSavedId()
    {
        var current = Tracks(
            (0, "Disable"),
            (1, "English"));

        Assert.Equal(5, VideoPlayerService.ResolveTrackId("Deutsch", 5, current));
    }

    [Fact]
    public void ResolveTrackId_NullOrEmptyName_UsesSavedId()
    {
        var current = Tracks(
            (0, "Disable"),
            (1, "English"));

        Assert.Equal(3, VideoPlayerService.ResolveTrackId(null, 3, current));
        Assert.Equal(3, VideoPlayerService.ResolveTrackId("  ", 3, current));
    }

    [Fact]
    public void ResolveTrackId_EmptyTrackList_FallsBackToSavedId()
    {
        Assert.Equal(4, VideoPlayerService.ResolveTrackId("English", 4, Tracks()));
    }

    [Fact]
    public void ResolveTrackId_DisabledSubtitle_MinusOne_IsPreserved()
    {
        var current = Tracks(
            (0, "Disable"),
            (1, "English"));

        Assert.Equal(-1, VideoPlayerService.ResolveTrackId(null, -1, current));
    }

    [Fact]
    public void ResolveTrackId_ReinitChangedIds_StillRestoresByNewId()
    {
        // Reinit sonrası ID'ler kaydıysa isim eşleşmesi yeni ID'yi bulmalı
        var current = Tracks(
            (0, "Disable"),
            (101, "English"),
            (102, "French"));

        Assert.Equal(101, VideoPlayerService.ResolveTrackId("English", 5, current));
    }

    [Fact]
    public void SubtitleLayout_DoesNotPerformNetworkParseBeforePlayback()
    {
        var source = ReadProjectFile("Noctra.Core", "Services", "VideoPlayerService.cs");

        Assert.DoesNotContain(
            "media.Parse(MediaParseOptions.ParseNetwork, timeout: 2000)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CreateLinkedTokenSource(cancellationToken, tcs.Token)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "media.AddOption($\":sub-margin={_lastSubtitleMargin}\")",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopSubtitleScaling_PreservesLargeAndExtraLargeDifference()
    {
        var source = ReadProjectFile("Noctra.Core", "Services", "VideoPlayerService.cs");

        Assert.Contains(
            "fontPercentage = _lastSubtitleFontSize / 1080d",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_lastSubtitleFontSize >= 60",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReinitializeAndEndSession_SerializeNativePlayerRelease()
    {
        var source = ReadProjectFile("Noctra.Core", "Services", "VideoPlayerService.cs");
        var endSession = ExtractMethod(source, "public async Task EndSessionAsync");

        Assert.Contains("_reinitializeLock", source, StringComparison.Ordinal);
        Assert.Contains("await _reinitializeLock.WaitAsync", source, StringComparison.Ordinal);
        Assert.Contains("await _dispatcherService.InvokeAsync", endSession, StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleDebounce_ClearsCompletedSaveState()
    {
        var source = ReadProjectFile("Noctra.Core", "ViewModels", "PlayerViewModel.cs");

        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_subtitleSaveCts, cts)", source, StringComparison.Ordinal);
        Assert.Contains("_subtitleSaveCts = null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSaves_AreSerializedAcrossProfileAndBackgroundWrites()
    {
        var source = ReadProjectFile("Noctra.Core", "Services", "SettingsService.cs");
        var saveMethod = ExtractMethod(source, "public async Task SaveAsync");

        Assert.Contains("SemaphoreSlim _saveLock", source, StringComparison.Ordinal);
        Assert.Contains("await _saveLock.WaitAsync", saveMethod, StringComparison.Ordinal);
        Assert.Contains("_saveLock.Release()", saveMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidRelease_UnregistersAudioNoisyReceiver()
    {
        var source = ReadProjectFile(
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs");
        var releasePlayer = ExtractMethod(source, "private void ReleasePlayer");

        Assert.Contains(
            "UpdateAudioBecomingNoisyReceiver(false)",
            releasePlayer,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] relativeParts)
        => File.ReadAllText(FindProjectFile(relativeParts));

    private static string FindProjectFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "Noctra.Core")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. relativeParts]);
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var start = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method {methodName} not found");

        var nextMethod = source.IndexOf("\n    public ", start + methodName.Length, StringComparison.Ordinal);
        if (nextMethod < 0)
        {
            nextMethod = source.Length;
        }

        return source[start..nextMethod];
    }
}
