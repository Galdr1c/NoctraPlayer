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
        if (sender is FrameworkElement element && 
            element.Visibility == Visibility.Visible && 
            this.TryFindResource("ShowSideSheetAnim") is System.Windows.Media.Animation.Storyboard sb)
        {
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
            if (e.ClickCount == 2)
            {
                vm.ToggleFullScreenCommand.Execute(null);
                return;
            }

            vm.UserInteractionCommand.Execute(null);
        }
    }

    private void Slider_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            vm.IsScrubbing = true;
            vm.ToggleLockCommand.Execute(null);
        }
    }

    private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            vm.IsScrubbing = false;
            vm.ToggleLockCommand.Execute(null);
            
            // Perform seek
            if (sender is Slider slider)
            {
                // Position is updated via TwoWay binding
            }
        }
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is not PlayerViewModel vm || !vm.IsScrubbing)
        {
            return;
        }

        var seconds = e.NewValue;
        vm.ScrubPreviewText = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
    }
}
