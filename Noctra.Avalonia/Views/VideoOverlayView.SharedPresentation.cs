using Avalonia.Controls;
using Avalonia.Threading;
using Noctra.UI.Views.Player;

namespace Noctra.Avalonia.Views;

public partial class VideoOverlayView
{
    private PlayerChromeView? _sharedPlayerChrome;
    private PlayerSheetOverlay? _sharedPlayerSheets;

    static VideoOverlayView()
    {
        DataContextProperty.Changed.AddClassHandler<VideoOverlayView>(
            (view, _) => view.QueueSharedPlayerPresentation());
    }

    private void QueueSharedPlayerPresentation()
    {
        Dispatcher.UIThread.Post(
            EnsureSharedPlayerPresentation,
            DispatcherPriority.Loaded);
    }

    private void EnsureSharedPlayerPresentation()
    {
        if (_sharedPlayerChrome is not null)
            return;

        var overlayContent = this.FindControl<Grid>("OverlayContent");
        if (overlayContent is null || overlayContent.Children.Count < 3)
            return;

        // VideoOverlayView's first three visual children are the legacy top gradient,
        // desktop-only top bar, and legacy transport/timeline surface. Keep the rest
        // (EPG, platform panels, toasts and native-window behavior) untouched.
        overlayContent.Children[0].IsVisible = false;
        overlayContent.Children[1].IsVisible = false;
        overlayContent.Children[2].IsVisible = false;

        _sharedPlayerChrome = new PlayerChromeView
        {
            ShowLockAction = false,
            ShowPiPAction = true
        };
        _sharedPlayerChrome.SetValue(Panel.ZIndexProperty, 700);

        _sharedPlayerSheets = new PlayerSheetOverlay();
        _sharedPlayerSheets.SetValue(Panel.ZIndexProperty, 1100);

        overlayContent.Children.Add(_sharedPlayerChrome);
        overlayContent.Children.Add(_sharedPlayerSheets);
    }
}
