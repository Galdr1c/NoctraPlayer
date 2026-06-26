using Avalonia;
using Avalonia.Controls;

namespace Noctra.Mobile.Controls;

public partial class MobileSeriesCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<MobileSeriesCard, bool>(nameof(ShowHistoryMenu));

    public bool ShowHistoryMenu
    {
        get => GetValue(ShowHistoryMenuProperty);
        set => SetValue(ShowHistoryMenuProperty, value);
    }

    public MobileSeriesCard()
    {
        InitializeComponent();
    }
}
