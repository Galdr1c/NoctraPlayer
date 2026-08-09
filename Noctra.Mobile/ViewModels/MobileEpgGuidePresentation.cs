using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Core.Collections;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.ViewModels;

/// <summary>
/// Viewport- and input-aware EPG presentation state. All pixel geometry stays
/// here in Noctra.Mobile rather than leaking into the Core view model.
/// </summary>
public sealed partial class MobileEpgGuidePresentation : ObservableObject
{
    private static readonly TimeSpan PastWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan FutureWindow = TimeSpan.FromHours(6);

    private readonly PlayerViewModel _player;
    private readonly Func<string, string> _translate;

    public MobileEpgGuidePresentation(
        PlayerViewModel player,
        Func<string, string> translate)
    {
        _player = player;
        _translate = translate;
    }

    public BatchObservableCollection<MobileEpgRow> Rows { get; } = new();

    public ObservableCollection<MobileEpgTimeTick> TimeTicks { get; } = new();

    [ObservableProperty] private DateTime _windowStart;
    [ObservableProperty] private DateTime _windowEnd;
    [ObservableProperty] private double _pixelsPerMinute = 3;
    [ObservableProperty] private double _canvasWidth;
    [ObservableProperty] private double _nowLineLeft;
    [ObservableProperty] private bool _isNowLineVisible;
    [ObservableProperty] private double _rowHeight = 64;
    [ObservableProperty] private double _channelRailWidth = 132;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedChannelName))]
    [NotifyPropertyChangedFor(nameof(CanWatchSelectedChannel))]
    private Channel? _selectedChannel;
    [ObservableProperty] private MobileEpgProgramItem? _selectedProgram;

    public bool HasSelection => SelectedProgram is not null;
    public string SelectedChannelName => SelectedChannel?.Name ?? string.Empty;
    public string SelectedTitle => SelectedProgram?.Program.Title ?? _translate("Player.Epg.SelectProgram");
    public string SelectedTimeRange => SelectedProgram?.TimeRange ?? string.Empty;
    public string SelectedDescription => string.IsNullOrWhiteSpace(SelectedProgram?.Program.Description)
        ? _translate("Player.Epg.NoDescription")
        : SelectedProgram.Program.Description!;
    public string SelectedCategory => SelectedProgram?.Program.Category ?? string.Empty;
    public bool HasSelectedCategory => !string.IsNullOrWhiteSpace(SelectedCategory);
    public bool CanWatchSelectedChannel => SelectedChannel is not null;
    public string WindowLabel => $"{WindowStart:HH:mm} – {WindowEnd:HH:mm}";
    public string SelectedTimingStatus => GetTimingStatus(SelectedProgram, DateTime.Now);

    public (DateTime Start, DateTime End) ConfigureWindow(
        DateTime localNow,
        double availableTimelineWidth,
        bool remoteMode)
    {
        WindowStart = MobileEpgTimelineGeometry.SnapWindowStart(localNow, PastWindow);
        WindowEnd = WindowStart + PastWindow + FutureWindow;
        ConfigureScale(localNow, availableTimelineWidth, remoteMode);
        OnPropertyChanged(nameof(WindowLabel));
        return (WindowStart, WindowEnd);
    }

    public void ConfigureScale(
        DateTime localNow,
        double availableTimelineWidth,
        bool remoteMode)
    {
        PixelsPerMinute = CalculatePixelsPerMinute(availableTimelineWidth, remoteMode);
        CanvasWidth = (WindowEnd - WindowStart).TotalMinutes * PixelsPerMinute;
        RowHeight = remoteMode ? 80 : 64;
        ChannelRailWidth = remoteMode ? 184 : 132;
        BuildTimeTicks();
        UpdateLive(localNow);
    }

    public void Rebuild(DateTime localNow)
    {
        var previousSelection = SelectedProgram is null
            ? null
            : CreateSelectionKey(SelectedProgram.Channel, SelectedProgram.Program);

        var rows = new List<MobileEpgRow>(_player.EpgRows.Count);
        foreach (var sourceRow in _player.EpgRows)
        {
            var row = new MobileEpgRow(
                sourceRow.Channel,
                sourceRow.Channel.Id == _player.CurrentChannel?.Id,
                CanvasWidth,
                RowHeight);

            foreach (var program in sourceRow.Programs)
            {
                var geometry = MobileEpgTimelineGeometry.CalculateBlock(
                    program.StartTime.ToLocalTime(),
                    program.EndTime.ToLocalTime(),
                    WindowStart,
                    WindowEnd,
                    PixelsPerMinute,
                    minimumWidth: 24);
                if (geometry is null)
                {
                    continue;
                }

                row.Programs.Add(new MobileEpgProgramItem(
                    sourceRow.Channel,
                    program,
                    geometry.Value,
                    row.IsPlayingChannel));
            }

            rows.Add(row);
        }

        Rows.ReplaceAll(rows);

        MobileEpgProgramItem? selection = null;
        if (previousSelection is not null)
        {
            selection = Rows
                .SelectMany(row => row.Programs)
                .FirstOrDefault(item => CreateSelectionKey(item.Channel, item.Program) == previousSelection);
        }

        selection ??= FindInitialSelection(localNow);
        Select(selection);
        if (selection is null)
        {
            SelectedChannel = Rows.FirstOrDefault(row => row.IsPlayingChannel)?.Channel
                              ?? Rows.FirstOrDefault()?.Channel;
        }
        UpdateLive(localNow);
    }

    public void RefreshPlayingChannel(DateTime localNow)
    {
        foreach (var row in Rows)
        {
            row.IsPlayingChannel = row.Channel.Id == _player.CurrentChannel?.Id;
            foreach (var program in row.Programs)
            {
                program.IsPlayingChannel = row.IsPlayingChannel;
            }
        }

        UpdateLive(localNow);
    }

    public void UpdateLive(DateTime localNow)
    {
        var nowUtc = localNow.ToUniversalTime();
        NowLineLeft = (localNow - WindowStart).TotalMinutes * PixelsPerMinute;
        IsNowLineVisible = localNow >= WindowStart && localNow <= WindowEnd;

        foreach (var item in Rows.SelectMany(row => row.Programs))
        {
            item.UpdateLive(nowUtc);
        }

        NotifySelectionChanged();
    }

    public void Select(MobileEpgProgramItem? item)
    {
        if (ReferenceEquals(SelectedProgram, item))
        {
            NotifySelectionChanged();
            return;
        }

        if (SelectedProgram is not null)
        {
            SelectedProgram.IsSelected = false;
        }

        SelectedProgram = item;
        if (SelectedProgram is not null)
        {
            SelectedChannel = SelectedProgram.Channel;
            SelectedProgram.IsSelected = true;
        }

        NotifySelectionChanged();
    }

    public MobileEpgProgramItem? SelectChannel(MobileEpgRow row, DateTime localNow)
    {
        SelectedChannel = row.Channel;
        var current = row.Programs.FirstOrDefault(item => item.IsCurrentProgram);
        if (current is not null)
        {
            Select(current);
            return current;
        }

        var intervals = row.Programs
            .Select(item => new MobileEpgProgramInterval(
                item.Program.StartTime.ToLocalTime(),
                item.Program.EndTime.ToLocalTime()))
            .ToList();
        var index = MobileEpgTimelineGeometry.FindNearestProgramIndex(intervals, localNow);
        var nearest = index >= 0 ? row.Programs[index] : null;
        Select(nearest);
        return nearest;
    }

    public MobileEpgProgramItem? FindHorizontal(MobileEpgProgramItem current, int direction)
    {
        var row = Rows.FirstOrDefault(candidate => candidate.Channel.Id == current.Channel.Id);
        if (row is null)
        {
            return null;
        }

        var index = row.Programs.IndexOf(current) + direction;
        return index >= 0 && index < row.Programs.Count ? row.Programs[index] : null;
    }

    public MobileEpgProgramItem? FindVertical(MobileEpgProgramItem current, int direction)
    {
        var rowIndex = Rows.IndexOf(Rows.First(row => row.Channel.Id == current.Channel.Id));
        var targetRowIndex = MobileEpgTimelineGeometry.FindNextPopulatedRowIndex(
            Rows.Count,
            rowIndex,
            direction,
            index => Rows[index].Programs.Count > 0);
        if (targetRowIndex < 0)
        {
            return null;
        }

        var targetRow = Rows[targetRowIndex];
        var intervals = targetRow.Programs
            .Select(item => new MobileEpgProgramInterval(
                item.Program.StartTime.ToLocalTime(),
                item.Program.EndTime.ToLocalTime()))
            .ToList();
        var index = MobileEpgTimelineGeometry.FindNearestProgramIndex(intervals, current.AnchorTime);
        return index >= 0 ? targetRow.Programs[index] : null;
    }

    public MobileEpgProgramItem? FindInitialSelection(DateTime localNow)
    {
        var playingRow = Rows.FirstOrDefault(row => row.IsPlayingChannel);
        if (playingRow is not null)
        {
            return SelectChannel(playingRow, localNow);
        }

        return Rows.SelectMany(row => row.Programs).FirstOrDefault();
    }

    private void BuildTimeTicks()
    {
        TimeTicks.Clear();
        for (var tick = WindowStart; tick <= WindowEnd; tick = tick.AddMinutes(30))
        {
            TimeTicks.Add(new MobileEpgTimeTick(
                (tick - WindowStart).TotalMinutes * PixelsPerMinute,
                tick.ToString("HH:mm", CultureInfo.CurrentCulture),
                tick.Minute == 0));
        }
    }

    private double CalculatePixelsPerMinute(double availableWidth, bool remoteMode)
    {
        var targetVisibleMinutes = remoteMode ? 180d : 150d;
        var adaptive = availableWidth <= 0 ? 3d : availableWidth / targetVisibleMinutes;
        return Math.Clamp(adaptive, remoteMode ? 3d : 2.2d, remoteMode ? 5d : 4d);
    }

    private string GetTimingStatus(MobileEpgProgramItem? item, DateTime localNow)
    {
        if (item is null)
        {
            return string.Empty;
        }

        var start = item.Program.StartTime.ToLocalTime();
        var end = item.Program.EndTime.ToLocalTime();
        if (localNow >= start && localNow < end)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((end - localNow).TotalMinutes));
            return string.Format(
                CultureInfo.CurrentCulture,
                _translate("Player.Epg.MinutesRemaining"),
                minutes);
        }

        if (localNow < start)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((start - localNow).TotalMinutes));
            return string.Format(
                CultureInfo.CurrentCulture,
                _translate("Player.Epg.StartsInMinutes"),
                minutes);
        }

        return _translate("Player.Epg.Ended");
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedChannel));
        OnPropertyChanged(nameof(SelectedChannelName));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedTimeRange));
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(HasSelectedCategory));
        OnPropertyChanged(nameof(CanWatchSelectedChannel));
        OnPropertyChanged(nameof(SelectedTimingStatus));
    }

    private static string CreateSelectionKey(Channel channel, EpgProgram program)
        => string.Join(
            '|',
            channel.Id.ToString(CultureInfo.InvariantCulture),
            program.ChannelId,
            program.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            program.EndTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            program.Title);
}

