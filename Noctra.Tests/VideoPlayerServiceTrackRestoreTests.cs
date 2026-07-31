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
}
