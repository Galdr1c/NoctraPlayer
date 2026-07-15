using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
public sealed class MobileVirtualizingCardGrid : ItemsControl
{
    private const double CardGap = 16;
    private const double FallbackAvailableWidth = 720;

    public static readonly StyledProperty<IEnumerable?> SourceItemsProperty =
        AvaloniaProperty.Register<MobileVirtualizingCardGrid, IEnumerable?>(nameof(SourceItems));

    public static readonly StyledProperty<MobileCardGridKind> CardKindProperty =
        AvaloniaProperty.Register<MobileVirtualizingCardGrid, MobileCardGridKind>(nameof(CardKind));

    private readonly BatchObservableCollection<MobileCardGridRow> _rows = new();
    private INotifyCollectionChanged? _observableSource;
    private int _rebuildQueued;
    private int _columns;
    private double _cardWidth;

    static MobileVirtualizingCardGrid()
    {
        SourceItemsProperty.Changed.AddClassHandler<MobileVirtualizingCardGrid>(
            (control, args) => control.OnSourceItemsChanged(args.OldValue as IEnumerable, args.NewValue as IEnumerable));
        CardKindProperty.Changed.AddClassHandler<MobileVirtualizingCardGrid>(
            (control, _) => control.QueueRebuild());
    }

    public MobileVirtualizingCardGrid()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ItemsSource = _rows;
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel
        {
            CacheLength = 0.5
        });
        ItemTemplate = new FuncDataTemplate<MobileCardGridRow>(
            (row, _) => row is null
                ? null
                : new MobileCardGridRowControl(this) { DataContext = row },
            supportsRecycling: true);
        SizeChanged += (_, _) => QueueRebuildIfMetricsChanged();
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

        QueueRebuild();
    }

    private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueRebuild();

    private void QueueRebuildIfMetricsChanged()
    {
        var metrics = CalculateMetrics(GetAvailableWidth(), CardKind);
        if (metrics.Columns != _columns || Math.Abs(metrics.CardWidth - _cardWidth) > 8)
        {
            QueueRebuild();
        }
    }

    private void QueueRebuild()
    {
        if (Interlocked.Exchange(ref _rebuildQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _rebuildQueued, 0);
            RebuildRows();
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
            .Cast<object>()
            .ToList() ?? new List<object>();
        var rows = new List<MobileCardGridRow>((items.Count + _columns - 1) / _columns);

        for (var index = 0; index < items.Count; index += _columns)
        {
            rows.Add(new MobileCardGridRow(
                items.Skip(index).Take(_columns).ToArray()));
        }

        _rows.ReplaceAll(rows);
    }

    private void PopulateRow(WrapPanel panel, MobileCardGridRow? row)
    {
        panel.Children.Clear();
        if (row == null)
        {
            return;
        }

        for (var index = 0; index < row.Items.Count; index++)
        {
            var card = CreateCard(row.Items[index]);
            card.Width = _cardWidth;
            if (CardKind is MobileCardGridKind.Vod or MobileCardGridKind.Series)
            {
                card.Height = Math.Round(_cardWidth * 1.5);
            }

            card.Margin = new Thickness(
                0,
                0,
                index < row.Items.Count - 1 ? CardGap : 0,
                CardKind == MobileCardGridKind.Live ? 0 : CardGap);
            panel.Children.Add(card);
        }
    }

    private Control CreateCard(object item)
    {
        Control card = CardKind switch
        {
            MobileCardGridKind.Live => new MobileLiveTvCard(),
            MobileCardGridKind.Vod => new MobileVodCard(),
            _ => new MobileSeriesCard()
        };

        card.DataContext = item;
        return card;
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

    private readonly record struct GridProfile(double MinWidth, double MaxWidth, int MaxColumns);
    private readonly record struct GridMetrics(int Columns, double CardWidth);

    private sealed class MobileCardGridRowControl : WrapPanel
    {
        private readonly MobileVirtualizingCardGrid _owner;

        public MobileCardGridRowControl(MobileVirtualizingCardGrid owner)
        {
            _owner = owner;
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            _owner.PopulateRow(this, DataContext as MobileCardGridRow);
        }
    }
}
