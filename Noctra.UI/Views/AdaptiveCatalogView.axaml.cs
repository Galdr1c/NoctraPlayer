using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Material.Icons;

namespace Noctra.UI.Views;

public partial class AdaptiveCatalogView : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, string?>(nameof(Title));
    public static readonly StyledProperty<string?> LoadingTextProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, string?>(nameof(LoadingText));
    public static readonly StyledProperty<string?> LoadingDetailTextProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, string?>(nameof(LoadingDetailText));
    public static readonly StyledProperty<string?> EmptyTitleProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, string?>(nameof(EmptyTitle));
    public static readonly StyledProperty<string?> EmptyDescriptionProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, string?>(nameof(EmptyDescription));
    public static readonly StyledProperty<string?> CategoryLabelProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, string?>(nameof(CategoryLabel));
    public static readonly StyledProperty<string?> SortLabelProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, string?>(nameof(SortLabel));
    public static readonly StyledProperty<object?> ItemsHostProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, object?>(nameof(ItemsHost));
    public static readonly StyledProperty<bool> ShowContentFiltersProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, bool>(nameof(ShowContentFilters));
    public static readonly StyledProperty<bool> ShowGroupFilterProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, bool>(nameof(ShowGroupFilter));
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, bool>(nameof(IsLoading));
    public static readonly StyledProperty<bool> ShowEmptyStateProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, bool>(nameof(ShowEmptyState));
    public static readonly StyledProperty<MaterialIconKind> SortIconKindProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, MaterialIconKind>(nameof(SortIconKind), MaterialIconKind.Sort);
    public static readonly StyledProperty<MaterialIconKind> EmptyIconKindProperty =
        AvaloniaProperty.Register<AdaptiveCatalogView, MaterialIconKind>(nameof(EmptyIconKind), MaterialIconKind.Television);

    public AdaptiveCatalogView()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? SortRequested;
    public event EventHandler<RoutedEventArgs>? CategoryRequested;

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? LoadingText { get => GetValue(LoadingTextProperty); set => SetValue(LoadingTextProperty, value); }
    public string? LoadingDetailText { get => GetValue(LoadingDetailTextProperty); set => SetValue(LoadingDetailTextProperty, value); }
    public string? EmptyTitle { get => GetValue(EmptyTitleProperty); set => SetValue(EmptyTitleProperty, value); }
    public string? EmptyDescription { get => GetValue(EmptyDescriptionProperty); set => SetValue(EmptyDescriptionProperty, value); }
    public string? CategoryLabel { get => GetValue(CategoryLabelProperty); set => SetValue(CategoryLabelProperty, value); }
    public string? SortLabel { get => GetValue(SortLabelProperty); set => SetValue(SortLabelProperty, value); }
    public object? ItemsHost { get => GetValue(ItemsHostProperty); set => SetValue(ItemsHostProperty, value); }
    public bool ShowContentFilters { get => GetValue(ShowContentFiltersProperty); set => SetValue(ShowContentFiltersProperty, value); }
    public bool ShowGroupFilter { get => GetValue(ShowGroupFilterProperty); set => SetValue(ShowGroupFilterProperty, value); }
    public bool IsLoading { get => GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public bool ShowEmptyState { get => GetValue(ShowEmptyStateProperty); set => SetValue(ShowEmptyStateProperty, value); }
    public MaterialIconKind SortIconKind { get => GetValue(SortIconKindProperty); set => SetValue(SortIconKindProperty, value); }
    public MaterialIconKind EmptyIconKind { get => GetValue(EmptyIconKindProperty); set => SetValue(EmptyIconKindProperty, value); }

    private void SortSelectionButton_Click(object? sender, RoutedEventArgs e)
        => SortRequested?.Invoke(this, e);

    private void CategorySelectionButton_Click(object? sender, RoutedEventArgs e)
        => CategoryRequested?.Invoke(this, e);
}
