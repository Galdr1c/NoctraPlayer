using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer.Views;

public partial class VideoOverlayView : UserControl
{
    public VideoOverlayView()
    {
        InitializeComponent();
        
        AudioSettingsPanel.IsVisibleChanged += Panel_IsVisibleChanged;
        QualitySettingsPanel.IsVisibleChanged += Panel_IsVisibleChanged;
    }

    private void Panel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Visibility == Visibility.Visible)
        {
            var sb = (System.Windows.Media.Animation.Storyboard)this.Resources["ShowSideSheetAnim"];
            sb.Begin(element);
        }
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            vm.UserInteractionCommand.Execute(null);
        }
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            // Toggle visibility if not interacting with controls
            // Actually, if we click on "background", we want to toggle.
            // But if control is visible, maybe just show it?
            // Noctra behavior: Click on video pauses/plays OR shows overlay.
            // Here let's just show it. Or toggle play/pause if double click?
            
            // For now, assume single click wakes up overlay.
            vm.UserInteractionCommand.Execute(null);
            
            // If overlay was already visible, maybe toggle play/pause?
            if (vm.IsVisible)
            {
                vm.PlayPauseCommand.Execute(null);
            }
        }
    }

    private void Slider_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            vm.ToggleLockCommand.Execute(null);
        }
    }

    private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            vm.ToggleLockCommand.Execute(null);
            
            // Perform seek
            if (sender is Slider slider)
            {
                // Position is updated via TwoWay binding
            }
        }
    }
}
