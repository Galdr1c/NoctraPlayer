namespace Noctra.Tests;

public sealed class KeysetPaginationContractTests
{
    [Fact]
    public void NewestAndOldestCursorsMoveStrictlyPastLastId()
    {
        var channels = new[]
        {
            new Noctra.Models.Channel { Id = 8 },
            new Noctra.Models.Channel { Id = 9 },
            new Noctra.Models.Channel { Id = 10 }
        }.AsQueryable();
        var cursor = new Noctra.Services.Interfaces.ContentPageCursor(9);

        var newest = Noctra.Services.PlaylistService
            .ApplyKeysetCursor(channels, Noctra.Models.ChannelSortOrder.NewestFirst, cursor)
            .OrderByDescending(channel => channel.Id)
            .Select(channel => channel.Id)
            .ToArray();
        var oldest = Noctra.Services.PlaylistService
            .ApplyKeysetCursor(channels, Noctra.Models.ChannelSortOrder.OldestFirst, cursor)
            .OrderBy(channel => channel.Id)
            .Select(channel => channel.Id)
            .ToArray();

        Assert.Equal(new[] { 8 }, newest);
        Assert.Equal(new[] { 10 }, oldest);
        Assert.False(Noctra.Services.PlaylistService.UsesKeysetPagination(
            Noctra.Models.ChannelSortOrder.NameAsc));
    }

    [Fact]
    public void ChannelPaginationCarriesCursorThroughQueryAndViewModel()
    {
        var requestContract = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "Interfaces", "IContentQueryService.cs"));
        var queryService = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "ContentQueryService.cs"));
        var playlistService = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "PlaylistService.cs"));
        var viewModel = File.ReadAllText(ProjectSource(
            "Noctra.Core", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("ContentPageCursor", requestContract, StringComparison.Ordinal);
        Assert.Contains("request.Cursor", queryService, StringComparison.Ordinal);
        Assert.Contains("ApplyKeysetCursor", playlistService, StringComparison.Ordinal);
        Assert.Contains("_lastChannelCursorId", viewModel, StringComparison.Ordinal);
        Assert.Contains("Cursor:", viewModel, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
