using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using Noctra.Models;
using Noctra.UI.Views.Player;
using DesktopRemoteImage = Noctra.Avalonia.Controls.RemoteImage;

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
        if (overlayContent is null)
            return;

        // Keep EPG/native-window behavior intact while replacing only the three
        // explicitly named legacy presentation layers.
        LegacyTopGradient.IsVisible = false;
        LegacyTopBar.IsVisible = false;
        LegacyTransportControls.IsVisible = false;

        // Common panel states render through the shared mobile-first sheet.
        // Their host-specific native/input behavior remains in this desktop view.
        SuppressLegacyPanel("AudioSettingsPanel");
        SuppressLegacyPanel("QualitySettingsPanel");
        SuppressLegacyPanel("InfoPanel");
        SuppressLegacyPanel("EpisodesPanel");
        SuppressLegacyPanel("SleepTimerPanel");

        _sharedPlayerChrome = new PlayerChromeView
        {
            ShowLockAction = false,
            ShowPiPAction = true
        };
        _sharedPlayerChrome.SetValue(Panel.ZIndexProperty, 700);

        _sharedPlayerSheets = new PlayerSheetOverlay
        {
            EpisodeThumbnailTemplate = new FuncDataTemplate<Episode>(
                (episode, _) => episode is null
                    ? null
                    : new DesktopRemoteImage
                    {
                        Url = episode.CoverUrl,
                        Stretch = Stretch.UniformToFill
                    },
                supportsRecycling: false)
        };
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
