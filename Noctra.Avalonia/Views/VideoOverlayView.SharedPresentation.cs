using Avalonia;
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

        // The first three children are the legacy top gradient, desktop top bar,
        // and desktop transport/timeline. Keep EPG/native-window behavior intact.
        overlayContent.Children[0].IsVisible = false;
        overlayContent.Children[1].IsVisible = false;
        overlayContent.Children[2].IsVisible = false;

        // Common panel states now render through the shared mobile-first sheet.
        // Suppress only their legacy desktop visuals; episodes/subtitle appearance
        // deliberately remain desktop-specific until their dependencies are shared.
        SuppressLegacyPanel("AudioSettingsPanel");
        SuppressLegacyPanel("QualitySettingsPanel");
        SuppressLegacyPanel("InfoPanel");
        SuppressLegacyPanel("SleepTimerPanel");

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

    private void SuppressLegacyPanel(string name)
    {
        var panel = this.FindControl<Grid>(name);
        if (panel is null)
            return;

        panel.Opacity = 0;
        panel.IsHitTestVisible = false;
    }
}
