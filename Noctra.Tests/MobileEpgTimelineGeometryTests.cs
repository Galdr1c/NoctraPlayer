using Noctra.Mobile.ViewModels;

namespace Noctra.Tests;

public sealed class MobileEpgTimelineGeometryTests
{
    [Fact]
    public void SnapWindowStart_UsesPreviousRealHalfHourBoundary()
    {
        var now = new DateTime(2026, 8, 8, 19, 47, 12, DateTimeKind.Local);

        var start = MobileEpgTimelineGeometry.SnapWindowStart(
            now,
            TimeSpan.FromHours(2));

        Assert.Equal(new DateTime(2026, 8, 8, 17, 30, 0, DateTimeKind.Local), start);
    }

    [Fact]
    public void CalculateBlock_ClipsProgramsAtWindowEdges()
    {
        var windowStart = new DateTime(2026, 8, 8, 17, 30, 0, DateTimeKind.Local);
        var windowEnd = windowStart.AddHours(8);

        var block = MobileEpgTimelineGeometry.CalculateBlock(
            windowStart.AddMinutes(-45),
            windowStart.AddMinutes(30),
            windowStart,
            windowEnd,
            pixelsPerMinute: 3,
            minimumWidth: 24);

        Assert.NotNull(block);
        Assert.Equal(0, block.Value.Left);
        Assert.Equal(90, block.Value.Width);
        Assert.True(block.Value.IsClippedLeft);
        Assert.False(block.Value.IsClippedRight);
    }

    [Fact]
    public void FindNearestProgramIndex_PreservesTheFocusedTimeAcrossRows()
    {
        var day = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Local);
        var programs = new[]
        {
            new MobileEpgProgramInterval(day.AddHours(19), day.AddHours(19.5)),
            new MobileEpgProgramInterval(day.AddHours(19.5), day.AddHours(20.5)),
            new MobileEpgProgramInterval(day.AddHours(20.5), day.AddHours(21))
        };

        var index = MobileEpgTimelineGeometry.FindNearestProgramIndex(
            programs,
            day.AddHours(19.75));

        Assert.Equal(1, index);
    }

    [Fact]
    public void FindNearestProgramIndex_PrefersProgramContainingTheFocusedTime()
    {
        var day = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Local);
        var programs = new[]
        {
            new MobileEpgProgramInterval(day.AddHours(19), day.AddHours(19.5)),
            new MobileEpgProgramInterval(day.AddHours(19.5), day.AddHours(23.5))
        };

        var index = MobileEpgTimelineGeometry.FindNearestProgramIndex(
            programs,
            day.AddHours(20));

        Assert.Equal(1, index);
    }

    [Fact]
    public void CalculateBlock_KeepsVisualWidthSeparateFromMinimumHitWidth()
    {
        var windowStart = new DateTime(2026, 8, 8, 17, 30, 0, DateTimeKind.Local);
        var block = MobileEpgTimelineGeometry.CalculateBlock(
            windowStart,
            windowStart.AddMinutes(5),
            windowStart,
            windowStart.AddHours(8),
            pixelsPerMinute: 2.2,
            minimumWidth: 24);

        Assert.NotNull(block);
        Assert.Equal(11, block.Value.Width, precision: 6);
        Assert.Equal(24, block.Value.HitTargetWidth, precision: 6);

        var next = MobileEpgTimelineGeometry.CalculateBlock(
            windowStart.AddMinutes(5),
            windowStart.AddMinutes(10),
            windowStart,
            windowStart.AddHours(8),
            pixelsPerMinute: 2.2,
            minimumWidth: 24);

        Assert.NotNull(next);
        Assert.True(
            block.Value.Left + block.Value.Width <= next.Value.Left,
            "Visual program blocks must not overlap their real timeline intervals.");
    }

    [Fact]
    public void FindNextPopulatedRowIndex_SkipsChannelsWithoutPrograms()
    {
        var index = MobileEpgTimelineGeometry.FindNextPopulatedRowIndex(
            rowCount: 4,
            currentIndex: 0,
            direction: 1,
            hasPrograms: rowIndex => new[] { 2, 0, 0, 1 }[rowIndex] > 0);

        Assert.Equal(3, index);
    }
}
