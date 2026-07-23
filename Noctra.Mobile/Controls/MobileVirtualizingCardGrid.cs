using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Noctra.Core.Collections;
using Noctra.Mobile.Services;

namespace Noctra.Mobile.Controls;

public enum MobileCardGridKind
{
    Live,
    Vod,
    Series,
    ContinueWatching
}

public sealed record MobileCardGridRow(IReadOnlyList<object> Items);

/// <summary>
/// Turns a responsive card grid into virtualized rows. Avalonia's built-in WrapPanel
/// realizes every child, while VirtualizingStackPanel can recycle rows outside the
/// effective viewport. Each realized row owns only the small number of cards that fit.
/// </summary>
public sealed class MobileVirtualizingCardGrid : ListBox
{
    private const double CardGap = 16;
    private const double FallbackAvailableWidth = 720;
    private const double MinimumStableWidth = 120;
    private const int ResumeRecoveryAttempts = 8;
    private const int ResumeRecoveryDelayMilliseconds = 50;

    public static readonly StyledProperty<IEnumerable?> SourceItemsProperty =
        AvaloniaProperty.Register<MobileVirtualizingCardGrid, IEnumerable?>(nameof(SourceItems));

    public static readonly StyledProperty<MobileCardGridKind> CardKindProperty =
        AvaloniaProperty.Register<MobileVirtualizingCardGrid, MobileCardGridKind>(nameof(CardKind));

    private readonly IncrementalRowCollection<object, MobileCardGridRow> _rowCollection =
        new(items => new MobileCardGridRow(items));
    private readonly Queue<PendingAppend> _pendingAppends = new();
    private readonly object _pendingAppendsLock = new();
    private INotifyCollectionChanged? _observableSource;
    private int _rebuildQueued;
    private int _fullRebuildRequired;
    private int _resumeRecoveryVersion;
    private int _columns;
    private double _cardWidth;
    private double _lastStableWidth = FallbackAvailableWidth;
    private bool _lifecycleSubscribed;

    protected override Type StyleKeyOverride => typeof(ListBox);

    static MobileVirtualizingCardGrid()
    {
        SourceItemsProperty.Changed.AddClassHandler<MobileVirtualizingCardGrid>(
            (control, args) => control.OnSourceItemsChanged(args.OldValue as IEnumerable, args.NewValue as IEnumerable));
        CardKindProperty.Changed.AddClassHandler<MobileVirtualizingCardGrid>(
            (control, _) => control.QueueFullRebuild());
    }

