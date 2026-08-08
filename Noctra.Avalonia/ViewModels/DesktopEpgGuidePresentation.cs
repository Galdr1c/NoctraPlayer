using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Avalonia.ViewModels;

/// <summary>
/// Desktop-only geometry for the legacy overlay timeline. Keeping these values
/// here prevents viewport and pixel concerns from leaking back into Noctra.Core.
/// </summary>
public sealed class DesktopEpgGuidePresentation
{
    public const double PixelsPerMinute = 3;
    public const double RowHeight = 68;

    private static readonly TimeSpan PastWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan FutureWindow = TimeSpan.FromHours(6);

    public DateTime WindowStart { get; init; }

    public DateTime WindowEnd { get; init; }

    public double CanvasWidth { get; init; }

    public double NowLineLeft { get; init; }

    public int CurrentRowIndex { get; init; } = -1;

    public IReadOnlyList<DesktopEpgPanelRow> Rows { get; init; } = [];

    public static DesktopEpgGuidePresentation CreateWindow(DateTime now)
    {
        var unsnappedStart = now - PastWindow;
        var windowStart = new DateTime(
            unsnappedStart.Year,
            unsnappedStart.Month,
            unsnappedStart.Day,
            unsnappedStart.Hour,
            unsnappedStart.Minute < 30 ? 0 : 30,
            0,
            unsnappedStart.Kind);
        var windowEnd = windowStart + PastWindow + FutureWindow;
        var canvasWidth = (windowEnd - windowStart).TotalMinutes * PixelsPerMinute;

        return new DesktopEpgGuidePresentation
        {
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            CanvasWidth = canvasWidth,
            NowLineLeft = Math.Clamp(
                (now - windowStart).TotalMinutes * PixelsPerMinute,
                0,
                canvasWidth)
        };
    }

    public static DesktopEpgGuidePresentation Build(
        IEnumerable<EpgGuideRow> guideRows,
        Channel? currentChannel,
        DateTime now)
    {
        var window = CreateWindow(now);
        var rows = new List<DesktopEpgPanelRow>();
        var currentRowIndex = -1;

        foreach (var guideRow in guideRows)
        {
            var isCurrentChannel = currentChannel?.Id == guideRow.Channel.Id;
            if (isCurrentChannel)
                currentRowIndex = rows.Count;

            var blocks = guideRow.Programs
                .Select(program => CreateBlock(program, window, now))
                .OfType<DesktopEpgProgramBlock>()
                .OrderBy(block => block.PixelLeft)
                .ToList();

            rows.Add(new DesktopEpgPanelRow
            {
                Channel = guideRow.Channel,
                Blocks = blocks,
                IsCurrentChannel = isCurrentChannel,
                NowLineLeft = window.NowLineLeft
            });
        }

        return new DesktopEpgGuidePresentation
        {
            WindowStart = window.WindowStart,
            WindowEnd = window.WindowEnd,
            CanvasWidth = window.CanvasWidth,
            NowLineLeft = window.NowLineLeft,
            CurrentRowIndex = currentRowIndex,
            Rows = rows
        };
    }

    private static DesktopEpgProgramBlock? CreateBlock(
        EpgProgram program,
        DesktopEpgGuidePresentation window,
        DateTime now)
    {
        var start = program.StartTime.ToLocalTime();
        var end = program.EndTime.ToLocalTime();
        if (end <= start || end <= window.WindowStart || start >= window.WindowEnd)
            return null;

        var visibleStart = start < window.WindowStart ? window.WindowStart : start;
        var visibleEnd = end > window.WindowEnd ? window.WindowEnd : end;
        var left = (visibleStart - window.WindowStart).TotalMinutes * PixelsPerMinute;
        var width = Math.Max(2, (visibleEnd - visibleStart).TotalMinutes * PixelsPerMinute);

        return new DesktopEpgProgramBlock
        {
            Program = program,
            PixelLeft = left,
            PixelWidth = width,
            IsCurrentProgram = start <= now && end > now,
            IsClippedLeft = start < window.WindowStart,
            IsPast = end <= now
        };
    }
}

public sealed class DesktopEpgProgramBlock
{
    public EpgProgram Program { get; init; } = null!;
    public double PixelLeft { get; init; }
    public double PixelWidth { get; init; }
    public bool IsCurrentProgram { get; init; }
    public bool IsClippedLeft { get; init; }
    public bool IsPast { get; init; }
    public double ProgressPixelWidth =>
        IsCurrentProgram ? Program.ProgressPercentage / 100.0 * PixelWidth : 0;
    public double TitleTextWidth => Math.Max(0, PixelWidth - 14);
    public bool IsStrip => PixelWidth < 6;
    public bool IsCompact => PixelWidth >= 6 && PixelWidth < 48;
    public bool HasReadableText => PixelWidth >= 48;
    public double VisualHeight => IsStrip ? 28 : 52;
}

public sealed class DesktopEpgPanelRow
{
    public Channel Channel { get; init; } = null!;
    public IReadOnlyList<DesktopEpgProgramBlock> Blocks { get; init; } = [];
    public bool IsCurrentChannel { get; init; }
    public double NowLineLeft { get; init; }
    public bool HasEpgData => Blocks.Count > 0;
}
