using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctra.Core.Collections;

namespace Noctra.Avalonia.Controls;

/// <summary>
/// Responsive desktop catalogue grid backed by virtualized rows. Unlike an
/// ItemsControl + WrapPanel, it does not instantiate every card in the catalogue.
/// </summary>
public sealed class DesktopVirtualizingCardGrid : ListBox
{
    private const double CardGap = 16;
    private const double FallbackAvailableWidth = 1180;
    private const double MinimumStableWidth = 240;

    public static readonly StyledProperty<IEnumerable?> SourceItemsProperty =
        AvaloniaProperty.Register<DesktopVirtualizingCardGrid, IEnumerable?>(nameof(SourceItems));

    public static readonly StyledProperty<DesktopCardGridKind> CardKindProperty =
        AvaloniaProperty.Register<DesktopVirtualizingCardGrid, DesktopCardGridKind>(nameof(CardKind));

    public static readonly StyledProperty<DesktopCardPresentationMode> PresentationModeProperty =
        AvaloniaProperty.Register<DesktopVirtualizingCardGrid, DesktopCardPresentationMode>(nameof(PresentationMode));

    private readonly IncrementalRowCollection<object, DesktopCardGridRow> _rows =
        new(items => new DesktopCardGridRow(items));
    private readonly Queue<PendingAppend> _pendingAppends = new();
    private readonly object _pendingLock = new();
    private INotifyCollectionChanged? _observableSource;
    private int _refreshQueued;
    private int _fullRebuildRequired;
    private int _rebuildRetryCount;
    private int _columns;
    private double _cardWidth;
    private double _lastStableWidth = FallbackAvailableWidth;

    protected override Type StyleKeyOverride => typeof(ListBox);

    static DesktopVirtualizingCardGrid()
    {
        SourceItemsProperty.Changed.AddClassHandler<DesktopVirtualizingCardGrid>(
            (control, args) => control.OnSourceItemsChanged(args.NewValue as IEnumerable));
        CardKindProperty.Changed.AddClassHandler<DesktopVirtualizingCardGrid>(
            (control, _) => control.QueueFullRebuild());
        PresentationModeProperty.Changed.AddClassHandler<DesktopVirtualizingCardGrid>(
            (control, _) => control.QueueFullRebuild());
    }

