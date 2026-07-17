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
using Noctra.Core.Collections;

namespace Noctra.Mobile.Controls;

public enum MobileCardGridKind
{
    Live,
    Vod,
    Series
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
    private int _columns;
    private double _cardWidth;

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
        var metrics = CalculateMetrics(GetAvailableWidth(), CardKind);
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
                RebuildRows();
                return;
            }

            while (TryDequeuePendingAppend(out var append))
            {
                if (_rowCollection.TryAppend(append.StartingIndex, append.Items, _columns))
                {
                    continue;
                }

                ClearPendingAppends();
                RebuildRows();
                return;
            }
        }, DispatcherPriority.Loaded);
    }

    private void RebuildRows()
    {
        var metrics = CalculateMetrics(GetAvailableWidth(), CardKind);
        _columns = metrics.Columns;
        _cardWidth = metrics.CardWidth;

        var items = SourceItems?
            .Cast<object?>()
            .Where(item => item != null)
            .Cast<object>() ?? Enumerable.Empty<object>();
        _rowCollection.Rebuild(items, _columns);
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
        panel.EnsureCardSlots();

        for (var index = 0; index < panel.Cards.Count; index++)
        {
            var card = panel.Cards[index];
            var item = row is not null && index < row.Items.Count
                ? row.Items[index]
                : null;
            card.DataContext = item;
            card.IsVisible = item != null;
            card.Width = _cardWidth;
            if (CardKind is MobileCardGridKind.Vod or MobileCardGridKind.Series)
            {
                card.Height = Math.Round(_cardWidth * 1.5);
            }
            else
            {
                card.Height = double.NaN;
            }

            card.Margin = new Thickness(
                0,
                0,
                row is not null && index < row.Items.Count - 1 ? CardGap : 0,
                CardKind == MobileCardGridKind.Live ? 0 : CardGap);
        }
    }

    private Control CreateCard()
        => CardKind switch
        {
            MobileCardGridKind.Live => new MobileLiveTvCard(),
            MobileCardGridKind.Vod => new MobileVodCard(),
            _ => new MobileSeriesCard()
        };

    private double GetAvailableWidth()
        => double.IsFinite(Bounds.Width) && Bounds.Width > 0
            ? Bounds.Width
            : FallbackAvailableWidth;

    private static GridMetrics CalculateMetrics(double availableWidth, MobileCardGridKind kind)
    {
        var profile = kind == MobileCardGridKind.Live
            ? new GridProfile(220, 410, 4)
            : new GridProfile(150, 180, 6);
        var columns = Math.Max(
            1,
            (int)Math.Floor((availableWidth + CardGap) / (profile.MinWidth + CardGap)));
        columns = Math.Min(columns, profile.MaxColumns);

        var width = Math.Floor((availableWidth - CardGap * (columns - 1)) / columns);
        width = Math.Clamp(width, 1, profile.MaxWidth);
        return new GridMetrics(columns, Math.Floor(width / 2) * 2);
    }

    private readonly record struct GridProfile(double MinWidth, double MaxWidth, int MaxColumns);
    private readonly record struct GridMetrics(int Columns, double CardWidth);
    private sealed record PendingAppend(int StartingIndex, IReadOnlyList<object> Items);

    private sealed class MobileCardGridRowControl : WrapPanel
    {
        private readonly MobileVirtualizingCardGrid _owner;
        private readonly List<Control> _cards = new();
        private MobileCardGridKind _cardKind;
        private int _slotCount;

        public MobileCardGridRowControl(MobileVirtualizingCardGrid owner)
        {
            _owner = owner;
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        public IReadOnlyList<Control> Cards => _cards;

        public void EnsureCardSlots()
        {
            if (_slotCount == _owner._columns && _cardKind == _owner.CardKind)
            {
                return;
            }

            Children.Clear();
            _cards.Clear();
            _slotCount = _owner._columns;
            _cardKind = _owner.CardKind;

            for (var index = 0; index < _slotCount; index++)
            {
                var card = _owner.CreateCard();
                _cards.Add(card);
                Children.Add(card);
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            _owner.PopulateRow(this, DataContext as MobileCardGridRow);
        }
    }
}
