using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using Noctra.ViewModels;

namespace Noctra.Views;

public partial class VideoOverlayView : UserControl
{
    private bool _isTimelineDragActive;

    public VideoOverlayView()
    {
        InitializeComponent();
        
        InfoPanel.IsVisibleChanged += Panel_IsVisibleChanged;
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
                // Double click: Toggle Fullscreen
                vm.ToggleFullScreenCommand.Execute(null);
            }
            else
            {
                // Single click: Wake up UI (Show overlay / Reset timer)
                // User requested: "Tek tık pause/play yapmıyor... Tek tık: UI’ı uyandırır"
                vm.UserInteractionCommand.Execute(null);
            }
        }
    }

    private void Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isTimelineDragActive = false;
    }

    private void Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isTimelineDragActive)
        {
            return;
        }

        if (DataContext is not PlayerViewModel vm || sender is not Slider slider)
        {
            return;
        }

        if (!vm.IsLiveContent)
        {
            vm.SeekCommand.Execute(slider.Value);
            vm.UserInteractionCommand.Execute(null);
        }
    }

    private void Slider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isTimelineDragActive = true;
        if (DataContext is PlayerViewModel vm)
        {
            vm.ToggleLockCommand.Execute(null);
        }
    }

    private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isTimelineDragActive = false;
        if (DataContext is PlayerViewModel vm)
        {
            vm.ToggleLockCommand.Execute(null);

            if (!vm.IsLiveContent && sender is Slider slider)
            {
                vm.SeekCommand.Execute(slider.Value);
                vm.UserInteractionCommand.Execute(null);
            }
        }
    }
}


