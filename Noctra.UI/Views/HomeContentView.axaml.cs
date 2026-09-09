using Avalonia;
using Avalonia.Controls;

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
