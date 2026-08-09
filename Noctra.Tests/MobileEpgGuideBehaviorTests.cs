using Noctra.Mobile.ViewModels;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Tests;

/// <summary>
/// Runtime behavior coverage for the EPG presentation state. The existing
/// panel contract tests intentionally remain architecture guards; these tests
/// exercise the same selection/rebuild operations used by the mobile panel.
/// </summary>
public sealed class MobileEpgGuideBehaviorTests
{
    [Fact]
    public void FindVertical_PreservesTheCurrentTimeAnchorAcrossPopulatedRows()
    {
        var context = new PlayerTestContext();
        var now = LocalDate(2026, 8, 8, 12, 0);
        var sourceRows = CreateRows(now);
        context.VM.EpgRows.ReplaceAll(sourceRows);

        var guide = CreateGuide(context, now);
        var current = guide.Rows[0].Programs[0];

        guide.Select(current);

        var target = guide.FindVertical(current, direction: 1);

        Assert.NotNull(target);
        Assert.Equal(22, target!.Program.Id);
        Assert.True(
            target.Program.StartTime.ToLocalTime() <= current.AnchorTime
            && current.AnchorTime < target.Program.EndTime.ToLocalTime(),
            "Vertical focus should choose the program containing the current time anchor.");
    }

    [Fact]
    public void Rebuild_RestoresSelectionAfterRowsAreRecreated()
    {
        var context = new PlayerTestContext();
        var now = LocalDate(2026, 8, 8, 12, 0);
        context.VM.EpgRows.ReplaceAll(CreateRows(now));

        var guide = CreateGuide(context, now);
        var selected = guide.Rows[2].Programs[1];
        guide.Select(selected);

        // This mirrors the collection reset caused by a virtualized guide
        // refresh: row and program objects are new instances, but identities
        // and time ranges remain stable.
        context.VM.EpgRows.ReplaceAll(CreateRows(now));
        guide.Rebuild(now);

        Assert.NotNull(guide.SelectedProgram);
        Assert.Equal(selected.Program.Id, guide.SelectedProgram!.Program.Id);
        Assert.Equal(selected.Channel.Id, guide.SelectedChannel!.Id);
        Assert.True(guide.SelectedProgram.IsSelected);
        Assert.Single(guide.Rows[2].Programs.Where(item => item.IsSelected));
    }

    [Fact]
    public void Rebuild_PreservesSelectionWhenSwitchingBetweenTouchAndRemoteScale()
    {
        var context = new PlayerTestContext();
        var now = LocalDate(2026, 8, 8, 12, 0);
        context.VM.EpgRows.ReplaceAll(CreateRows(now));

        var guide = CreateGuide(context, now);
        var selected = guide.Rows[2].Programs[1];
        guide.Select(selected);

        // MobilePlayerEpgPanel.SetRemoteMode uses this configure/rebuild
        // sequence whenever input changes between touch and a remote/keyboard.
        guide.ConfigureScale(now, availableTimelineWidth: 720, remoteMode: true);
        guide.Rebuild(now);
        Assert.Equal(selected.Program.Id, guide.SelectedProgram?.Program.Id);
        Assert.Equal(80, guide.RowHeight);

        guide.ConfigureScale(now, availableTimelineWidth: 900, remoteMode: false);
        guide.Rebuild(now);
        Assert.Equal(selected.Program.Id, guide.SelectedProgram?.Program.Id);
        Assert.Equal(64, guide.RowHeight);
    }

    private static MobileEpgGuidePresentation CreateGuide(PlayerTestContext context, DateTime now)
    {
        var guide = new MobileEpgGuidePresentation(context.VM, key => key);
        guide.ConfigureWindow(now, availableTimelineWidth: 900, remoteMode: false);
        guide.Rebuild(now);
        return guide;
    }

    private static IReadOnlyList<EpgGuideRow> CreateRows(DateTime now)
    {
        var firstChannel = new Channel { Id = 1, Name = "First", TvgId = "first" };
        var emptyChannel = new Channel { Id = 2, Name = "Empty", TvgId = "empty" };
        var targetChannel = new Channel { Id = 3, Name = "Target", TvgId = "target" };

        return new[]
        {
            new EpgGuideRow
            {
                Channel = firstChannel,
                Programs = new[]
                {
                    Program(11, firstChannel, now.AddHours(-1), 120)
                }
            },
            new EpgGuideRow
            {
                Channel = emptyChannel,
                Programs = Array.Empty<EpgProgram>()
            },
            new EpgGuideRow
            {
                Channel = targetChannel,
                Programs = new[]
                {
                    Program(21, targetChannel, now.AddHours(-2), 90),
                    Program(22, targetChannel, now.AddMinutes(-30), 120),
                    Program(23, targetChannel, now.AddMinutes(90), 90)
                }
            }
        };
    }

    private static EpgProgram Program(
        int id,
        Channel channel,
        DateTime localStart,
        int durationMinutes)
    {
        var startUtc = localStart.ToUniversalTime();
        return new EpgProgram
        {
            Id = id,
            ChannelId = channel.TvgId!,
            Title = $"Program {id}",
            StartTime = startUtc,
            EndTime = startUtc.AddMinutes(durationMinutes)
        };
    }

    private static DateTime LocalDate(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Local);
}
