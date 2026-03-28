using System;
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
    public void FixChannelTypes_CategoryWithSeriesKeyword_ChangesTypeToSeries()
    {
        // Arrange: (S|UK) pattern used by provider
        var channels = new List<Channel>
        {
            Live("The Boys", "(S|UK) English Series", "http://stream/1"),
            Live("The Boys 2", "(S|UK) English Series", "http://stream/2"),
            Live("Other Channel", "TR TURKIYE", "http://stream/3")
        };

        // Act
        _sut.FixChannelTypes(channels);

        // Assert
        var seriesChannels = channels.Where(c => c.GroupTitle == "(S|UK) English Series").ToList();
        Assert.All(seriesChannels, c => Assert.Equal(ChannelType.Series, c.Type));
        
        var liveChannel = channels.First(c => c.GroupTitle == "TR TURKIYE");
        Assert.Equal(ChannelType.Live, liveChannel.Type);
    }

    [Fact]
    public void Organize_CrossTypeDuplicates_MergesAndPrefersSeries()
    {
        // Arrange
        var channels = new List<Channel>
        {
            // Same content, one as Live (leak), one as Series
            Live("The Boys S01E01", "(S|UK) English Series", "http://cdn/theboys_1_1"),
            Series("The Boys S01E01", "Diziler", "http://cdn/theboys_1_1")
        };

        // Act
        var result = _sut.Organize(channels);

        // Assert
        // Should be merged into 1 channel, typed as Series
        Assert.Single(result);
        Assert.Equal(ChannelType.Series, result[0].Type);
    }

    [Fact]
    public void Organize_SeriesCategoryLeak_IsCorrectedAndGroupedInSeries()
    {
        // Arrange
        var channels = new List<Channel>
        {
            Live("Game of Thrones S01E01", "(S|RU) Russian Series", "http://cdn/got_1_1"),
            Live("TRT 1", "TR TURKIYE", "http://cdn/trt1")
        };

        // Act
        var result = _sut.Organize(channels);

        // Assert
        var got = result.First(c => c.Name.Contains("Game of Thrones"));
        var trt = result.First(c => c.Name == "TRT 1");

        Assert.Equal(ChannelType.Series, got.Type);
        Assert.Equal(ChannelType.Live, trt.Type);
    }
}
