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
using Noctra.Core.Advertising;
using Noctra.Core.Collections;
using Noctra.Mobile.Services;

namespace Noctra.Mobile.Controls;

public sealed class MobileCardSection : AvaloniaObject
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<MobileCardSection, string?>(nameof(Header));

    public static readonly StyledProperty<string?> GroupHeaderProperty =
        AvaloniaProperty.Register<MobileCardSection, string?>(nameof(GroupHeader));

    public static readonly StyledProperty<IEnumerable?> SourceItemsProperty =
        AvaloniaProperty.Register<MobileCardSection, IEnumerable?>(nameof(SourceItems));

    public static readonly StyledProperty<MobileCardGridKind> CardKindProperty =
        AvaloniaProperty.Register<MobileCardSection, MobileCardGridKind>(nameof(CardKind));

    public static readonly StyledProperty<MobileCardPresentationMode> PresentationModeProperty =
        AvaloniaProperty.Register<MobileCardSection, MobileCardPresentationMode>(nameof(PresentationMode));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? GroupHeader
    {
        get => GetValue(GroupHeaderProperty);
        set => SetValue(GroupHeaderProperty, value);
    }

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

    public MobileCardPresentationMode PresentationMode
    {
        get => GetValue(PresentationModeProperty);
        set => SetValue(PresentationModeProperty, value);
    }

    /// <summary>
    /// Internal presentation marker used by MobileSectionedCardFeed. Domain/view-model
    /// sections never set this; the feed owns synthetic advertising sections.
    /// </summary>
    public bool IsNativeAd { get; set; }
}

/// <summary>
/// Flattens multiple named card sections into one vertically virtualized list.
/// Each realized item is either a section heading or a small responsive card row.
/// Source appends and identity-preserving diffs are projected into only the
/// affected section so unrelated rows keep their identity during paging.
/// </summary>
public sealed class MobileSectionedCardFeed : ListBox
{
    private const double CardGap = 16;
    private const double FallbackAvailableWidth = 720;

    public static readonly StyledProperty<AdPlacement> AdPlacementProperty =
        AvaloniaProperty.Register<MobileSectionedCardFeed, AdPlacement>(
            nameof(AdPlacement),
            AdPlacement.None);

    public static readonly StyledProperty<int> NativeAdInsertAfterSectionProperty =
        AvaloniaProperty.Register<MobileSectionedCardFeed, int>(
            nameof(NativeAdInsertAfterSection),
            0);

    private readonly SectionedIncrementalRowCollection<MobileCardSection, object> _rowCollection = new();
    private readonly Dictionary<MobileCardSection, INotifyCollectionChanged> _observedSources = new();
    private readonly Queue<PendingAppend> _pendingAppends = new();
    private readonly Queue<MobileCardSection> _pendingSynchronizations = new();
    private readonly HashSet<MobileCardSection> _pendingSynchronizationSet = new();
    private readonly object _pendingAppendsLock = new();
    private readonly string _adOwnerKey = $"sectioned:{Guid.NewGuid():N}";
    private readonly MobileCardSection _nativeAdSection = new()
    {
        IsNativeAd = true,
        CardKind = MobileCardGridKind.Vod,
        PresentationMode = MobileCardPresentationMode.Standard
    };
    private int _rebuildQueued;
    private int _fullRebuildRequired;
    private double _lastAvailableWidth;
    private bool _hasNativeAdSection;
    private IMobileAdvertisingService? _advertisingService;

    protected override Type StyleKeyOverride => typeof(ListBox);

    static MobileSectionedCardFeed()
    {
        AdPlacementProperty.Changed.AddClassHandler<MobileSectionedCardFeed>(
            (control, _) => control.QueueFullRebuild());
        NativeAdInsertAfterSectionProperty.Changed.AddClassHandler<MobileSectionedCardFeed>(
            (control, _) => control.QueueFullRebuild());
    }

