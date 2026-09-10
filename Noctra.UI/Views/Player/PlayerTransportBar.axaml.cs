using Avalonia.Controls;
using Avalonia.Input;

namespace Noctra.UI.Views.Player;

public partial class PlayerTransportBar : UserControl
{
    public PlayerTransportBar()
    {
        InitializeComponent();
    }

    public void FocusPrimaryAction()
        => PlayPauseButton.Focus(NavigationMethod.Directional);
}
