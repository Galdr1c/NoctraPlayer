using System.Collections.Generic;
using System.Linq;
using Noctra.Models;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests;

public class SeriesLeakageTests
{
    private readonly PlaylistOrganizerService _sut = new();

    private static Channel Live(string name, string? group = null, string? url = null)
        => new() { Name = name, Type = ChannelType.Live, GroupTitle = group, StreamUrl = url ?? $"http://x/{name}" };

    private static Channel Series(string name, string? group = null, string? url = null)
        => new() { Name = name, Type = ChannelType.Series, GroupTitle = group, StreamUrl = url ?? $"http://x/{name}" };

    [Fact]
    public void Organize_SameUrlDuplicates_KeepsOne()
    {
        var channels = new List<Channel>
        {
            Live("TRT 1", "Ulusal", "http://cdn/trt1"),
            Live("TRT 1", "Ulusal", "http://cdn/trt1")
        };

        var result = _sut.Organize(channels);

        Assert.Single(result);
    }
}
