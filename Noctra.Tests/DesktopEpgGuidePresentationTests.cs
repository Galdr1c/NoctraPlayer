using Noctra.Avalonia.ViewModels;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class DesktopEpgGuidePresentationTests
{
    [Fact]
    public void Build_KeepsPixelGeometryOutsideCoreAndSnapsWindow()
    {
        var now = new DateTime(2026, 8, 8, 19, 47, 0, DateTimeKind.Local);
        var channel = new Channel { Id = 7, Name = "News" };
        var program = new EpgProgram
        {
            Title = "Evening News",
            StartTime = new DateTime(2026, 8, 8, 19, 30, 0, DateTimeKind.Local),
            EndTime = new DateTime(2026, 8, 8, 20, 0, 0, DateTimeKind.Local)
        };

        var presentation = DesktopEpgGuidePresentation.Build(
            [new EpgGuideRow { Channel = channel, Programs = [program] }],
            channel,
            now);

        Assert.Equal(30, presentation.WindowStart.Minute);
        Assert.Equal(0, presentation.WindowStart.Second);
        Assert.True(presentation.CanvasWidth > 0);
        Assert.Single(presentation.Rows);
        Assert.Single(presentation.Rows[0].Blocks);
        Assert.True(presentation.Rows[0].Blocks[0].PixelLeft >= 0);
    }
}
