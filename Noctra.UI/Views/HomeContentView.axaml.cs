using Avalonia;
using Avalonia.Controls;
using Noctra.UI.Layout;

namespace Noctra.UI.Views;

public partial class HomeContentView : UserControl
{
    public static readonly StyledProperty<bool> HasContentProperty =
        AvaloniaProperty.Register<HomeContentView, bool>(nameof(HasContent));

    public static readonly StyledProperty<object?> ItemsHostProperty =
        AvaloniaProperty.Register<HomeContentView, object?>(nameof(ItemsHost));

    public static readonly StyledProperty<object?> EmptyArtworkProperty =
        AvaloniaProperty.Register<HomeContentView, object?>(nameof(EmptyArtwork));

    public HomeContentView()
    {
        InitializeComponent();
        ApplyAdaptiveLayout(Bounds.Width);
        SizeChanged += (_, args) => ApplyAdaptiveLayout(args.NewSize.Width);
    }

    private void ApplyAdaptiveLayout(double width)
    {
        var metrics = AdaptiveLayoutMetrics.ForWidth(width);
        LayoutRoot.Margin = new Thickness(metrics.PagePadding);
    }

    public bool HasContent
    {
        get => GetValue(HasContentProperty);
        set => SetValue(HasContentProperty, value);
    }

    public object? ItemsHost
    {
        get => GetValue(ItemsHostProperty);
        set => SetValue(ItemsHostProperty, value);
    }

    public object? EmptyArtwork
    {
        get => GetValue(EmptyArtworkProperty);
        set => SetValue(EmptyArtworkProperty, value);
    }
}
