using Avalonia.Controls;

namespace Noctra.Mobile.Views;

public partial class MobilePlayerCompactControls : UserControl
{
    public MobilePlayerCompactControls()
    {
        InitializeComponent();
    }

    public void FocusPrimaryAction()
        => TransportBar.FocusPrimaryAction();
}
