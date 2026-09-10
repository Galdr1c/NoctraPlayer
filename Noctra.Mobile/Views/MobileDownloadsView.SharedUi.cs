using System.Linq;
using Avalonia.Controls;
using SharedDownloadsContentView = Noctra.UI.Views.AdaptiveDownloadsContentView;

namespace Noctra.Mobile.Views;

public partial class MobileDownloadsView
{
    private SharedDownloadsContentView? _sharedDownloadsContent;

    private void InstallSharedDownloadsPresentation()
    {
        if (_sharedDownloadsContent is not null)
            return;

        if (PrimaryScrollContent.Content is not StackPanel legacyContent)
            return;

        // Keep the platform tab/item renderer intact. The shared layer owns only
        // the duplicated mobile-first title/storage/warning/empty presentation.
        var tabHost = legacyContent.Children.OfType<TabControl>().FirstOrDefault();
        if (tabHost is null)
            return;

        legacyContent.Children.Remove(tabHost);

        _sharedDownloadsContent = new SharedDownloadsContentView
        {
            ContentHost = tabHost
        };
        PrimaryScrollContent.Content = _sharedDownloadsContent;
    }
}
