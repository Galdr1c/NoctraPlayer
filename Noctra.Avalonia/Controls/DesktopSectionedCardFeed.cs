using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Noctra.Core.Collections;

namespace Noctra.Avalonia.Controls;

public sealed class DesktopCardSection : StyledElement
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<DesktopCardSection, string?>(nameof(Header));
    public static readonly StyledProperty<string?> GroupHeaderProperty =
        AvaloniaProperty.Register<DesktopCardSection, string?>(nameof(GroupHeader));
    public static readonly StyledProperty<IEnumerable?> SourceItemsProperty =
        AvaloniaProperty.Register<DesktopCardSection, IEnumerable?>(nameof(SourceItems));
    public static readonly StyledProperty<DesktopCardGridKind> CardKindProperty =
        AvaloniaProperty.Register<DesktopCardSection, DesktopCardGridKind>(nameof(CardKind));
    public static readonly StyledProperty<DesktopCardPresentationMode> PresentationModeProperty =
        AvaloniaProperty.Register<DesktopCardSection, DesktopCardPresentationMode>(nameof(PresentationMode));

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? GroupHeader { get => GetValue(GroupHeaderProperty); set => SetValue(GroupHeaderProperty, value); }
    public IEnumerable? SourceItems { get => GetValue(SourceItemsProperty); set => SetValue(SourceItemsProperty, value); }
    public DesktopCardGridKind CardKind { get => GetValue(CardKindProperty); set => SetValue(CardKindProperty, value); }
    public DesktopCardPresentationMode PresentationMode { get => GetValue(PresentationModeProperty); set => SetValue(PresentationModeProperty, value); }
}

/// <summary>
/// One vertically virtualized feed for Favorites, My List, History and Search.
/// Section headers and card rows share the same recycling list, avoiding nested
/// ScrollViewer/StackPanel/WrapPanel trees.
/// </summary>
public sealed class DesktopSectionedCardFeed : ListBox
{
    private const double FallbackAvailableWidth = 1180;
    private const double MinimumStableWidth = 240;
    private readonly SectionedIncrementalRowCollection<DesktopCardSection, object> _rows = new();
    private readonly HashSet<DesktopCardSection> _observedSections = new();
    private readonly Dictionary<DesktopCardSection, INotifyCollectionChanged> _observedSources = new();
    private readonly Queue<PendingAppend> _pendingAppends = new();
    private readonly object _pendingLock = new();
    private int _refreshQueued;
    private int _fullRebuildRequired;
    private double _lastStableWidth = FallbackAvailableWidth;
    private double _lastAvailableWidth;

    protected override Type StyleKeyOverride => typeof(ListBox);

