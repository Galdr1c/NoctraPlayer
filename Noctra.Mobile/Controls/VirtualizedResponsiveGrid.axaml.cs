using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Noctra.Mobile.Controls;

public partial class VirtualizedResponsiveGrid : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<VirtualizedResponsiveGrid, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<VirtualizedResponsiveGrid, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<VirtualizedResponsiveGrid, double>(
            nameof(MinItemWidth),
            150d);

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<VirtualizedResponsiveGrid, double>(
            nameof(ItemSpacing),
            16d);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<VirtualizedResponsiveGrid, int>(
            nameof(MaxColumns),
            6);

    private INotifyCollectionChanged? _observableSource;
    private ScrollViewer? _scrollViewer;
    private bool _rebuildQueued;
    private int _lastColumnCount;

    static VirtualizedResponsiveGrid()
    {
        ItemsSourceProperty.Changed.AddClassHandler<VirtualizedResponsiveGrid>(
            static (control, args) => control.OnItemsSourceChanged(args.NewValue as IEnumerable));
        MinItemWidthProperty.Changed.AddClassHandler<VirtualizedResponsiveGrid>(
            static (control, _) => control.QueueRowsRebuild(force: true));
        ItemSpacingProperty.Changed.AddClassHandler<VirtualizedResponsiveGrid>(
            static (control, _) => control.QueueRowsRebuild(force: true));
        MaxColumnsProperty.Changed.AddClassHandler<VirtualizedResponsiveGrid>(
            static (control, _) => control.QueueRowsRebuild(force: true));
    }

    public VirtualizedResponsiveGrid()
    {
        InitializeComponent();
        SizeChanged += (_, _) => QueueRowsRebuild();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public ObservableCollection<VirtualizedGridRow> Rows { get; } = [];

    public event EventHandler<ScrollChangedEventArgs>? ScrollChanged;

    private void OnItemsSourceChanged(IEnumerable? newSource)
    {
        if (_observableSource is not null)
            _observableSource.CollectionChanged -= OnSourceCollectionChanged;

        _observableSource = newSource as INotifyCollectionChanged;
        if (_observableSource is not null)
            _observableSource.CollectionChanged += OnSourceCollectionChanged;

        QueueRowsRebuild(force: true);
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueRowsRebuild(force: true);

    private void QueueRowsRebuild(bool force = false)
    {
        var columns = CalculateColumnCount(Bounds.Width);
        if (!force && columns == _lastColumnCount)
            return;

        if (_rebuildQueued)
            return;

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            RebuildRows();
        }, DispatcherPriority.Background);
    }

    private void RebuildRows()
    {
        var columns = CalculateColumnCount(Bounds.Width);
        _lastColumnCount = columns;
        var items = ItemsSource?.Cast<object>().ToArray() ?? [];

        Rows.Clear();
        for (var offset = 0; offset < items.Length; offset += columns)
        {
            Rows.Add(new VirtualizedGridRow(
                items.Skip(offset).Take(columns).ToArray()));
        }
    }

    private int CalculateColumnCount(double availableWidth)
    {
        var minWidth = Math.Max(1, MinItemWidth);
        var spacing = Math.Max(0, ItemSpacing);
        var maxColumns = Math.Max(1, MaxColumns);
        var width = double.IsFinite(availableWidth) && availableWidth > 0
            ? availableWidth
            : minWidth;
        var columns = (int)Math.Floor((width + spacing) / (minWidth + spacing));
        return Math.Clamp(columns, 1, maxColumns);
    }

    private void RowsListBox_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        => ScrollChanged?.Invoke(sender, e);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_scrollViewer is not null)
                return;

            _scrollViewer = RowsListBox
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged += RowsListBox_OnScrollChanged;
        }, DispatcherPriority.Loaded);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= RowsListBox_OnScrollChanged;
        _scrollViewer = null;
    }
}

public sealed record VirtualizedGridRow(IReadOnlyList<object> Items);
