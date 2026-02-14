using System.Windows;

namespace Noctra.Views;

public partial class PlayerOverlayWindow : Window
{
    public PlayerOverlayWindow()
    {
        InitializeComponent();
    }

    public VideoOverlayView OverlayView => OverlayViewControl;
}
