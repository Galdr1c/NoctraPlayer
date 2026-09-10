using Avalonia;
using Avalonia.Controls;
using Noctra.UI.Layout;

namespace Noctra.UI.Views;

public partial class AdaptiveDownloadsContentView : UserControl
{
    public static readonly StyledProperty<Control?> ContentHostProperty =
        AvaloniaProperty.Register<AdaptiveDownloadsContentView, Control?>(nameof(ContentHost));

    public static readonly StyledProperty<Control?> PlatformStorageActionsProperty =
        AvaloniaProperty.Register<AdaptiveDownloadsContentView, Control?>(nameof(PlatformStorageActions));

    public AdaptiveDownloadsContentView()
    {
        InitializeComponent();
        ApplyAdaptiveLayout(Bounds.Width);
        SizeChanged += (_, args) => ApplyAdaptiveLayout(args.NewSize.Width);
    }

    public Control? ContentHost
    {
        get => GetValue(ContentHostProperty);
        set => SetValue(ContentHostProperty, value);
    }

    public Control? PlatformStorageActions
    {
        get => GetValue(PlatformStorageActionsProperty);
        set => SetValue(PlatformStorageActionsProperty, value);
    }

    private void ApplyAdaptiveLayout(double width)
    {
        var metrics = AdaptiveLayoutMetrics.ForWidth(width);
        LayoutRoot.Margin = new Thickness(metrics.PagePadding);
    }
}