    public MobileVirtualizingCardGrid()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = null;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        ItemsSource = _rowCollection.Rows;
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel
        {
            CacheLength = 0.5
        });
        ItemTemplate = new FuncDataTemplate<MobileCardGridRow>(
            (row, _) => row is null
                ? null
                : new MobileCardGridRowControl(this) { DataContext = row },
            supportsRecycling: true);
        SelectionChanged += ClearTransientSelection;
        SizeChanged += (_, _) => QueueRebuildIfMetricsChanged();
        AttachedToVisualTree += (_, _) => SubscribeToLifecycle();
        DetachedFromVisualTree += (_, _) => UnsubscribeFromLifecycle();
        AddHandler(ScrollViewer.ScrollChangedEvent, OnInnerScrollChanged);
    }

    public event EventHandler<ScrollChangedEventArgs>? ScrollChanged;

    public IEnumerable? SourceItems
    {
        get => GetValue(SourceItemsProperty);
        set => SetValue(SourceItemsProperty, value);
    }

    public MobileCardGridKind CardKind
    {
        get => GetValue(CardKindProperty);
        set => SetValue(CardKindProperty, value);
    }

    /// <summary>
    /// Revalidates layout after Android recreates or reconnects the render surface.
    /// Transient resume widths are ignored and retried for a bounded number of frames.
    /// </summary>
    public void RefreshAfterResume()
    {
        // Force a full rebuild even if the width later returns to the same value.
        // Without this, a timeout (350 ms) followed by the same width would skip
        // rebuild and leave a corrupted or blank visual tree.
        Interlocked.Exchange(ref _fullRebuildRequired, 1);

        var version = Interlocked.Increment(ref _resumeRecoveryVersion);
        _ = RecoverAfterResumeAsync(version);
    }

    private void SubscribeToLifecycle()
    {
        if (_lifecycleSubscribed)
        {
            return;
        }

        MobileAppLifecycle.Resumed += OnAppResumed;
        _lifecycleSubscribed = true;
        QueueFullRebuild();
    }

    private void UnsubscribeFromLifecycle()
    {
        if (!_lifecycleSubscribed)
        {
            return;
        }

        MobileAppLifecycle.Resumed -= OnAppResumed;
        _lifecycleSubscribed = false;
        Interlocked.Increment(ref _resumeRecoveryVersion);
    }

    private void OnAppResumed(object? sender, EventArgs e)
        => RefreshAfterResume();

    private async Task RecoverAfterResumeAsync(int version)
    {
        try
        {
            for (var attempt = 0; attempt < ResumeRecoveryAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(ResumeRecoveryDelayMilliseconds).ConfigureAwait(false);
                }

                if (version != Volatile.Read(ref _resumeRecoveryVersion))
                {
                    return;
                }

                if (await TryRepairAfterResumeAsync(version).ConfigureAwait(false))
                {
                    return;
                }
            }

            Debug.WriteLine("[Noctra] Resume layout recovery timed out before a stable width was observed.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Noctra] Resume layout recovery failed: {ex}");
        }
    }

    private Task<bool> TryRepairAfterResumeAsync(int version)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (version != Volatile.Read(ref _resumeRecoveryVersion) || VisualRoot is null)
                {
                    completion.TrySetResult(false);
                    return;
                }

                InvalidateLayoutChain();
                if (!TryGetStableAvailableWidth(out _))
                {
                    completion.TrySetResult(false);
                    return;
                }

                // A full rebuild repopulates every realized row even when the final
                // dimensions match the pre-suspend dimensions. This heals recycled
                // controls that were arranged while Android reported a transient size.
                ClearPendingAppends();
                if (!TryRebuildRows())
                {
                    Interlocked.Exchange(ref _fullRebuildRequired, 1);
                    completion.TrySetResult(false);
                    return;
                }

                Interlocked.Exchange(ref _fullRebuildRequired, 0);
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, DispatcherPriority.Render);

        return completion.Task;
    }

    private void InvalidateLayoutChain()
    {
        for (Control? control = this; control is not null; control = control.Parent as Control)
        {
            control.InvalidateMeasure();
            control.InvalidateArrange();
        }
    }

    private void OnSourceItemsChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (_observableSource != null)
        {
            _observableSource.CollectionChanged -= Source_CollectionChanged;
        }

        _observableSource = newValue as INotifyCollectionChanged;
        if (_observableSource != null)
        {
            _observableSource.CollectionChanged += Source_CollectionChanged;
        }

        QueueFullRebuild();
    }

    private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems is { Count: > 0 } &&
            e.NewStartingIndex >= 0)
        {
            var items = e.NewItems
                .Cast<object?>()
                .Where(item => item != null)
                .Cast<object>()
                .ToArray();
            if (items.Length == e.NewItems.Count)
            {
                lock (_pendingAppendsLock)
                {
                    _pendingAppends.Enqueue(new PendingAppend(e.NewStartingIndex, items));
                }

                QueueRefresh();
                return;
            }
        }

        QueueFullRebuild();
    }

    private void OnInnerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer scrollViewer)
        {
            ScrollChanged?.Invoke(scrollViewer, e);
        }
    }

    private void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedIndex >= 0)
        {
            SelectedIndex = -1;
        }
    }

    protected override bool ShouldTriggerSelection(Visual source, PointerEventArgs e)
        => false;

    protected override bool ShouldTriggerSelection(Visual source, KeyEventArgs e)
        => false;

    protected override void PrepareContainerForItemOverride(Control element, object? item, int index)
    {
        base.PrepareContainerForItemOverride(element, item, index);
        if (element is ListBoxItem container)
        {
            container.Padding = new Thickness(0);
            container.Margin = new Thickness(0);
            container.Background = null;
            container.BorderThickness = new Thickness(0);
            container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            container.Focusable = false;
        }
    }

    private void QueueRebuildIfMetricsChanged()
    {
        if (!TryGetStableAvailableWidth(out var availableWidth))
        {
            return;
        }

        var metrics = CalculateMetrics(availableWidth, CardKind);
        if (Volatile.Read(ref _fullRebuildRequired) == 1 ||
            metrics.Columns != _columns ||
            Math.Abs(metrics.CardWidth - _cardWidth) > 8)
        {
            QueueFullRebuild();
        }
    }

    private void QueueFullRebuild()
    {
        Interlocked.Exchange(ref _fullRebuildRequired, 1);
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _rebuildQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _rebuildQueued, 0);
            var requiresFullRebuild = Interlocked.Exchange(ref _fullRebuildRequired, 0) == 1;
            if (requiresFullRebuild)
            {
                ClearPendingAppends();
                if (!TryRebuildRows())
                {
                    Interlocked.Exchange(ref _fullRebuildRequired, 1);
                }

                return;
            }

            while (TryDequeuePendingAppend(out var append))
            {
                if (_columns > 0 &&
                    _cardWidth >= 2 &&
                    _rowCollection.TryAppend(append.StartingIndex, append.Items, _columns))
                {
                    continue;
                }

                ClearPendingAppends();
                if (!TryRebuildRows())
                {
                    Interlocked.Exchange(ref _fullRebuildRequired, 1);
                }

                return;
            }
        }, DispatcherPriority.Loaded);
    }

    private bool TryRebuildRows()
    {
        if (!TryGetStableAvailableWidth(out var availableWidth))
        {
            return false;
        }

        var metrics = CalculateMetrics(availableWidth, CardKind);
        _columns = metrics.Columns;
        _cardWidth = metrics.CardWidth;

        var items = SourceItems?
            .Cast<object?>()
            .Where(item => item != null)
            .Cast<object>() ?? Enumerable.Empty<object>();
        _rowCollection.Rebuild(items, _columns);
        return true;
    }

    private bool TryDequeuePendingAppend(out PendingAppend append)
    {
        lock (_pendingAppendsLock)
        {
            return _pendingAppends.TryDequeue(out append!);
        }
    }

    private void ClearPendingAppends()
    {
        lock (_pendingAppendsLock)
        {
            _pendingAppends.Clear();
        }
    }

    private void PopulateRow(MobileCardGridRowControl panel, MobileCardGridRow? row)
    {
        panel.RowPresenter.Populate(
            CardKind,
            MobileCardPresentationMode.Standard,
            _columns,
            _cardWidth,
            row?.Items ?? Array.Empty<object>());
    }

    private bool TryGetStableAvailableWidth(out double availableWidth)
    {
        var currentWidth = Bounds.Width;
        if (VisualRoot is null ||
            !double.IsFinite(currentWidth) ||
            currentWidth < MinimumStableWidth)
        {
            availableWidth = _lastStableWidth;
            return false;
        }

        _lastStableWidth = currentWidth;
        availableWidth = currentWidth;
        return true;
    }

    private static GridMetrics CalculateMetrics(double availableWidth, MobileCardGridKind kind)
    {
        if (!double.IsFinite(availableWidth) || availableWidth < 2)
        {
            availableWidth = 2;
        }

        var profile = kind switch
        {
            MobileCardGridKind.Live => new GridProfile(220, 410, 4),
            MobileCardGridKind.ContinueWatching => new GridProfile(220, 410, 4),
            _ => new GridProfile(150, 180, 6)
        };
        var columns = Math.Max(
            1,
            (int)Math.Floor((availableWidth + CardGap) / (profile.MinWidth + CardGap)));
        columns = Math.Min(columns, profile.MaxColumns);

        var width = Math.Floor((availableWidth - CardGap * (columns - 1)) / columns);
        width = Math.Clamp(width, 2, profile.MaxWidth);
        var roundedWidth = Math.Floor(width / 2) * 2;
        return new GridMetrics(columns, Math.Max(2, roundedWidth));
    }

    private readonly record struct GridProfile(double MinWidth, double MaxWidth, int MaxColumns);
    private readonly record struct GridMetrics(int Columns, double CardWidth);
    private sealed record PendingAppend(int StartingIndex, IReadOnlyList<object> Items);

    private sealed class MobileCardGridRowControl : ContentControl
    {
        private readonly MobileVirtualizingCardGrid _owner;

        public MobileCardGridRowControl(MobileVirtualizingCardGrid owner)
        {
            _owner = owner;
            RowPresenter = new MobileCardRowPresenter();
            Content = RowPresenter;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        public MobileCardRowPresenter RowPresenter { get; }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            _owner.PopulateRow(this, DataContext as MobileCardGridRow);
        }
    }
}
