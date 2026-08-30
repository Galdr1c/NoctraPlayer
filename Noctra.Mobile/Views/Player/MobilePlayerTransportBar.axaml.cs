using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Noctra.Mobile.Views.Player;

public partial class MobilePlayerTransportBar : UserControl
{
    private const double NarrowLayoutMaxWidth = 420d;

    public static readonly StyledProperty<bool> IsNarrowLayoutProperty =
        AvaloniaProperty.Register<MobilePlayerTransportBar, bool>(nameof(IsNarrowLayout));

    public bool IsNarrowLayout
    {
        get => GetValue(IsNarrowLayoutProperty);
        private set => SetValue(IsNarrowLayoutProperty, value);
    }

    public MobilePlayerTransportBar()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        IsNarrowLayout = UsesNarrowLayout(e.NewSize.Width);
    }

    private static bool UsesNarrowLayout(double width)
        => width > 0d && width <= NarrowLayoutMaxWidth;

    public void FocusPrimaryAction()
        => PlayPauseButton.Focus(NavigationMethod.Directional);
}
