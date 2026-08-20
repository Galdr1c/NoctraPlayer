namespace Noctra.Tests;

public sealed class EpgDatabaseFilterContractTests
{
    [Fact]
    public void EpgLiveLoaderUsesPlaylistQueryWithDatabaseLevelTypeFilter()
    {
        var epgService = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "EpgService.cs"));
        var playlistService = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "PlaylistService.cs"));
        var playlistContract = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "Interfaces", "IPlaylistService.cs"));

        Assert.Contains("GetLiveChannelsAsync", epgService, StringComparison.Ordinal);
        Assert.Contains("GetLiveChannelsAsync", playlistContract, StringComparison.Ordinal);
        Assert.Contains("channel.Type == ChannelType.Live", playlistService, StringComparison.Ordinal);
        Assert.Contains("ToListAsync(cancellationToken)", playlistService, StringComparison.Ordinal);
        Assert.Contains("var channelSnapshot = await GetLiveChannelsAsync(playlistId)", playlistService, StringComparison.Ordinal);
        Assert.DoesNotContain("Include(p => p.Channels)", playlistService, StringComparison.Ordinal);
        Assert.DoesNotContain("GetChannelsAsync(playlistId)", epgService, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
