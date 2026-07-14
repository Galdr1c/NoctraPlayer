using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public class PlaybackMediaMetadataTests
{
    [Fact]
    public void VideoPlayerContract_ExposesPlaybackMediaMetadata()
    {
        var metadataType = typeof(Channel).Assembly.GetType("Noctra.Models.PlaybackMediaMetadata");

        Assert.NotNull(metadataType);
        Assert.NotNull(metadataType.GetProperty("Title"));
        Assert.NotNull(metadataType.GetProperty("Subtitle"));
        Assert.NotNull(metadataType.GetProperty("ArtworkUrl"));

        var updateMethod = typeof(IVideoPlayerService).GetMethod("UpdateMediaMetadata");
        Assert.NotNull(updateMethod);
        Assert.Equal(metadataType, updateMethod.GetParameters().Single().ParameterType);
    }

    // ── Record equality tests ──────────────────────────────────────────────────

    [Fact]
    public void PlaybackMediaMetadata_RecordEquality_SameValuesAreEqual()
    {
        var a = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn.png");
        var b = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn.png");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void PlaybackMediaMetadata_RecordInequality_DifferentTitleAreNotEqual()
    {
        var a = new PlaybackMediaMetadata("BBC", "News", "https://img.test/bbc.png");
        var b = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn.png");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void PlaybackMediaMetadata_RecordInequality_DifferentSubtitleAreNotEqual()
    {
        var a = new PlaybackMediaMetadata("CNN", "Sports", "https://img.test/cnn.png");
        var b = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn.png");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void PlaybackMediaMetadata_RecordInequality_DifferentArtworkAreNotEqual()
    {
        var a = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn1.png");
        var b = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn2.png");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void PlaybackMediaMetadata_NullOptionalProperties_DefaultsToNull()
    {
        var metadata = new PlaybackMediaMetadata("Channel");

        Assert.Equal("Channel", metadata.Title);
        Assert.Null(metadata.Subtitle);
        Assert.Null(metadata.ArtworkUrl);
    }

    [Fact]
    public void PlaybackMediaMetadata_NullOptionalProperties_AreEqual()
    {
        var a = new PlaybackMediaMetadata("Channel");
        var b = new PlaybackMediaMetadata("Channel", null, null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void PlaybackMediaMetadata_WithAllProperties_ReturnsCorrectValues()
    {
        var metadata = new PlaybackMediaMetadata(
            "Noctra Live",
            "Entertainment",
            "https://cdn.example.com/logo.png");

        Assert.Equal("Noctra Live", metadata.Title);
        Assert.Equal("Entertainment", metadata.Subtitle);
        Assert.Equal("https://cdn.example.com/logo.png", metadata.ArtworkUrl);
    }

    [Fact]
    public void PlaybackMediaMetadata_RecordClone_CreatesNewInstance()
    {
        var original = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn.png");
        var clone = original with { };

        Assert.Equal(original, clone);
        Assert.NotSame(original, clone);
    }

    [Fact]
    public void PlaybackMediaMetadata_RecordClone_CanModifyProperties()
    {
        var original = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn.png");
        var modified = original with { Title = "BBC" };

        Assert.Equal("BBC", modified.Title);
        Assert.Equal("News", modified.Subtitle);
        Assert.Equal("https://img.test/cnn.png", modified.ArtworkUrl);
        Assert.NotEqual(original, modified);
    }

    [Fact]
    public void PlaybackMediaMetadata_DifferentInstances_AreNotReferenceEqual()
    {
        var a = new PlaybackMediaMetadata("CNN");
        var b = new PlaybackMediaMetadata("CNN");

        Assert.Equal(a, b);
        Assert.NotSame(a, b);
    }

    [Fact]
    public void PlaybackMediaMetadata_NullSubtitle_WithNonNullArtwork_PreservesBoth()
    {
        var metadata = new PlaybackMediaMetadata(
            "Channel",
            Subtitle: null,
            ArtworkUrl: "https://img.test/logo.png");

        Assert.Null(metadata.Subtitle);
        Assert.Equal("https://img.test/logo.png", metadata.ArtworkUrl);
    }

    [Fact]
    public void PlaybackMediaMetadata_NullArtwork_WithNonNullSubtitle_PreservesBoth()
    {
        var metadata = new PlaybackMediaMetadata(
            "Channel",
            Subtitle: "Group",
            ArtworkUrl: null);

        Assert.Equal("Group", metadata.Subtitle);
        Assert.Null(metadata.ArtworkUrl);
    }

    [Fact]
    public void PlaybackMediaMetadata_EmptyStrings_AreAllowed()
    {
        var metadata = new PlaybackMediaMetadata("", "", "");

        Assert.Equal("", metadata.Title);
        Assert.Equal("", metadata.Subtitle);
        Assert.Equal("", metadata.ArtworkUrl);
    }

    [Fact]
    public void PlaybackMediaMetadata_ToString_ContainsAllValues()
    {
        var metadata = new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn.png");

        var str = metadata.ToString();

        Assert.Contains("CNN", str);
        Assert.Contains("News", str);
        Assert.Contains("https://img.test/cnn.png", str);
    }

    // ── Channel mapping tests ──────────────────────────────────────────────────

    [Fact]
    public void PlaybackMediaMetadata_ChannelMapping_PreservesFields()
    {
        var channel = new Channel
        {
            Id = 1,
            Name = "Sports TV",
            GroupTitle = "Sports",
            LogoUrl = "https://cdn.example.com/sports.png"
        };

        var metadata = new PlaybackMediaMetadata(
            channel.Name,
            channel.GroupTitle,
            channel.CoverUrl);

        Assert.Equal("Sports TV", metadata.Title);
        Assert.Equal("Sports", metadata.Subtitle);
        Assert.Equal("https://cdn.example.com/sports.png", metadata.ArtworkUrl);
    }

    [Fact]
    public void PlaybackMediaMetadata_ChannelWithNullFields_HandlesGracefully()
    {
        var channel = new Channel
        {
            Id = 2,
            Name = "Test",
            GroupTitle = null,
            LogoUrl = null
        };

        var metadata = new PlaybackMediaMetadata(
            channel.Name,
            channel.GroupTitle,
            channel.CoverUrl);

        Assert.Equal("Test", metadata.Title);
        Assert.Null(metadata.Subtitle);
        Assert.Null(metadata.ArtworkUrl);
    }

    // ── Integration flow: service contract ──────────────────────────────────────

    [Fact]
    public void UpdateMediaMetadata_DefaultInterfaceImplementation_ThrowsOnNull()
    {
        var service = new MinimalVideoPlayerService();

        var ex = Record.Exception(() => service.UpdateMediaMetadata(null!));

        Assert.NotNull(ex);
        Assert.IsType<ArgumentNullException>(ex);
    }

    [Fact]
    public void UpdateMediaMetadata_AfterSet_MetadataPersistsUntilNextSet()
    {
        var service = new MinimalVideoPlayerService();

        service.UpdateMediaMetadata(new PlaybackMediaMetadata("CNN", "News"));
        Assert.Equal("CNN", service.LastMetadata?.Title);
        Assert.Equal("News", service.LastMetadata?.Subtitle);

        // Reinitialize should not clear metadata
        service.ReinitializeAsync();
        Assert.Equal("CNN", service.LastMetadata?.Title);
        Assert.Equal("News", service.LastMetadata?.Subtitle);
    }

    [Fact]
    public void UpdateMediaMetadata_MultipleUpdates_KeepsLatest()
    {
        var service = new MinimalVideoPlayerService();

        service.UpdateMediaMetadata(new PlaybackMediaMetadata("CNN", "News"));
        service.UpdateMediaMetadata(new PlaybackMediaMetadata("BBC", "Sports"));
        service.UpdateMediaMetadata(new PlaybackMediaMetadata("Sky", "Entertainment"));

        Assert.Equal("Sky", service.LastMetadata?.Title);
        Assert.Equal("Entertainment", service.LastMetadata?.Subtitle);
    }

    [Fact]
    public void UpdateMediaMetadata_DoesNotAffectPlaybackState()
    {
        var service = new MinimalVideoPlayerService();

        service.UpdateMediaMetadata(new PlaybackMediaMetadata("CNN"));
        Assert.False(service.IsPlaying);

        service.UpdateMediaMetadata(new PlaybackMediaMetadata("BBC"));
        Assert.False(service.IsPlaying);
    }

    [Fact]
    public void UpdateMediaMetadata_MetadataCapturedAtPlayTime_MatchesWhatWasSet()
    {
        var service = new MinimalVideoPlayerService();

        service.UpdateMediaMetadata(new PlaybackMediaMetadata("CNN", "News", "https://img.test/cnn.png"));
        service.PlayAsync("http://stream.test/live.m3u8").GetAwaiter().GetResult();

        Assert.Equal("CNN", service.MetadataAtPlay?.Title);
        Assert.Equal("News", service.MetadataAtPlay?.Subtitle);
        Assert.Equal("https://img.test/cnn.png", service.MetadataAtPlay?.ArtworkUrl);
    }

    [Fact]
    public void UpdateMediaMetadata_MetadataRetainedAcrossMultiplePlays()
    {
        var service = new MinimalVideoPlayerService();

        service.UpdateMediaMetadata(new PlaybackMediaMetadata("Channel A", "Group A"));
        service.PlayAsync("http://stream1.test/live.m3u8").GetAwaiter().GetResult();
        Assert.Equal("Channel A", service.MetadataAtPlay?.Title);

        service.UpdateMediaMetadata(new PlaybackMediaMetadata("Channel B", "Group B"));
        service.PlayAsync("http://stream2.test/live.m3u8").GetAwaiter().GetResult();
        Assert.Equal("Channel B", service.MetadataAtPlay?.Title);
        Assert.Equal("Group B", service.MetadataAtPlay?.Subtitle);
    }

    // ── Minimal service for integration tests ───────────────────────────────────

    private sealed class MinimalVideoPlayerService : IVideoPlayerService
    {
        public PlaybackMediaMetadata? LastMetadata { get; private set; }
        public PlaybackMediaMetadata? MetadataAtPlay { get; private set; }
        public string? CurrentUrl { get; private set; }
        public bool IsPlaying { get; private set; }
        public PlaybackState State => IsPlaying ? PlaybackState.Playing : PlaybackState.Stopped;
        public bool HasLoadedMedia => CurrentUrl != null;
        public long CurrentTimeMilliseconds => 0;
        public double Position { get; set; }
        public float PlaybackRate { get; set; } = 1f;
        public double Duration => 0;
        public int Volume { get; set; }
        public bool IsMuted { get; set; }
        public StreamQualityInfo? StreamQuality => null;
        public IReadOnlyList<(int Id, string? Name)> AudioTracks => Array.Empty<(int, string?)>();
        public IReadOnlyList<(int Id, string? Name)> SubtitleTracks => Array.Empty<(int, string?)>();

        public event EventHandler<int>? VolumeChanged;
        public event EventHandler<bool>? PlayingChanged;
        public event EventHandler<double>? PositionChanged;
        public event EventHandler? PlayerReady;
        public event EventHandler? PlaybackEnded;
        public event EventHandler<float>? BufferingChanged;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<string?>? SubtitleTextChanged;
        public event EventHandler<StreamQualityInfo>? QualityDetected;

        public Task PlayAsync(string url, double startTimeSeconds = 0)
        {
            MetadataAtPlay = LastMetadata;
            CurrentUrl = url;
            IsPlaying = true;
            return Task.CompletedTask;
        }

        public Task ReinitializeAsync() => Task.CompletedTask;

        public void UpdateMediaMetadata(PlaybackMediaMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            LastMetadata = metadata;
        }

        public void Pause() => IsPlaying = false;
        public void Resume() => IsPlaying = true;
        public void Stop() { IsPlaying = false; CurrentUrl = null; }
        public Task HardSeekAsync(double seconds) => Task.CompletedTask;
        public void SeekToTime(long milliseconds) { }
        public void PlayLoadedMedia() => Resume();
        public void SetAudioTrack(int trackId) { }
        public void SetSubtitleTrack(int trackId) { }
        public void SetVideoLayout(string? aspectRatio, string? cropGeometry) { }
        public void Dispose() { }
    }
}