    public DesktopSectionedCardFeed()
    {
        Classes.Add("DesktopVirtualFeed");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = null;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        ItemsSource = _rows.Rows;
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 0.9 });
        ItemTemplate = new FuncDataTemplate<SectionedCollectionRow<DesktopCardSection, object>>(
            (row, _) => row is null ? null : new FeedRowControl(this) { DataContext = row },
            supportsRecycling: true);
        Sections.CollectionChanged += Sections_CollectionChanged;
        SelectionChanged += ClearTransientSelection;
        SizeChanged += (_, _) => QueueRebuildIfMetricsChanged();
        AttachedToVisualTree += (_, _) => QueueFullRebuild();
        AddHandler(ScrollViewer.ScrollChangedEvent, OnInnerScrollChanged);
    }

    public AvaloniaList<DesktopCardSection> Sections { get; } = new();
    public event EventHandler<ScrollChangedEventArgs>? ScrollChanged;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        PropagateDataContextToSections();
    }

    private void PropagateDataContextToSections()
    {
        foreach (var section in Sections)
        {
            section.DataContext = DataContext;
        }
    }

    private void Sections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.Cast<DesktopCardSection>())
            {
                item.DataContext = DataContext;
            }
        }
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.Cast<DesktopCardSection>())
            {
                item.DataContext = null;
            }
        }
        RefreshSubscriptions();
        QueueFullRebuild();
    }

    private void RefreshSubscriptions()
    {
        foreach (var section in _observedSections)
        {
            section.PropertyChanged -= Section_PropertyChanged;
        }
        foreach (var observable in _observedSources.Values)
        {
            observable.CollectionChanged -= Source_CollectionChanged;
        }
        _observedSections.Clear();
        _observedSources.Clear();

        foreach (var section in Sections)
        {
            section.PropertyChanged += Section_PropertyChanged;
            _observedSections.Add(section);
            if (section.SourceItems is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged += Source_CollectionChanged;
                _observedSources[section] = observable;
            }
        }
    }

    private void Section_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        RefreshSubscriptions();
        QueueFullRebuild();
    }

    private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var section = _observedSources.FirstOrDefault(x => ReferenceEquals(x.Value, sender)).Key;
        if (section is not null &&
            e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems is { Count: > 0 } &&
            e.NewStartingIndex >= 0)
        {
            var items = e.NewItems.Cast<object?>().Where(x => x is not null).Cast<object>().ToArray();
            if (items.Length == e.NewItems.Count)
            {
                lock (_pendingLock)
                {
                    _pendingAppends.Enqueue(new PendingAppend(section, e.NewStartingIndex, items));
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

        if (Math.Abs(width - _lastAvailableWidth) > 8)
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
                    Interlocked.Exchange(ref _fullRebuildRequired, 1);
                }
                return;
            }

            while (TryDequeue(out var append))
            {
                if (!TryGetStableWidth(out var width))
                {
                    Interlocked.Exchange(ref _fullRebuildRequired, 1);
                    return;
                }

                var columns = DesktopVirtualizingCardGrid.CalculateMetrics(
                    width, append.Section.CardKind).Columns;
                if (_rows.TryAppend(append.Section, append.StartingIndex, append.Items, columns))
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

        _lastAvailableWidth = width;
        var sources = Sections.Select(section =>
        {
            var columns = DesktopVirtualizingCardGrid.CalculateMetrics(
                width, section.CardKind).Columns;
            var items = section.SourceItems?.Cast<object?>().Where(x => x is not null).Cast<object>()
                ?? Enumerable.Empty<object>();
            return new SectionedRowSource<DesktopCardSection, object>(section, items, columns);
        });
        _rows.Rebuild(sources);
        return true;
    }

    private void PopulateRow(
        DesktopCardRowPresenter presenter,
        SectionedCollectionRow<DesktopCardSection, object> row)
    {
        var metrics = DesktopVirtualizingCardGrid.CalculateMetrics(
            _lastAvailableWidth, row.Section.CardKind);
        presenter.Populate(
            row.Section.CardKind,
            row.Section.PresentationMode,
            metrics.Columns,
            metrics.CardWidth,
            row.Items);
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

    private bool ShouldShowGroupHeader(DesktopCardSection section)
    {
        if (string.IsNullOrWhiteSpace(section.GroupHeader))
        {
            return false;
        }

        DesktopCardSection? previous = null;
        foreach (var row in _rows.Rows)
        {
            if (!row.IsHeader)
            {
                continue;
            }
            if (ReferenceEquals(row.Section, section))
            {
                return !string.Equals(previous?.GroupHeader, section.GroupHeader, StringComparison.Ordinal);
            }
            previous = row.Section;
        }
        return true;
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

    private sealed record PendingAppend(
        DesktopCardSection Section,
        int StartingIndex,
        IReadOnlyList<object> Items);

    private sealed class FeedRowControl : ContentControl
    {
        private readonly DesktopSectionedCardFeed _owner;
        private SectionHeaderControl? _header;
        private DesktopCardRowPresenter? _cards;

        public FeedRowControl(DesktopSectionedCardFeed owner)
        {
            _owner = owner;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is not SectionedCollectionRow<DesktopCardSection, object> row)
            {
                Content = null;
                return;
            }

            if (row.IsHeader)
            {
                _header ??= new SectionHeaderControl();
                _header.Populate(row.Section, _owner.ShouldShowGroupHeader(row.Section));
                Content = _header;
                return;
            }

            _cards ??= new DesktopCardRowPresenter();
            _owner.PopulateRow(_cards, row);
            Content = _cards;
        }
    }

    private sealed class SectionHeaderControl : StackPanel
    {
        private readonly TextBlock _groupHeader;
        private readonly TextBlock _header;

        public SectionHeaderControl()
        {
            Spacing = 8;
            Margin = new Thickness(0, 24, 0, 12);
            _groupHeader = new TextBlock
            {
                FontSize = 24,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _header = new TextBlock
            {
                FontSize = 19,
                FontWeight = FontWeight.SemiBold
            };
            Children.Add(_groupHeader);
            Children.Add(_header);
        }

        public void Populate(DesktopCardSection section, bool showGroupHeader)
        {
            _groupHeader.Text = section.GroupHeader;
            _groupHeader.IsVisible = showGroupHeader;
            _header.Text = section.Header;
        }
    }
}