    public MobileSectionedCardFeed()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = null;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        ItemsSource = _rowCollection.Rows;
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel
        {
            CacheLength = 0.75
        });
        ItemTemplate = new FuncDataTemplate<SectionedCollectionRow<MobileCardSection, object>>(
            (row, _) => row is null
                ? null
                : new MobileSectionFeedRowControl(this) { DataContext = row },
            supportsRecycling: true);
        Sections.CollectionChanged += Sections_CollectionChanged;
        SelectionChanged += ClearTransientSelection;
        SizeChanged += (_, _) => QueueRebuildIfMetricsChanged();
        AttachedToVisualTree += (_, _) => SubscribeAdvertising();
        DetachedFromVisualTree += (_, _) => UnsubscribeAdvertising();
        AddHandler(ScrollViewer.ScrollChangedEvent, OnInnerScrollChanged);
    }

    public AvaloniaList<MobileCardSection> Sections { get; } = new();

    public AdPlacement AdPlacement
    {
        get => GetValue(AdPlacementProperty);
        set => SetValue(AdPlacementProperty, value);
    }

    /// <summary>
    /// Search uses 3: the ad is inserted after the three exact-result sections
    /// and before the three "similar" sections.
    /// </summary>
    public int NativeAdInsertAfterSection
    {
        get => GetValue(NativeAdInsertAfterSectionProperty);
        set => SetValue(NativeAdInsertAfterSectionProperty, value);
    }

    public event EventHandler<ScrollChangedEventArgs>? ScrollChanged;

    private void SubscribeAdvertising()
    {
        _advertisingService = MobileAdvertisingServices.TryGet();
        if (_advertisingService is null)
            return;

        _advertisingService.EligibilityChanged -= AdvertisingService_EligibilityChanged;
        _advertisingService.EligibilityChanged += AdvertisingService_EligibilityChanged;
        QueueFullRebuild();
    }

    private void UnsubscribeAdvertising()
    {
        if (_advertisingService is null)
            return;

        _advertisingService.EligibilityChanged -= AdvertisingService_EligibilityChanged;
        _advertisingService.ReleaseOwner(_adOwnerKey);
        _advertisingService = null;
    }

    private void AdvertisingService_EligibilityChanged(object? sender, EventArgs e)
        => QueueFullRebuild();

    private void Sections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSectionSubscriptions();
        QueueFullRebuild();
    }

    private void RefreshSectionSubscriptions()
    {
        foreach (var section in _observedSources.Keys.ToArray())
        {
            section.PropertyChanged -= Section_PropertyChanged;
            _observedSources[section].CollectionChanged -= Source_CollectionChanged;
        }

        _observedSources.Clear();
        foreach (var section in Sections)
        {
            section.PropertyChanged -= Section_PropertyChanged;
            section.PropertyChanged += Section_PropertyChanged;
            if (section.SourceItems is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged += Source_CollectionChanged;
                _observedSources[section] = observable;
            }
        }
    }

    private void Section_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        RefreshSectionSubscriptions();
        QueueFullRebuild();
    }

    private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var section = _observedSources.FirstOrDefault(pair => ReferenceEquals(pair.Value, sender)).Key;
        if (section is null)
        {
            QueueFullRebuild();
            return;
        }

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
                    _pendingAppends.Enqueue(new PendingAppend(section, e.NewStartingIndex, items));
                }

                QueueRefresh();
                return;
            }
        }

        lock (_pendingAppendsLock)
        {
            if (_pendingSynchronizationSet.Add(section))
            {
                _pendingSynchronizations.Enqueue(section);
            }
        }
        QueueRefresh();
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
        var width = GetAvailableWidth();
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
                ClearPendingChanges();
                RebuildRows();
                return;
            }

            while (TryDequeuePendingAppend(out var append))
            {
                // The first append that makes Search eligible for its native slot must
                // add the synthetic section in the correct exact/similar boundary.
                if (!_hasNativeAdSection && IsNativeAdThresholdReached())
                {
                    ClearPendingChanges();
                    RebuildRows();
                    return;
                }

                var columns = CalculateMetrics(GetAvailableWidth(), append.Section.CardKind).Columns;
                if (_rowCollection.TryAppend(
                        append.Section,
                        append.StartingIndex,
                        append.Items,
                        columns,
                        ShouldRefreshFollowingGroupHeader))
                {
                    continue;
                }

                if (!TrySynchronizeSection(append.Section, columns))
                {
                    ClearPendingChanges();
                    RebuildRows();
                    return;
                }
            }

            while (TryDequeuePendingSynchronization(out var section))
            {
                var columns = CalculateMetrics(GetAvailableWidth(), section.CardKind).Columns;
                if (TrySynchronizeSection(section, columns))
                {
                    continue;
                }

                ClearPendingChanges();
                RebuildRows();
                return;
            }
        }, DispatcherPriority.Loaded);
    }

    private void RebuildRows()
    {
        _lastAvailableWidth = GetAvailableWidth();

        var sources = Sections.Select(section =>
        {
            var columns = CalculateMetrics(_lastAvailableWidth, section.CardKind).Columns;
            var items = section.SourceItems?
                .Cast<object?>()
                .Where(item => item != null)
                .Cast<object>() ?? Enumerable.Empty<object>();
            return new SectionedRowSource<MobileCardSection, object>(
                section,
                items,
                columns);
        }).ToList();

        _hasNativeAdSection = false;
        var insertAfter = Math.Clamp(NativeAdInsertAfterSection, 0, Sections.Count);
        if (TryGetSearchNativeAdSlot(insertAfter, out var slot))
        {
            _nativeAdSection.SourceItems = new object[] { slot };
            sources.Insert(
                insertAfter,
                new SectionedRowSource<MobileCardSection, object>(
                    _nativeAdSection,
                    new object[] { slot },
                    1));
            _hasNativeAdSection = true;
        }
        else
        {
            _nativeAdSection.SourceItems = null;
        }

        _rowCollection.Rebuild(sources);
    }

    private bool IsNativeAdThresholdReached()
    {
        var insertAfter = Math.Clamp(NativeAdInsertAfterSection, 0, Sections.Count);
        var service = _advertisingService ??= MobileAdvertisingServices.TryGet();
        if (service is null ||
            !service.CanServeAds ||
            AdPlacement == AdPlacement.None)
        {
            return false;
        }

        var policy = service.Options.GetNative(AdPlacement);
        return AdPlacementPlanner.IsEligibleContentCount(
            CountItemsBeforeSection(insertAfter),
            policy);
    }

    private bool TryGetSearchNativeAdSlot(
        int insertAfter,
        out MobileNativeAdSlot slot)
    {
        slot = default;
        if (!IsNativeAdThresholdReached())
            return false;

        var service = _advertisingService ??= MobileAdvertisingServices.TryGet();
        if (service is null)
            return false;

        // Search intentionally has one slot. The eligibility count uses only the
        // sections before insertAfter (the three exact-result groups in XAML).
        service.PrimeNative(_adOwnerKey, AdPlacement, 1);
        slot = new MobileNativeAdSlot(_adOwnerKey, AdPlacement, 0);
        return true;
    }

    private int CountItemsBeforeSection(int exclusiveSectionIndex)
    {
        var count = 0;
        for (var index = 0; index < exclusiveSectionIndex; index++)
        {
            if (Sections[index].SourceItems is not IEnumerable source)
                continue;

            foreach (var item in source)
            {
                if (item is not null)
                    count++;
            }
        }

        return count;
    }

    private bool TryDequeuePendingAppend(out PendingAppend append)
    {
        lock (_pendingAppendsLock)
        {
            return _pendingAppends.TryDequeue(out append!);
        }
    }

    private bool TryDequeuePendingSynchronization(out MobileCardSection section)
    {
        lock (_pendingAppendsLock)
        {
            if (!_pendingSynchronizations.TryDequeue(out section!))
            {
                return false;
            }

            _pendingSynchronizationSet.Remove(section);
            return true;
        }
    }

    private bool TrySynchronizeSection(MobileCardSection section, int columns)
    {
        var items = section.SourceItems?
            .Cast<object?>()
            .Where(item => item != null)
            .Cast<object>() ?? Enumerable.Empty<object>();
        return _rowCollection.TrySynchronizeSection(
            section,
            items,
            columns,
            ShouldRefreshFollowingGroupHeader);
    }

    private void ClearPendingChanges()
    {
        lock (_pendingAppendsLock)
        {
            _pendingAppends.Clear();
            _pendingSynchronizations.Clear();
            _pendingSynchronizationSet.Clear();
        }
    }

    private void PopulateRow(
        MobileCardRowPresenter panel,
        SectionedCollectionRow<MobileCardSection, object> row)
    {
        var metrics = CalculateMetrics(GetAvailableWidth(), row.Section.CardKind);
        panel.Populate(
            row.Section.CardKind,
            row.Section.PresentationMode,
            metrics.Columns,
            metrics.CardWidth,
            row.Items);
    }

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

    private bool ShouldShowGroupHeader(MobileCardSection section)
    {
        if (string.IsNullOrWhiteSpace(section.GroupHeader))
        {
            return false;
        }

        MobileCardSection? previous = null;
        foreach (var row in _rowCollection.Rows)
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

    private static bool ShouldRefreshFollowingGroupHeader(
        MobileCardSection _,
        MobileCardSection following)
        => !string.IsNullOrWhiteSpace(following.GroupHeader);

    private readonly record struct GridProfile(double MinWidth, double MaxWidth, int MaxColumns);
    private readonly record struct GridMetrics(int Columns, double CardWidth);
    private sealed record PendingAppend(
        MobileCardSection Section,
        int StartingIndex,
        IReadOnlyList<object> Items);

    private sealed class MobileSectionFeedRowControl : ContentControl
    {
        private readonly MobileSectionedCardFeed _owner;
        private MobileSectionHeaderControl? _header;
        private MobileCardRowPresenter? _cards;
        private MobileNativeAdHost? _adHost;

        public MobileSectionFeedRowControl(MobileSectionedCardFeed owner)
        {
            _owner = owner;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is not SectionedCollectionRow<MobileCardSection, object> row)
            {
                Content = null;
                return;
            }

            if (row.Section.IsNativeAd)
            {
                if (row.IsHeader)
                {
                    _adHost?.Clear();
                    Height = 0;
                    Content = null;
                    return;
                }

                Height = double.NaN;
                _adHost ??= new MobileNativeAdHost();
                if (row.Items.Count > 0 && row.Items[0] is MobileNativeAdSlot slot)
                {
                    _adHost.Bind(slot);
                    Content = _adHost;
                }
                else
                {
                    _adHost.Clear();
                    Content = null;
                }
                return;
            }

            Height = double.NaN;
            _adHost?.Clear();
            if (row.IsHeader)
            {
                _header ??= new MobileSectionHeaderControl();
                _header.Populate(row.Section, _owner.ShouldShowGroupHeader(row.Section));
                Content = _header;
                return;
            }

            _cards ??= new MobileCardRowPresenter();
            _owner.PopulateRow(_cards, row);
            Content = _cards;
        }
    }

    private sealed class MobileSectionHeaderControl : StackPanel
    {
        private readonly TextBlock _groupHeader;
        private readonly TextBlock _header;

        public MobileSectionHeaderControl()
        {
            Spacing = 10;
            Margin = new Thickness(0, 24, 0, 12);
            _groupHeader = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _header = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeight.SemiBold
            };
            Children.Add(_groupHeader);
            Children.Add(_header);
        }

        public void Populate(MobileCardSection section, bool showGroupHeader)
        {
            _groupHeader.Text = section.GroupHeader;
            _groupHeader.IsVisible = showGroupHeader;
            _header.Text = section.Header;
        }
    }

}
