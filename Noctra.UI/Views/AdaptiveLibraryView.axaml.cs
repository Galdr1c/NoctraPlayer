using Avalonia;
using Avalonia.Controls;
using Material.Icons;

namespace Noctra.UI.Views;

public partial class AdaptiveLibraryView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> CountTextProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, string>(nameof(CountText), string.Empty);
    public static readonly StyledProperty<bool> ShowCountBadgeProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, bool>(nameof(ShowCountBadge));
    public static readonly StyledProperty<Control?> HeaderActionHostProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, Control?>(nameof(HeaderActionHost));
    public static readonly StyledProperty<Control?> ItemsHostProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, Control?>(nameof(ItemsHost));
    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, bool>(nameof(IsEmpty));
    public static readonly StyledProperty<MaterialIconKind> EmptyIconKindProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, MaterialIconKind>(nameof(EmptyIconKind), MaterialIconKind.InformationOutline);
    public static readonly StyledProperty<string> EmptyTitleProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, string>(nameof(EmptyTitle), string.Empty);
    public static readonly StyledProperty<string> EmptyDescriptionProperty =
        AvaloniaProperty.Register<AdaptiveLibraryView, string>(nameof(EmptyDescription), string.Empty);

    public AdaptiveLibraryView() => InitializeComponent();

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string CountText { get => GetValue(CountTextProperty); set => SetValue(CountTextProperty, value); }
    public bool ShowCountBadge { get => GetValue(ShowCountBadgeProperty); set => SetValue(ShowCountBadgeProperty, value); }
    public Control? HeaderActionHost { get => GetValue(HeaderActionHostProperty); set => SetValue(HeaderActionHostProperty, value); }
    public Control? ItemsHost { get => GetValue(ItemsHostProperty); set => SetValue(ItemsHostProperty, value); }
    public bool IsEmpty { get => GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }
    public MaterialIconKind EmptyIconKind { get => GetValue(EmptyIconKindProperty); set => SetValue(EmptyIconKindProperty, value); }
    public string EmptyTitle { get => GetValue(EmptyTitleProperty); set => SetValue(EmptyTitleProperty, value); }
    public string EmptyDescription { get => GetValue(EmptyDescriptionProperty); set => SetValue(EmptyDescriptionProperty, value); }
}
