using Avalonia;
using Avalonia.Controls;

namespace Noctra.UI.Views.Player;

public partial class PlayerTopOverlay : UserControl
{
    public static readonly StyledProperty<bool> ShowLockActionProperty =
        AvaloniaProperty.Register<PlayerTopOverlay, bool>(nameof(ShowLockAction), true);

    public static readonly StyledProperty<bool> ShowPiPActionProperty =
        AvaloniaProperty.Register<PlayerTopOverlay, bool>(nameof(ShowPiPAction), true);

    public PlayerTopOverlay()
    {
        InitializeComponent();
    }

    public bool ShowLockAction
    {
        get => GetValue(ShowLockActionProperty);
        set => SetValue(ShowLockActionProperty, value);
    }

    public bool ShowPiPAction
    {
        get => GetValue(ShowPiPActionProperty);
        set => SetValue(ShowPiPActionProperty, value);
    }
}
