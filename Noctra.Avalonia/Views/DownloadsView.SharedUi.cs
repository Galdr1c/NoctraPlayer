using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Noctra.Avalonia.Localization;
using SharedDownloadsContentView = Noctra.UI.Views.AdaptiveDownloadsContentView;

namespace Noctra.Avalonia.Views;

public partial class DownloadsView
{
    private SharedDownloadsContentView? _sharedDownloadsContent;

    private void InstallSharedDownloadsPresentation()
    {
        if (_sharedDownloadsContent is not null)
            return;

        if (DownloadsScrollViewer.Content is not StackPanel legacyContent)
            return;

        var tabHost = legacyContent.Children.OfType<TabControl>().FirstOrDefault();
        if (tabHost is null)
            return;

        legacyContent.Children.Remove(tabHost);

        var openFolderButton = new Button
        {
            Content = LocalizationSource.Instance["Downloads.Action.OpenFolder"],
            MinHeight = 44,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        openFolderButton.Classes.Add("SettingsActionButton");
        openFolderButton.Bind(Button.CommandProperty, new Binding("OpenDownloadsFolderCommand"));

        _sharedDownloadsContent = new SharedDownloadsContentView
        {
            ContentHost = tabHost,
            PlatformStorageActions = openFolderButton
        };
        DownloadsScrollViewer.Content = _sharedDownloadsContent;
    }
}
