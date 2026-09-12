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

        // Legacy desktop chrome and side panels are gone from the XAML: this view now
        // hosts only the shared mobile-first chrome/sheets plus the desktop-specific
        // EPG/native-window behavior that stays in this host.
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
}
