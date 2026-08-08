using System;
using System.Collections.Generic;

namespace Noctra.Mobile.ViewModels;

/// <summary>
/// Pure timeline calculations owned by the mobile EPG presentation layer.
/// </summary>
public static class MobileEpgTimelineGeometry
{
    public static DateTime SnapWindowStart(DateTime localNow, TimeSpan pastWindow)
    {
        var unsnapped = localNow - pastWindow;
        var snappedMinute = unsnapped.Minute < 30 ? 0 : 30;
        return new DateTime(
            unsnapped.Year,
            unsnapped.Month,
            unsnapped.Day,
            unsnapped.Hour,
            snappedMinute,
            0,
            unsnapped.Kind);
    }

    public static MobileEpgBlockGeometry? CalculateBlock(
        DateTime programStart,
        DateTime programEnd,
        DateTime windowStart,
        DateTime windowEnd,
        double pixelsPerMinute,
        double minimumWidth)
    {
        if (programEnd <= programStart
            || windowEnd <= windowStart
            || programEnd <= windowStart
            || programStart >= windowEnd)
        {
            return null;
        }

        var clippedStart = programStart < windowStart ? windowStart : programStart;
        var clippedEnd = programEnd > windowEnd ? windowEnd : programEnd;
        var left = (clippedStart - windowStart).TotalMinutes * pixelsPerMinute;
        var visibleWidth = (clippedEnd - clippedStart).TotalMinutes * pixelsPerMinute;

        return new MobileEpgBlockGeometry(
            left,
            Math.Max(minimumWidth, visibleWidth),
            programStart < windowStart,
            programEnd > windowEnd,
            clippedStart,
            clippedEnd);
    }

    public static double CalculateProgressWidth(
        MobileEpgBlockGeometry block,
        DateTime currentTime)
    {
        if (block.VisibleEnd <= block.VisibleStart)
        {
            return 0;
        }

        var progress = (currentTime - block.VisibleStart).TotalSeconds
                       / (block.VisibleEnd - block.VisibleStart).TotalSeconds;
        return block.Width * Math.Clamp(progress, 0, 1);
    }

    public static int FindNearestProgramIndex(
        IReadOnlyList<MobileEpgProgramInterval> programs,
        DateTime anchor)
    {
        if (programs.Count == 0)
        {
            return -1;
        }

        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < programs.Count; index++)
        {
            var program = programs[index];
            if (anchor >= program.Start && anchor < program.End)
            {
                return index;
            }

            var nearestEdge = anchor < program.Start ? program.Start : program.End;
            var distance = Math.Abs((nearestEdge - anchor).TotalSeconds);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    public static int FindNextPopulatedRowIndex(
        int rowCount,
        int currentIndex,
        int direction,
        Func<int, bool> hasPrograms)
    {
        if (direction == 0)
        {
            return -1;
        }

        var step = Math.Sign(direction);
        for (var index = currentIndex + step;
             index >= 0 && index < rowCount;
             index += step)
        {
            if (hasPrograms(index))
            {
                return index;
            }
        }

        return -1;
    }
}

public readonly record struct MobileEpgBlockGeometry(
    double Left,
    double Width,
    bool IsClippedLeft,
    bool IsClippedRight,
    DateTime VisibleStart,
    DateTime VisibleEnd);

public readonly record struct MobileEpgProgramInterval(DateTime Start, DateTime End);
