using Avalonia;
using Avalonia.Controls;

namespace Noctra.Mobile.Controls;

public partial class MobileVodCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<MobileVodCard, bool>(nameof(ShowHistoryMenu));

    public bool ShowHistoryMenu
    {
        get => GetValue(ShowHistoryMenuProperty);
        set => SetValue(ShowHistoryMenuProperty, value);
    }

    public MobileVodCard()
    {
        InitializeComponent();
    }
}