    public DesktopVirtualizingCardGrid()
    {
        Classes.Add("DesktopVirtualFeed");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = null;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        ItemsSource = _rows.Rows;
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 0.75 });
        ItemTemplate = new FuncDataTemplate<DesktopCardGridRow>(
            (row, _) => row is null ? null : new RowControl(this) { DataContext = row },
            supportsRecycling: true);
        SelectionChanged += ClearTransientSelection;
        SizeChanged += (_, _) => QueueRebuildIfMetricsChanged();
        AttachedToVisualTree += (_, _) => QueueFullRebuild();
        AddHandler(ScrollViewer.ScrollChangedEvent, OnInnerScrollChanged);
    }

    public event EventHandler<ScrollChangedEventArgs>? ScrollChanged;

    /// <summary>
    /// Returns source items held by realized rows. VirtualizingStackPanel's
    /// cache is intentionally included as a small overscan window.
    /// </summary>
    public IReadOnlyList<object> GetVisibleSourceItems()
        => this.GetVisualDescendants()
            .OfType<RowControl>()
            .SelectMany(row => (row.DataContext as DesktopCardGridRow)?.Items ?? Array.Empty<object>())
            .Distinct(ReferenceEqualityComparer.Instance)
            .ToArray();

    public IEnumerable? SourceItems
    {
        get => GetValue(SourceItemsProperty);
        set => SetValue(SourceItemsProperty, value);
    }

    public DesktopCardGridKind CardKind
    {
        get => GetValue(CardKindProperty);
        set => SetValue(CardKindProperty, value);
    }

    public DesktopCardPresentationMode PresentationMode
    {
        get => GetValue(PresentationModeProperty);
        set => SetValue(PresentationModeProperty, value);
    }

    private void OnSourceItemsChanged(IEnumerable? source)
    {
        if (_observableSource is not null)
        {
            _observableSource.CollectionChanged -= Source_CollectionChanged;
        }

        _observableSource = source as INotifyCollectionChanged;
        if (_observableSource is not null)
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
            var items = e.NewItems.Cast<object?>().Where(x => x is not null).Cast<object>().ToArray();
            if (items.Length == e.NewItems.Count)
            {
                lock (_pendingLock)
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

    protected override bool ShouldTriggerSelection(Visual source, PointerEventArgs e) => false;
    protected override bool ShouldTriggerSelection(Visual source, KeyEventArgs e) => false;

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
        if (!TryGetStableWidth(out var width))
        {
            return;
        }

        var metrics = CalculateMetrics(width, CardKind);
        if (metrics.Columns != _columns || Math.Abs(metrics.CardWidth - _cardWidth) > 8)
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
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            if (Interlocked.Exchange(ref _fullRebuildRequired, 0) == 1)
            {
                ClearPendingAppends();
                if (!TryRebuildRows())
                {
                    var retries = Interlocked.Increment(ref _rebuildRetryCount);
                    if (retries < 8)
                    {
                        Interlocked.Exchange(ref _fullRebuildRequired, 1);
                        Interlocked.Exchange(ref _refreshQueued, 0);
                        Dispatcher.UIThread.Post(() => QueueRefresh(), DispatcherPriority.Background);
                    }
                    else
                    {
                        Interlocked.Exchange(ref _rebuildRetryCount, 0);
                        Interlocked.Exchange(ref _fullRebuildRequired, 1);
                    }
                }
                else
                {
                    Interlocked.Exchange(ref _rebuildRetryCount, 0);
                }
                return;
            }

            while (TryDequeue(out var append))
            {
                if (_columns > 0 && _rows.TryAppend(append.StartingIndex, append.Items, _columns))
                {
                    continue;
                }

                ClearPendingAppends();
                TryRebuildRows();
                return;
            }
        }, DispatcherPriority.Loaded);
    }

    private bool TryRebuildRows()
    {
        if (!TryGetStableWidth(out var width))
        {
            return false;
        }

        var metrics = CalculateMetrics(width, CardKind);
        _columns = metrics.Columns;
        _cardWidth = metrics.CardWidth;
        var items = SourceItems?.Cast<object?>().Where(x => x is not null).Cast<object>()
            ?? Enumerable.Empty<object>();
        _rows.Rebuild(items, _columns);
        return true;
    }

    private bool TryGetStableWidth(out double width)
    {
        var current = Bounds.Width;
        if (VisualRoot is null || !double.IsFinite(current) || current < MinimumStableWidth)
        {
            width = _lastStableWidth;
            return false;
        }

        _lastStableWidth = current;
        width = current;
        return true;
    }

    internal static GridMetrics CalculateMetrics(double availableWidth, DesktopCardGridKind kind)
    {
        if (!double.IsFinite(availableWidth) || availableWidth < 2)
        {
            availableWidth = FallbackAvailableWidth;
        }

        var profile = kind == DesktopCardGridKind.Live
            ? new GridProfile(300, 430, 5)
            : new GridProfile(168, 220, 8);
        var columns = Math.Max(1,
            (int)Math.Floor((availableWidth + CardGap) / (profile.MinWidth + CardGap)));
        columns = Math.Min(columns, profile.MaxColumns);
        var cardWidth = Math.Floor((availableWidth - CardGap * (columns - 1)) / columns);
        cardWidth = Math.Clamp(cardWidth, 2, profile.MaxWidth);
        cardWidth = Math.Max(2, Math.Floor(cardWidth / 2) * 2);
        return new GridMetrics(columns, cardWidth);
    }

    private bool TryDequeue(out PendingAppend append)
    {
        lock (_pendingLock)
        {
            return _pendingAppends.TryDequeue(out append!);
        }
    }

    private void ClearPendingAppends()
    {
        lock (_pendingLock)
        {
            _pendingAppends.Clear();
        }
    }

    internal readonly record struct GridMetrics(int Columns, double CardWidth);
    private readonly record struct GridProfile(double MinWidth, double MaxWidth, int MaxColumns);
    private sealed record PendingAppend(int StartingIndex, IReadOnlyList<object> Items);

    private sealed class RowControl : ContentControl
    {
        private readonly DesktopVirtualizingCardGrid _owner;
        private readonly DesktopCardRowPresenter _presenter = new();

        public RowControl(DesktopVirtualizingCardGrid owner)
        {
            _owner = owner;
            Content = _presenter;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            var row = DataContext as DesktopCardGridRow;
            _presenter.Populate(
                _owner.CardKind,
                _owner.PresentationMode,
                _owner._columns,
                _owner._cardWidth,
                row?.Items ?? Array.Empty<object>());
        }
    }
}
