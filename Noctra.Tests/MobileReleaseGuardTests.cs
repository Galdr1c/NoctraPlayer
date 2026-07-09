namespace Noctra.Tests;

public class MobileReleaseGuardTests
{
    [Fact]
    public void AndroidPlayer_UsesExoPlayerInsteadOfLegacyMediaPlayer()
    {
        var serviceSource = ReadProjectFile("Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("AndroidX.Media3.ExoPlayer", serviceSource);
        Assert.Contains("ExoPlayerBuilder", serviceSource);
        Assert.DoesNotContain("Android.Media.MediaPlayer", serviceSource);
        Assert.DoesNotContain("new MediaPlayer", serviceSource);
        Assert.DoesNotContain("UpdateStreamQualityFromPreparedPlayer", serviceSource);
    }

    [Fact]
    public void AndroidNotificationPermission_IsNotDeclaredWithoutRuntimeRequestFlow()
    {
        var manifest = ReadProjectFile("Noctra.Android", "Properties", "AndroidManifest.xml");
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        var manifestDeclaresPermission = manifest.Contains("android.permission.POST_NOTIFICATIONS", StringComparison.Ordinal);
        var activityRequestsPermission =
            activity.Contains("RequestPermissions", StringComparison.Ordinal) &&
            activity.Contains("android.permission.POST_NOTIFICATIONS", StringComparison.Ordinal);

        Assert.Equal(manifestDeclaresPermission, activityRequestsPermission);
    }

    [Fact]
    public void DesktopApplicationIconAsset_Exists()
    {
        var iconPath = FindProjectFile("Noctra.Avalonia", "Assets", "Noctra.ico");

        Assert.True(File.Exists(iconPath), $"Missing desktop application icon: {iconPath}");
    }

    [Fact]
    public void MobilePrimaryContentGrids_UseRowVirtualization()
    {
        var gridControl = ReadProjectFile(
            "Noctra.Mobile",
            "Controls",
            "VirtualizedResponsiveGrid.axaml");
        Assert.Contains("<VirtualizingStackPanel", gridControl);

        foreach (var viewName in new[]
                 {
                     "MobileLiveView.axaml",
                     "MobileMoviesView.axaml",
                     "MobileSeriesView.axaml",
                     "MobileFavoritesView.axaml",
                     "MobileMyListView.axaml",
                     "MobileHistoryView.axaml",
                     "MobileSearchView.axaml"
                 })
        {
            var view = ReadProjectFile("Noctra.Mobile", "Views", viewName);
            Assert.Contains("<controls:VirtualizedResponsiveGrid", view);
            Assert.DoesNotContain("<WrapPanel HorizontalAlignment=\"Center\"", view);
        }
    }

    [Fact]
    public void ProfileSetupProviderSelector_UsesEqualWidthProviderColumns()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        Assert.Contains("ColumnDefinitions=\"*,*,*\"", view);
        Assert.Contains("Classes=\"ProviderTypeOption\"", view);
        Assert.DoesNotContain("<StackPanel Orientation=\"Horizontal\"\r\n                          Spacing=\"12\"\r\n                          Margin=\"28,14,28,14\"", view);
    }

    [Fact]
    public void MobileComboBoxDropdown_DoesNotChainScrollIntoPage()
    {
        var styles = ReadProjectFile("Noctra.Mobile", "Resources", "Styles.axaml");

        Assert.Contains("MaxDropDownHeight\" Value=\"320\"", styles);
        Assert.Contains("ScrollViewer.IsScrollChainingEnabled=\"False\"", styles);
    }

    private static string ReadProjectFile(params string[] relativeParts)
        => File.ReadAllText(FindProjectFile(relativeParts));

    private static string FindProjectFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find project file: {Path.Combine(relativeParts)}");
    }
}
