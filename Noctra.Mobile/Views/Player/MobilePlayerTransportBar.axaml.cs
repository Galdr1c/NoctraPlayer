using Avalonia.Controls;
using Avalonia.Input;

namespace Noctra.Mobile.Views.Player;

public partial class MobilePlayerTransportBar : UserControl
{
    public MobilePlayerTransportBar()
    {
        InitializeComponent();
    }

    public void FocusPrimaryAction()
        => PlayPauseButton.Focus(NavigationMethod.Directional);
}
