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
}
