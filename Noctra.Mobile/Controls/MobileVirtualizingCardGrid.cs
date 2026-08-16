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
using Noctra.Core.Advertising;
using Noctra.Core.Collections;
using Noctra.Diagnostics;
using Noctra.Mobile.Services;

namespace Noctra.Mobile.Controls;

public enum MobileCardGridKind
{
    Live,
    Vod,
    Series,
    ContinueWatching
}

public sealed record MobileCardGridRow(
    IReadOnlyList<object> Items,
    MobileNativeAdSlot? AdSlot = null)
{
    public bool IsNativeAd => AdSlot.HasValue;
}

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
    private const int ResumeRecoveryAttempts = 3;
    private const int ResumeRecoveryDelayMilliseconds = 50;

    public static readonly StyledProperty<IEnumerable?> SourceItemsProperty =
        AvaloniaProperty.Register<MobileVirtualizingCardGrid, IEnumerable?>(nameof(SourceItems));

    public static readonly StyledProperty<MobileCardGridKind> CardKindProperty =
        AvaloniaProperty.Register<MobileVirtualizingCardGrid, MobileCardGridKind>(nameof(CardKind));

    public static readonly StyledProperty<AdPlacement> AdPlacementProperty =
        AvaloniaProperty.Register<MobileVirtualizingCardGrid, AdPlacement>(
            nameof(AdPlacement),
            AdPlacement.None);

    private readonly string _adOwnerKey = $"grid:{Guid.NewGuid():N}";
    private readonly AdAwareIncrementalRowCollection<object, MobileCardGridRow> _rowCollection;
    private readonly Queue<PendingAppend> _pendingAppends = new();
    private readonly object _pendingAppendsLock = new();
    private INotifyCollectionChanged? _observableSource;
    private int _rebuildQueued;
    private int _fullRebuildRequired;
    private int _resumeRecoveryVersion;
    private static long _gridResumeRequested;
    private static long _gridResumeSkippedInactive;
    private static long _gridResumeAttempted;
    private static long _gridResumeCompleted;
    private static long _gridResumeGenerationCancelled;
    private int _columns;
    private double _cardWidth;
    private double _lastStableWidth = FallbackAvailableWidth;
    private bool _lifecycleSubscribed;
    private IMobileAdvertisingService? _advertisingService;

    protected override Type StyleKeyOverride => typeof(ListBox);

    static MobileVirtualizingCardGrid()
    {
        SourceItemsProperty.Changed.AddClassHandler<MobileVirtualizingCardGrid>(
            (control, args) => control.OnSourceItemsChanged(args.OldValue as IEnumerable, args.NewValue as IEnumerable));
        CardKindProperty.Changed.AddClassHandler<MobileVirtualizingCardGrid>(
            (control, _) => control.QueueFullRebuild());
        AdPlacementProperty.Changed.AddClassHandler<MobileVirtualizingCardGrid>(
            (control, _) => control.QueueFullRebuild());
    }

    public MobileVirtualizingCardGrid()
    {
        _rowCollection = new AdAwareIncrementalRowCollection<object, MobileCardGridRow>(
            items => new MobileCardGridRow(items),
            ordinal => new MobileCardGridRow(
                Array.Empty<object>(),
                new MobileNativeAdSlot(_adOwnerKey, AdPlacement, ordinal)));

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

    public AdPlacement AdPlacement
    {
        get => GetValue(AdPlacementProperty);
        set => SetValue(AdPlacementProperty, value);
    }

    /// <summary>
    /// Revalidates layout after Android recreates or reconnects the render surface.
    /// Transient resume widths are ignored and retried for a bounded number of frames.
    /// </summary>
    public void RefreshAfterResume()
    {
        MarkCounter("GridResumeRequested", ref _gridResumeRequested);
        if (!IsResumeRecoveryEligibleOnUiThread())
        {
            MarkCounter("GridResumeSkippedInactive", ref _gridResumeSkippedInactive);
            return;
        }

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
        MobileAppLifecycle.Paused += OnAppPaused;
        _advertisingService = MobileAdvertisingServices.TryGet();
        if (_advertisingService is not null)
        {
            _advertisingService.EligibilityChanged -= AdvertisingService_EligibilityChanged;
            _advertisingService.EligibilityChanged += AdvertisingService_EligibilityChanged;
        }

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
        MobileAppLifecycle.Paused -= OnAppPaused;
        if (_advertisingService is not null)
        {
            _advertisingService.EligibilityChanged -= AdvertisingService_EligibilityChanged;
            _advertisingService.ReleaseOwner(_adOwnerKey);
            _advertisingService = null;
        }

        _lifecycleSubscribed = false;
        Interlocked.Increment(ref _resumeRecoveryVersion);
        MarkCounter("GridResumeGenerationCancelled", ref _gridResumeGenerationCancelled);
    }

    private void AdvertisingService_EligibilityChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(QueueFullRebuild);

    private void OnAppResumed(object? sender, EventArgs e)
        => RefreshAfterResume();

    private void OnAppPaused(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _resumeRecoveryVersion);
        MarkCounter("GridResumeGenerationCancelled", ref _gridResumeGenerationCancelled);
    }

    private static bool IsResumeRecoveryGenerationCurrent(int version, int currentVersion)
        => MobileAppLifecycle.IsForeground && version == currentVersion;

    private bool IsResumeRecoveryEligibleOnUiThread(int? version = null)
    {
        Debug.Assert(Dispatcher.UIThread.CheckAccess());
        if (!MobileAppLifecycle.IsForeground || !IsEffectivelyVisible || VisualRoot is null)
        {
            return false;
        }

        return !version.HasValue || version.Value == Volatile.Read(ref _resumeRecoveryVersion);
    }

    private async Task RecoverAfterResumeAsync(int version)
    {
        try
        {
            for (var attempt = 0; attempt < ResumeRecoveryAttempts; attempt++)
            {
                if (!IsResumeRecoveryGenerationCurrent(
                        version,
                        Volatile.Read(ref _resumeRecoveryVersion)))
                {
                    return;
                }

                if (attempt > 0)
                {
                    await Task.Delay(ResumeRecoveryDelayMilliseconds).ConfigureAwait(false);
                }

                if (!IsResumeRecoveryGenerationCurrent(
                        version,
                        Volatile.Read(ref _resumeRecoveryVersion)))
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
                if (!IsResumeRecoveryEligibleOnUiThread(version))
                {
                    MarkCounter("GridResumeSkippedInactive", ref _gridResumeSkippedInactive);
                    completion.TrySetResult(false);
                    return;
                }

                MarkCounter("GridResumeAttempted", ref _gridResumeAttempted);
                InvalidateMeasure();
                InvalidateArrange();
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
                MarkCounter("GridResumeCompleted", ref _gridResumeCompleted);
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, DispatcherPriority.Loaded);

        return completion.Task;
    }

    private static void MarkCounter(string name, ref long counter)
    {
        var value = Interlocked.Increment(ref counter);
        PerformanceTrace.Mark(name, value);
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
                    _rowCollection.TryAppend(
                        append.StartingIndex,
                        append.Items,
                        _columns,
                        GetAdAnchors()))
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
        _rowCollection.Rebuild(items, _columns, GetAdAnchors());
        return true;
    }

    private IReadOnlyList<int> GetAdAnchors()
    {
        var service = _advertisingService ??= MobileAdvertisingServices.TryGet();
        PerformanceTrace.Mark("Ads.CanServe",
            service?.CanServeAds == true ? 1 : 0);
        PerformanceTrace.Mark("Ads.Placement", (int)AdPlacement);
        if (service is null ||
            !service.CanServeAds ||
            AdPlacement == AdPlacement.None)
        {
            return Array.Empty<int>();
        }

        var policy = service.Options.GetNative(AdPlacement);
        var anchors = AdPlacementPlanner.GetContentAnchors(_columns, policy);
        PerformanceTrace.Mark("Ads.Anchors", anchors.Count);
        if (anchors.Count > 0)
        {
            service.PrimeNative(_adOwnerKey, AdPlacement, anchors.Count);
        }

        return anchors;
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
        if (row?.AdSlot is { } adSlot)
        {
            panel.ShowAd(adSlot);
            return;
        }

        panel.ShowCards();
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
        private readonly MobileNativeAdHost _adHost = new();

        public MobileCardGridRowControl(MobileVirtualizingCardGrid owner)
        {
            _owner = owner;
            RowPresenter = new MobileCardRowPresenter();
            Content = RowPresenter;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        public MobileCardRowPresenter RowPresenter { get; }

        public void ShowCards()
        {
            _adHost.Clear();
            Content = RowPresenter;
        }

        public void ShowAd(MobileNativeAdSlot slot)
        {
            _adHost.Bind(slot);
            Content = _adHost;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            _owner.PopulateRow(this, DataContext as MobileCardGridRow);
        }
    }
}
