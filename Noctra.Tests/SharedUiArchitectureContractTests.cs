namespace Noctra.Tests;

public sealed class SharedUiArchitectureContractTests
{
    [Fact]
    public void SharedUiProject_IsCanonicalAndReferencedByBothPresentationHosts()
    {
        var root = FindSolutionRoot();
        var sharedProjectPath = Path.Combine(root, "Noctra.UI", "Noctra.UI.csproj");

        Assert.True(
            File.Exists(sharedProjectPath),
            "The platform-neutral Noctra.UI project must exist before UI is shared.");

        var sharedProject = File.ReadAllText(sharedProjectPath);
        var mobileProject = File.ReadAllText(
            Path.Combine(root, "Noctra.Mobile", "Noctra.Mobile.csproj"));
        var desktopProject = File.ReadAllText(
            Path.Combine(root, "Noctra.Avalonia", "Noctra.Avalonia.csproj"));
        var solution = File.ReadAllText(Path.Combine(root, "NoctraPlayer.sln"));

        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", sharedProject, StringComparison.Ordinal);
        Assert.Contains("..\\Noctra.Core\\Noctra.Core.csproj", sharedProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Noctra.Mobile.csproj", sharedProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Noctra.Avalonia.csproj", sharedProject, StringComparison.Ordinal);

        Assert.Contains("..\\Noctra.UI\\Noctra.UI.csproj", mobileProject, StringComparison.Ordinal);
        Assert.Contains("..\\Noctra.UI\\Noctra.UI.csproj", desktopProject, StringComparison.Ordinal);
        Assert.Contains("\"Noctra.UI\", \"Noctra.UI\\Noctra.UI.csproj\"", solution, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(320d, "Compact", "Bottom", 16d, 320d)]
    [InlineData(599.99d, "Compact", "Bottom", 16d, 599.99d)]
    [InlineData(600d, "Medium", "CollapsibleRail", 24d, 560d)]
    [InlineData(1023.99d, "Medium", "CollapsibleRail", 24d, 560d)]
    [InlineData(1024d, "Expanded", "Rail", 32d, 640d)]
    public void AdaptiveMetrics_UseOneResponsivePolicy(
        double width,
        string expectedClass,
        string expectedNavigation,
        double expectedPadding,
        double expectedSheetWidth)
    {
        var assembly = System.Reflection.Assembly.Load("Noctra.UI");
        var metricsType = assembly.GetType("Noctra.UI.Layout.AdaptiveLayoutMetrics");

        Assert.NotNull(metricsType);

        var forWidth = metricsType.GetMethod(
            "ForWidth",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(forWidth);

        var metrics = forWidth.Invoke(null, [width]);
        Assert.NotNull(metrics);

        Assert.Equal(expectedClass, ReadProperty(metrics, "LayoutClass")?.ToString());
        Assert.Equal(expectedNavigation, ReadProperty(metrics, "Navigation")?.ToString());
        Assert.Equal(expectedPadding, Assert.IsType<double>(ReadProperty(metrics, "PagePadding")));
        Assert.Equal(44d, Assert.IsType<double>(ReadProperty(metrics, "MinimumTouchTargetSize")));
        Assert.Equal(expectedSheetWidth, Assert.IsType<double>(ReadProperty(metrics, "MaximumSheetWidth")));
    }

    [Fact]
    public void BothHosts_LoadMobileFirstTokensAndColorsFromSharedAssembly()
    {
        var root = FindSolutionRoot();
        var sharedTokensPath = Path.Combine(root, "Noctra.UI", "Resources", "Tokens.axaml");
        var sharedColorsPath = Path.Combine(root, "Noctra.UI", "Resources", "Colors.axaml");

        Assert.True(File.Exists(sharedTokensPath), "Shared mobile-first tokens must be canonical.");
        Assert.True(File.Exists(sharedColorsPath), "Shared brand colors must be canonical.");

        var sharedTokens = File.ReadAllText(sharedTokensPath);
        Assert.Contains("x:Key=\"FWatermark\">30<", sharedTokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TouchTarget\">44<", sharedTokens, StringComparison.Ordinal);

        foreach (var appPath in new[]
                 {
                     Path.Combine(root, "Noctra.Mobile", "App.axaml"),
                     Path.Combine(root, "Noctra.Avalonia", "App.axaml")
                 })
        {
            var app = File.ReadAllText(appPath);
            Assert.Contains(
                "avares://Noctra.UI/Resources/Tokens.axaml",
                app,
                StringComparison.Ordinal);
            Assert.Contains(
                "avares://Noctra.UI/Resources/Colors.axaml",
                app,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DesktopOverrides_ContainLayoutAliasesWithoutDuplicatingCanonicalTokens()
    {
        var root = FindSolutionRoot();
        var desktopOverridesPath = Path.Combine(
            root,
            "Noctra.Avalonia",
            "Resources",
            "DesktopAdaptiveTokens.axaml");

        Assert.True(
            File.Exists(desktopOverridesPath),
            "Desktop-only expanded-layout aliases must be isolated from canonical tokens.");

        var overrides = File.ReadAllText(desktopOverridesPath);
        Assert.Contains("x:Key=\"DesktopPagePadding\"", overrides, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DesktopSheetMaxWidth\"", overrides, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"FWatermark\"", overrides, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"S1\"", overrides, StringComparison.Ordinal);

        var desktopApp = File.ReadAllText(
            Path.Combine(root, "Noctra.Avalonia", "App.axaml"));
        Assert.Contains(
            "avares://Noctra/Resources/DesktopAdaptiveTokens.axaml",
            desktopApp,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(360d, "Vod", 2, 172d)]
    [InlineData(360d, "Series", 2, 172d)]
    [InlineData(360d, "Live", 1, 360d)]
    [InlineData(360d, "ContinueWatching", 1, 360d)]
    [InlineData(1200d, "Vod", 6, 180d)]
    [InlineData(1200d, "Live", 4, 288d)]
    public void CardGridMetrics_FollowMobileProfilesOnEveryHost(
        double width,
        string cardKind,
        int expectedColumns,
        double expectedCardWidth)
    {
        var assembly = System.Reflection.Assembly.Load("Noctra.UI");
        var metricsType = assembly.GetType("Noctra.UI.Layout.AdaptiveCardGridMetrics");
        var kindType = assembly.GetType("Noctra.UI.Layout.AdaptiveCardGridKind");

        Assert.NotNull(metricsType);
        Assert.NotNull(kindType);

        var calculate = metricsType.GetMethod(
            "Calculate",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(calculate);

        var kind = Enum.Parse(kindType, cardKind);
        var metrics = calculate.Invoke(null, [width, kind]);
        Assert.NotNull(metrics);

        Assert.Equal(expectedColumns, Assert.IsType<int>(ReadProperty(metrics, "Columns")));
        Assert.Equal(expectedCardWidth, Assert.IsType<double>(ReadProperty(metrics, "CardWidth")));
    }

    [Fact]
    public void MobileAndDesktopGrids_UseTheSharedCardMetricCalculator()
    {
        var root = FindSolutionRoot();
        var mobile = File.ReadAllText(Path.Combine(
            root, "Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs"));
        var desktop = File.ReadAllText(Path.Combine(
            root, "Noctra.Avalonia", "Controls", "DesktopVirtualizingCardGrid.cs"));

        Assert.Contains("AdaptiveCardGridMetrics.Calculate", mobile, StringComparison.Ordinal);
        Assert.Contains("AdaptiveCardGridMetrics.Calculate", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("new GridProfile", mobile, StringComparison.Ordinal);
        Assert.DoesNotContain("new GridProfile", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePresentation_IsSharedWhileEachHostKeepsItsVirtualizedGrid()
    {
        var root = FindSolutionRoot();
        var sharedViewPath = Path.Combine(root, "Noctra.UI", "Views", "HomeContentView.axaml");
        Assert.True(File.Exists(sharedViewPath), "Home's visible presentation must have one canonical XAML source.");

        var shared = File.ReadAllText(sharedViewPath);
        Assert.Contains("x:Class=\"Noctra.UI.Views.HomeContentView\"", shared, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource FHeadline}", shared, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsPresenter\"", shared, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EmptyState\"", shared, StringComparison.Ordinal);
        Assert.Contains("Text=\"{loc:Translate Home.Welcome}\"", shared, StringComparison.Ordinal);

        var mobile = File.ReadAllText(Path.Combine(
            root, "Noctra.Mobile", "Views", "MobileHomeView.axaml"));
        var desktop = File.ReadAllText(Path.Combine(
            root, "Noctra.Avalonia", "Views", "HomeView.axaml"));

        Assert.Contains("<shared:HomeContentView", mobile, StringComparison.Ordinal);
        Assert.Contains("<controls:MobileVirtualizingCardGrid", mobile, StringComparison.Ordinal);
        Assert.Contains("Home.EmptyState.Description", shared, StringComparison.Ordinal);

        Assert.Contains("<shared:HomeContentView", desktop, StringComparison.Ordinal);
        Assert.Contains("<controls:DesktopVirtualizingCardGrid", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("<WrapPanel", desktop, StringComparison.Ordinal);

        Assert.Equal(0, CountOccurrences(mobile + desktop, "Home.Welcome"));
    }

    [Fact]
    public void BothHosts_LoadMobileFirstStylesAndSpinnerFromSharedAssembly()
    {
        var root = FindSolutionRoot();
        foreach (var relativePath in new[]
                 {
                     Path.Combine("Resources", "CommonStyles.axaml"),
                     Path.Combine("Resources", "SettingsStyles.axaml"),
                     Path.Combine("Controls", "PremiumSpinner.axaml")
                 })
        {
            Assert.True(
                File.Exists(Path.Combine(root, "Noctra.UI", relativePath)),
                $"Missing canonical shared UI resource: {relativePath}");
        }

        foreach (var appPath in new[]
                 {
                     Path.Combine(root, "Noctra.Mobile", "App.axaml"),
                     Path.Combine(root, "Noctra.Avalonia", "App.axaml")
                 })
        {
            var app = File.ReadAllText(appPath);
            Assert.Contains("avares://Noctra.UI/Resources/CommonStyles.axaml", app, StringComparison.Ordinal);
            Assert.Contains("avares://Noctra.UI/Resources/SettingsStyles.axaml", app, StringComparison.Ordinal);
            Assert.Contains("avares://Noctra.UI/Controls/PremiumSpinner.axaml", app, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CatalogPages_ShareOneMobileFirstPresentationAndKeepOnlyHostOverlays()
    {
        var root = FindSolutionRoot();
        var sharedPath = Path.Combine(root, "Noctra.UI", "Views", "AdaptiveCatalogView.axaml");
        Assert.True(File.Exists(sharedPath), "Live, Movies and Series must share one catalog presentation.");

        var shared = File.ReadAllText(sharedPath);
        Assert.Contains("RowDefinitions=\"Auto,Auto,*\"", shared, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SortSelectionButton\"", shared, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CategorySelectionButton\"", shared, StringComparison.Ordinal);
        Assert.Contains("<sharedControls:PremiumSpinner", shared, StringComparison.Ordinal);

        foreach (var (project, view, grid) in new[]
                 {
                     ("Noctra.Mobile", "MobileLiveView.axaml", "MobileVirtualizingCardGrid"),
                     ("Noctra.Mobile", "MobileMoviesView.axaml", "MobileVirtualizingCardGrid"),
                     ("Noctra.Mobile", "MobileSeriesView.axaml", "MobileVirtualizingCardGrid"),
                     ("Noctra.Avalonia", "LiveView.axaml", "DesktopVirtualizingCardGrid"),
                     ("Noctra.Avalonia", "MoviesView.axaml", "DesktopVirtualizingCardGrid"),
                     ("Noctra.Avalonia", "SeriesView.axaml", "DesktopVirtualizingCardGrid")
                 })
        {
            var host = File.ReadAllText(Path.Combine(root, project, "Views", view));
            Assert.Contains("<shared:AdaptiveCatalogView", host, StringComparison.Ordinal);
            Assert.Contains($"<controls:{grid}", host, StringComparison.Ordinal);
            Assert.DoesNotContain("RowDefinitions=\"Auto,Auto,*\"", host, StringComparison.Ordinal);
            Assert.DoesNotContain("<controls:PremiumSpinner", host, StringComparison.Ordinal);
        }
    }

    private static object? ReadProperty(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name);
        Assert.NotNull(property);
        return property.GetValue(instance);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "NoctraPlayer.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate NoctraPlayer.sln.");
    }
}