public sealed partial class MobileEpgRow : ObservableObject
{
    public MobileEpgRow(Channel channel, bool isPlayingChannel, double canvasWidth, double rowHeight)
    {
        Channel = channel;
        _isPlayingChannel = isPlayingChannel;
        CanvasWidth = canvasWidth;
        RowHeight = rowHeight;
    }

    public Channel Channel { get; }
    public ObservableCollection<MobileEpgProgramItem> Programs { get; } = new();
    public bool HasPrograms => Programs.Count > 0;
    public double CanvasWidth { get; }
    public double RowHeight { get; }

    [ObservableProperty] private bool _isPlayingChannel;
}

public sealed partial class MobileEpgProgramItem : ObservableObject
{
    private readonly MobileEpgBlockGeometry _geometry;

    public MobileEpgProgramItem(
        Channel channel,
        EpgProgram program,
        MobileEpgBlockGeometry geometry,
        bool isPlayingChannel)
    {
        Channel = channel;
        Program = program;
        _geometry = geometry;
        Left = geometry.Left;
        Width = geometry.Width;
        IsClippedLeft = geometry.IsClippedLeft;
        IsClippedRight = geometry.IsClippedRight;
        HitTargetWidth = geometry.HitTargetWidth;
        _isPlayingChannel = isPlayingChannel;
    }

