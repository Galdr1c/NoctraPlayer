using Noctra.Core.Services;

namespace Noctra.Tests;

public sealed class BoundedResponseReaderTests
{
    [Fact]
    public async Task CopyToAsync_RejectsChunkedBodyPastLimit()
    {
        await using var source = new MemoryStream(new byte[9]);
        await using var destination = new MemoryStream();

        var accepted = await BoundedResponseReader.CopyToAsync(
            source,
            destination,
            maxBytes: 8,
            CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(8, destination.Length);
    }

    [Fact]
    public async Task CopyToAsync_AcceptsBodyAtLimit()
    {
        await using var source = new MemoryStream(new byte[8]);
        await using var destination = new MemoryStream();

        var accepted = await BoundedResponseReader.CopyToAsync(
            source,
            destination,
            maxBytes: 8,
            CancellationToken.None);

        Assert.True(accepted);
        Assert.Equal(8, destination.Length);
    }
}
