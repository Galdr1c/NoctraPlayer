using System.Reflection;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class StreamQualityInfoTests
{
    [Fact]
    public void DetailLabels_FormatBitratesFromKilobitsPerSecond()
    {
        var quality = new StreamQualityInfo
        {
            VideoCodec = "h264",
            VideoBitrate = 5_000,
            AudioCodec = "aac",
            AudioChannels = 2,
            AudioBitrate = 192
        };

        Assert.Equal("H264 • 5.0 Mbps", quality.DetailLabel);
        Assert.Equal("AAC • Stereo • 192 kbps", quality.AudioDetailLabel);
    }

    [Theory]
    [InlineData(5_000, "5.00 Mbps")]
    [InlineData(800, "800 Kbps")]
    public void PlayerQualityBitrateText_FormatsKilobitsPerSecond(int bitrateKbps, string expected)
    {
        var formatBitrate = typeof(PlayerViewModel).GetMethod(
            "FormatBitrate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(formatBitrate);
        Assert.Equal(expected, formatBitrate.Invoke(null, new object[] { bitrateKbps }));
    }
}