    public Channel Channel { get; }
    public EpgProgram Program { get; }
    public double Left { get; }
    public double Width { get; }
    public double HitTargetWidth { get; }
    public bool IsClippedLeft { get; }
    public bool IsClippedRight { get; }
    public DateTime AnchorTime => Program.StartTime.ToLocalTime()
        + TimeSpan.FromTicks((Program.EndTime - Program.StartTime).Ticks / 2);
    public string TimeRange => $"{Program.StartTime.ToLocalTime():HH:mm} – {Program.EndTime.ToLocalTime():HH:mm}";
    public string AccessibilityName => $"{Channel.Name}, {Program.Title}, {TimeRange}";

    [ObservableProperty] private bool _isPlayingChannel;
    [ObservableProperty] private bool _isCurrentProgram;
    [ObservableProperty] private bool _isPlayingProgram;
    [ObservableProperty] private bool _isPast;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private double _progressWidth;

    public void UpdateLive(DateTime nowUtc)
    {
        IsCurrentProgram = nowUtc >= Program.StartTime && nowUtc < Program.EndTime;
        IsPlayingProgram = IsPlayingChannel && IsCurrentProgram;
        IsPast = Program.EndTime <= nowUtc;

        if (!IsCurrentProgram || Program.EndTime <= Program.StartTime)
        {
            ProgressWidth = 0;
            return;
        }

        ProgressWidth = MobileEpgTimelineGeometry.CalculateProgressWidth(
            _geometry,
            nowUtc.ToLocalTime());
    }
}

public sealed record MobileEpgTimeTick(double Left, string Label, bool IsHour);
