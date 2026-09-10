using Avalonia;
using Avalonia.Controls;

namespace Noctra.UI.Views.Player;

public partial class PlayerChromeView : UserControl
{
    public static readonly StyledProperty<bool> ShowLockActionProperty =
        AvaloniaProperty.Register<PlayerChromeView, bool>(nameof(ShowLockAction), true);

    public static readonly StyledProperty<bool> ShowPiPActionProperty =
        AvaloniaProperty.Register<PlayerChromeView, bool>(nameof(ShowPiPAction), true);

    public static readonly StyledProperty<Thickness> SafeAreaProperty =
        AvaloniaProperty.Register<PlayerChromeView, Thickness>(nameof(SafeArea));

    static PlayerChromeView()
    {
        SafeAreaProperty.Changed.AddClassHandler<PlayerChromeView>((view, _) => view.ApplySafeArea());
    }

    public PlayerChromeView()
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

    public Thickness SafeArea
    {
        get => GetValue(SafeAreaProperty);
        set => SetValue(SafeAreaProperty, value);
    }

    public void FocusPrimaryAction() => TransportBar.FocusPrimaryAction();

    private void ApplySafeArea()
    {
        var safe = SafeArea;
        TopOverlay.Margin = new Thickness(safe.Left, safe.Top, safe.Right, 0);
        TransportBar.Margin = new Thickness(safe.Left, 0, safe.Right, safe.Bottom);
    }
}
