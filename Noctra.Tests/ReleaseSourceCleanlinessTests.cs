using System.Text.Json;
using System.Xml.Linq;

namespace Noctra.Tests;

public sealed class ReleaseSourceCleanlinessTests
{
    [Fact]
    public void BuildConfiguration_PinsDotNet10AndOneAvaloniaVersion()
    {
        var repositoryRoot = FindRepositoryRoot();
        var globalJson = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));
        var sdkVersion = globalJson.RootElement
            .GetProperty("sdk")
            .GetProperty("version")
            .GetString();

        Assert.Equal("10.0.301", sdkVersion);

        var directoryBuildProps = XDocument.Load(
            Path.Combine(repositoryRoot, "Directory.Build.props"));
        var avaloniaVersion = directoryBuildProps
            .Descendants("AvaloniaVersion")
            .SingleOrDefault()
            ?.Value;

        Assert.Equal("12.0.4", avaloniaVersion);

        var projectFiles = Directory.EnumerateFiles(
            repositoryRoot,
            "*.csproj",
            SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        var avaloniaReferences = projectFiles
            .SelectMany(path => XDocument.Load(path)
                .Descendants("PackageReference")
                .Where(reference => reference.Attribute("Include")?.Value
                    .StartsWith("Avalonia", StringComparison.Ordinal) == true)
                .Select(reference => new
                {
                    Path = path,
                    Version = reference.Attribute("Version")?.Value
                }))
            .ToList();

        Assert.NotEmpty(avaloniaReferences);
        Assert.All(avaloniaReferences, reference =>
            Assert.Equal("$(AvaloniaVersion)", reference.Version));
    }

    [Fact]
    public void ProductionSources_DoNotContainPerformanceTraceInstrumentation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionRoots = new[]
        {
            Path.Combine(repositoryRoot, "Noctra.Core"),
            Path.Combine(repositoryRoot, "Noctra.Avalonia")
        };
        var forbiddenTerms = new[]
        {
            "PerformanceTraceService",
            "IPerformanceTraceService",
            "PerfTraceDirectory",
            "NOCTRA_PERF_TRACE_DIR",
            "_perfTrace",
            "perfTrace"
        };

        var matches = productionRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 })
                .Where(entry => forbiddenTerms.Any(term =>
                    entry.Line.Contains(term, StringComparison.Ordinal))))
            .ToList();

        Assert.Empty(matches);
    }

    [Fact]
    public void SharedVideoPlayerContract_DoesNotExposeLibVlcTypes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contractPath = Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Services",
            "Interfaces",
            "IVideoPlayerService.cs");
        var source = File.ReadAllText(contractPath);

        Assert.DoesNotContain("LibVLCSharp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaPlayerReady", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMediaPlayer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidShell_UsesSeparateProjectsAndSupportedApiLevels()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobileProjectPath = Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Noctra.Mobile.csproj");
        var androidProjectPath = Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Noctra.Android.csproj");

        Assert.True(File.Exists(mobileProjectPath));
        Assert.True(File.Exists(androidProjectPath));

        var mobileProject = XDocument.Load(mobileProjectPath);
        Assert.Equal(
            "net10.0",
            mobileProject.Descendants("TargetFramework").Single().Value);

        var androidProject = XDocument.Load(androidProjectPath);
        Assert.Equal(
            "net10.0-android36.0",
            androidProject.Descendants("TargetFramework").Single().Value);
        Assert.Equal(
            "31",
            androidProject.Descendants("SupportedOSPlatformVersion").Single().Value);

        var projectReferences = androidProject
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ToList();

        Assert.Contains(@"..\Noctra.Mobile\Noctra.Mobile.csproj", projectReferences);
        Assert.Contains(@"..\Noctra.Core\Noctra.Core.csproj", projectReferences);
    }

    [Fact]
    public void SharedDependencies_PinPatchedCachingMemoryPackage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreProject = XDocument.Load(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Noctra.Core.csproj"));
        var cachingMemoryReference = coreProject
            .Descendants("PackageReference")
            .SingleOrDefault(reference =>
                reference.Attribute("Include")?.Value ==
                "Microsoft.Extensions.Caching.Memory");

        Assert.NotNull(cachingMemoryReference);
        Assert.Equal("8.0.1", cachingMemoryReference.Attribute("Version")?.Value);
    }

    [Fact]
    public void SharedDependencies_PinAndroid16CompatibleSqliteBundle()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreProject = XDocument.Load(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Noctra.Core.csproj"));
        var sqliteBundleReference = coreProject
            .Descendants("PackageReference")
            .SingleOrDefault(reference =>
                reference.Attribute("Include")?.Value ==
                "SQLitePCLRaw.bundle_e_sqlite3");

        Assert.NotNull(sqliteBundleReference);
        Assert.Equal("2.1.11", sqliteBundleReference.Attribute("Version")?.Value);
    }

    [Fact]
    public void MobileShell_DefinesFiveDestinationsAndResponsiveNavigation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var codeSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("x:Name=\"NavigationRail\"", viewSource);
        Assert.Contains("x:Name=\"BottomNavigation\"", viewSource);
        Assert.Contains("Tag=\"Home\"", viewSource);
        Assert.Contains("Tag=\"Live\"", viewSource);
        Assert.Contains("Tag=\"Movies\"", viewSource);
        Assert.Contains("Tag=\"Series\"", viewSource);
        Assert.Contains("Tag=\"More\"", viewSource);
        Assert.Contains("Button.nav.active", viewSource);
        Assert.Contains("UpdateNavigationSelection(destination)", codeSource);
        Assert.Contains("button.Classes.Set(\"active\"", codeSource);
        Assert.Contains("TabletBreakpoint = 720", codeSource);
        Assert.Contains("NavigationRail.IsVisible = useNavigationRail", codeSource);
        Assert.Contains("BottomNavigation.IsVisible = !useNavigationRail", codeSource);
        Assert.Contains("CanShowNavigationChrome()", codeSource);
        Assert.Contains("!ProfilesOverlay.IsVisible", codeSource);
        Assert.Contains("!LegalConsentOverlay.IsVisible", codeSource);
        Assert.Contains("DispatcherPriority.Loaded", codeSource);
        Assert.Contains("MaxProfileSelectionRetries", codeSource);
        Assert.Contains("Loaded += OnLoaded", codeSource);
        Assert.Contains("StartStartupFlow", codeSource);
    }

    [Fact]
    public void MobileApp_LoadsMaterialIconStyles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));

        Assert.Contains("xmlns:materialIcons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", appSource);
        Assert.Contains("<materialIcons:MaterialIconStyles />", appSource);
    }

    [Fact]
    public void MobileViews_UseStringMaterialIconKinds()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobileViewSources = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "Noctra.Mobile"), "*.axaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        foreach (var source in mobileViewSources)
        {
            Assert.DoesNotContain("x:Static materialIcons:MaterialIconKind", source);
            Assert.DoesNotContain("xmlns:materialIcons=\"clr-namespace:Material.Icons;assembly=Material.Icons\"", source);
        }
    }

    [Fact]
    public void MobileViews_UseConverterParametersForLocalizedStringFormats()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobileViewSources = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "Noctra.Mobile"), "*.axaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        foreach (var source in mobileViewSources)
        {
            Assert.DoesNotContain("StringFormat={loc:Translate", source);
        }
    }

    [Fact]
    public void MobileViews_UseMobileAssemblyAssetUris()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobileViewSources = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "Noctra.Mobile"), "*.axaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        foreach (var source in mobileViewSources)
        {
            Assert.DoesNotContain("avares://Noctra/Assets/", source);
        }
    }

    [Fact]
    public void MobileShellAndPlayer_UseMaterialIconsForPrimaryControls()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", mainViewSource);
        Assert.Contains("Kind=\"HomeVariantOutline\"", mainViewSource);
        Assert.Contains("Kind=\"Television\"", mainViewSource);
        Assert.Contains("Kind=\"Magnify\"", mainViewSource);
        Assert.Contains("Kind=\"DotsHorizontal\"", mainViewSource);

        Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", playerViewSource);
        Assert.Contains("Kind=\"Play\"", playerViewSource);
        Assert.Contains("Kind=\"Stop\"", playerViewSource);
        Assert.Contains("Kind=\"VolumeOff\"", playerViewSource);
        Assert.Contains("Kind=\"PictureInPictureBottomRight\"", playerViewSource);
    }

    [Fact]
    public void MobileShellAndPlayer_UsePhoneSafeControlLayouts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("ColumnDefinitions=\"*,*,*,*,*\"", mainViewSource);
        Assert.DoesNotContain("ColumnDefinitions=\"*,*,*,*,*,*,*\"", mainViewSource);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", mainViewSource);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"56\" />", mainViewSource);

        Assert.DoesNotContain("ColumnDefinitions=\"*,*,*,*,*\"", playerViewSource);
        Assert.Contains("<WrapPanel", playerViewSource);
        Assert.Contains("Classes=\"compactPlayerAction\"", playerViewSource);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"96\" />", playerViewSource);
    }

    [Fact]
    public void MobileDetailScreens_UseMaterialIconsForPrimaryActions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var downloadsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileDownloadsView.axaml"));
        var seriesDetailSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSeriesDetailView.axaml"));

        Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", downloadsSource);
        Assert.Contains("Kind=\"Harddisk\"", downloadsSource);
        Assert.Contains("Kind=\"FolderOpenOutline\"", downloadsSource);
        Assert.Contains("Kind=\"TrashCanOutline\"", downloadsSource);

        Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", seriesDetailSource);
        Assert.Contains("Kind=\"ArrowLeft\"", seriesDetailSource);
        Assert.Contains("Kind=\"Play\"", seriesDetailSource);
        Assert.Contains("Kind=\"Youtube\"", seriesDetailSource);
        Assert.Contains("Kind=\"DownloadOutline\"", seriesDetailSource);
    }

    [Fact]
    public void MobileDownloadsView_ReusesDesktopMaterialIcons()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileDownloadsView.axaml"));

        Assert.Contains("Kind=\"Harddisk\"", source);
        Assert.Contains("Kind=\"AlertCircleOutline\"", source);
        Assert.Contains("Kind=\"DownloadOffOutline\"", source);
        Assert.Contains("Kind=\"Speedometer\"", source);
        Assert.Contains("Kind=\"PlayCircleOutline\"", source);
        Assert.Contains("Kind=\"ChevronRight\"", source);
        Assert.Contains("Kind=\"Close\"", source);
        Assert.Contains("ConverterParameter='Play|Pause'", source);
        Assert.Contains("Downloads.Storage.Warning", source);
        Assert.Contains("ShowStorageWarning", source);
    }

    [Fact]
    public void MobileSettingsView_ReusesDesktopMaterialIcons()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSettingsView.axaml"));

        Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", source);
        Assert.Contains("Kind=\"AccountBoxOutline\"", source);
        Assert.Contains("Kind=\"PlayBoxOutline\"", source);
        Assert.Contains("Kind=\"FormatListBulletedSquare\"", source);
        Assert.Contains("Kind=\"EyeOutline\"", source);
        Assert.Contains("Kind=\"CalendarClock\"", source);
        Assert.Contains("Kind=\"Lock\"", source);
        Assert.Contains("Kind=\"Plus\"", source);
        Assert.Contains("Kind=\"AlertCircleOutline\"", source);
        Assert.Contains("Kind=\"DeleteOutline\"", source);
        Assert.Contains("Kind=\"Refresh\"", source);
        Assert.Contains("Kind=\"ShieldAccountOutline\"", source);
        Assert.Contains("Kind=\"BrushVariant\"", source);
        Assert.Contains("Kind=\"Crown\"", source);
        Assert.Contains("Kind=\"Update\"", source);
        Assert.Contains("Kind=\"RocketLaunch\"", source);
        Assert.Contains("Kind=\"BugOutline\"", source);
    }

    [Fact]
    public void MobileCollectionScreens_UseMaterialIconsForMediaFallbacks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var liveCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileLiveTvCard.axaml"));
        var seriesCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileSeriesCard.axaml"));
        var vodCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileVodCard.axaml"));
        var collectionViewPaths = new[]
        {
            Path.Combine(repositoryRoot, "Noctra.Mobile", "Views", "MobileFavoritesView.axaml"),
            Path.Combine(repositoryRoot, "Noctra.Mobile", "Views", "MobileMyListView.axaml"),
            Path.Combine(repositoryRoot, "Noctra.Mobile", "Views", "MobileHistoryView.axaml")
        };

        foreach (var viewPath in collectionViewPaths)
        {
            var source = File.ReadAllText(viewPath);

            Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", source);
            Assert.Contains("<controls:MobileLiveTvCard", source);
            Assert.Contains("<controls:MobileSeriesCard", source);
            Assert.Contains("<controls:MobileVodCard", source);
            Assert.DoesNotContain("Text=\"TV\"", source);
            Assert.DoesNotContain("Text=\"EP\"", source);
            Assert.DoesNotContain("Text=\"VOD\"", source);
        }

        Assert.Contains("Kind=\"Television\"", liveCardSource);
        Assert.Contains("Kind=\"TelevisionPlay\"", seriesCardSource);
        Assert.Contains("Kind=\"Movie\"", vodCardSource);
    }

    [Fact]
    public void MobileLiveView_UsesMaterialIconForChannelFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLiveView.axaml"));

        Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", source);
        Assert.Contains("Kind=\"Television\"", source);
        Assert.DoesNotContain("Text=\"TV\"", source);
    }

    [Fact]
    public void MobileMediaCards_UseMaterialIconsForPosterFallbacks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cardExpectations = new[]
        {
            new
            {
                Path = Path.Combine(repositoryRoot, "Noctra.Mobile", "Controls", "MobileVodCard.axaml"),
                Icon = "Kind=\"Movie\""
            },
            new
            {
                Path = Path.Combine(repositoryRoot, "Noctra.Mobile", "Controls", "MobileSeriesCard.axaml"),
                Icon = "Kind=\"TelevisionPlay\""
            },
            new
            {
                Path = Path.Combine(repositoryRoot, "Noctra.Mobile", "Controls", "MobileContinueWatchingCard.axaml"),
                Icon = "Kind=\"MoviePlay\""
            }
        };

        foreach (var expectation in cardExpectations)
        {
            var source = File.ReadAllText(expectation.Path);

            Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", source);
            Assert.Contains(expectation.Icon, source);
            Assert.Contains("Opacity=\"0.32\"", source);
        }
    }

    [Fact]
    public void MobileMediaCards_ExposeVisibleOverflowActions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cardPaths = new[]
        {
            Path.Combine(repositoryRoot, "Noctra.Mobile", "Controls", "MobileVodCard.axaml"),
            Path.Combine(repositoryRoot, "Noctra.Mobile", "Controls", "MobileSeriesCard.axaml"),
            Path.Combine(repositoryRoot, "Noctra.Mobile", "Controls", "MobileContinueWatchingCard.axaml"),
            Path.Combine(repositoryRoot, "Noctra.Mobile", "Controls", "MobileLiveTvCard.axaml")
        };

        foreach (var cardPath in cardPaths)
        {
            var source = File.ReadAllText(cardPath);

            Assert.Contains("ActionsFlyout", source);
            Assert.Contains("Flyout=\"{StaticResource", source);
            Assert.Contains("Kind=\"DotsVertical\"", source);
            Assert.Contains("Context.Favorite.Toggle", source);
        }

        // Poster-style cards still expose the MyList bookmark action.
        foreach (var cardPath in cardPaths.Where(p => !p.Contains("LiveTvCard")))
        {
            var source = File.ReadAllText(cardPath);
            Assert.Contains("Context.MyList.Toggle", source);
        }
    }

    [Fact]
    public void MobileHistoryCards_ExposeRemoveFromHistoryAction()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobileVodCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileVodCard.axaml"));
        var mobileVodCardCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileVodCard.axaml.cs"));
        var mobileSeriesCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileSeriesCard.axaml"));
        var mobileSeriesCardCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileSeriesCard.axaml.cs"));
        var mobileHistorySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileHistoryView.axaml"));

        Assert.Contains("ShowHistoryMenuProperty", mobileVodCardCode);
        Assert.Contains("History.Remove", mobileVodCardSource);
        Assert.Contains("RemoveFromHistoryCommand", mobileVodCardSource);
        Assert.Contains("Kind=\"DeleteOutline\"", mobileVodCardSource);
        Assert.Contains("IsVisible=\"{Binding #VodCardControl.ShowHistoryMenu}\"", mobileVodCardSource);

        Assert.Contains("ShowHistoryMenuProperty", mobileSeriesCardCode);
        Assert.Contains("History.Remove", mobileSeriesCardSource);
        Assert.Contains("RemoveFromHistoryCommand", mobileSeriesCardSource);
        Assert.Contains("Kind=\"DeleteOutline\"", mobileSeriesCardSource);
        Assert.Contains("IsVisible=\"{Binding #SeriesCardControl.ShowHistoryMenu}\"", mobileSeriesCardSource);

        Assert.Contains("<controls:MobileSeriesCard", mobileHistorySource);
        Assert.Contains("<controls:MobileVodCard", mobileHistorySource);
        Assert.Contains("ShowHistoryMenu=\"True\"", mobileHistorySource);
        Assert.DoesNotContain("ItemsSource=\"{Binding HistorySeriesItems}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Series\">\r\n              <Button", mobileHistorySource);
        Assert.DoesNotContain("ItemsSource=\"{Binding HistoryVodChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Channel\">\r\n              <Button", mobileHistorySource);
    }

    [Fact]
    public void MobilePersonalLibraryViews_ReusePosterCardsForVodAndSeries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var favoritesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileFavoritesView.axaml"));
        var favoritesCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileFavoritesView.axaml.cs"));
        var myListSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMyListView.axaml"));
        var myListCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMyListView.axaml.cs"));

        Assert.Contains("<controls:MobileSeriesCard", favoritesSource);
        Assert.Contains("<controls:MobileVodCard", favoritesSource);
        Assert.Contains("ItemsSource=\"{Binding FavoriteSeriesItems}\"", favoritesSource);
        Assert.Contains("ItemsSource=\"{Binding FavoriteVodChannels}\"", favoritesSource);
        Assert.DoesNotContain("ItemsSource=\"{Binding FavoriteSeriesItems}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Series\">\r\n              <Button", favoritesSource);
        Assert.DoesNotContain("ItemsSource=\"{Binding FavoriteVodChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Channel\">\r\n              <Button", favoritesSource);

        Assert.Contains("<controls:MobileSeriesCard", myListSource);
        Assert.Contains("<controls:MobileVodCard", myListSource);
        Assert.Contains("ItemsSource=\"{Binding MyListSeriesItems}\"", myListSource);
        Assert.Contains("ItemsSource=\"{Binding MyListVodChannels}\"", myListSource);
        Assert.DoesNotContain("ItemsSource=\"{Binding MyListSeriesItems}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Series\">\r\n              <Button", myListSource);
        Assert.DoesNotContain("ItemsSource=\"{Binding MyListVodChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Channel\">\r\n              <Button", myListSource);
    }

    [Fact]
    public void MobileLiveTvViews_ReuseSharedLiveTvCard()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cardPath = Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileLiveTvCard.axaml");
        var liveSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLiveView.axaml"));
        var historySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileHistoryView.axaml"));
        var favoritesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileFavoritesView.axaml"));
        var favoritesCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileFavoritesView.axaml.cs"));
        var myListSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMyListView.axaml"));
        var myListCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMyListView.axaml.cs"));

        Assert.True(File.Exists(cardPath));

        var cardSource = File.ReadAllText(cardPath);
        Assert.Contains("x:Class=\"Noctra.Mobile.Controls.MobileLiveTvCard\"", cardSource);
        Assert.Contains("SelectMediaCommand", cardSource);
        Assert.Contains("ToggleFavoriteCommand", cardSource);
        Assert.Contains("CurrentProgramTitle", cardSource);
        Assert.Contains("EpgProgress", cardSource);
        Assert.Contains("Kind=\"HeartOutline\"", cardSource);

        Assert.Contains("<controls:MobileLiveTvCard", liveSource);
        Assert.Contains("<controls:MobileLiveTvCard", historySource);
        Assert.Contains("<controls:MobileLiveTvCard", favoritesSource);
        Assert.Contains("<controls:MobileLiveTvCard", myListSource);

        Assert.DoesNotContain("ItemsSource=\"{Binding HistoryLiveChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Channel\">\r\n              <Button", historySource);
        Assert.DoesNotContain("ItemsSource=\"{Binding FavoriteLiveChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Channel\">\r\n              <Button", favoritesSource);
        Assert.DoesNotContain("ItemsSource=\"{Binding MyListLiveChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Channel\">\r\n              <Button", myListSource);
    }

    [Fact]
    public void MobileSearchView_ReusesSharedMediaCards()
    {
        var repositoryRoot = FindRepositoryRoot();
        var searchSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSearchView.axaml"));

        Assert.Contains("<controls:MobileLiveTvCard", searchSource);
        Assert.Contains("<controls:MobileSeriesCard", searchSource);
        Assert.Contains("<controls:MobileVodCard", searchSource);
        Assert.Contains("ItemsSource=\"{Binding SearchLiveChannels}\"", searchSource);
        Assert.Contains("ItemsSource=\"{Binding SearchSeriesChannels}\"", searchSource);
        Assert.Contains("ItemsSource=\"{Binding SearchVodChannels}\"", searchSource);
        Assert.Contains("ItemsSource=\"{Binding SearchSimilarLiveChannels}\"", searchSource);
        Assert.Contains("ItemsSource=\"{Binding SearchSimilarSeriesChannels}\"", searchSource);
        Assert.Contains("ItemsSource=\"{Binding SearchSimilarVodChannels}\"", searchSource);
        Assert.Contains("ResponsiveCardMetricConverter", searchSource);

        Assert.DoesNotContain("ItemsSource=\"{Binding SearchLiveChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Channel\">\r\n              <Button", searchSource);
        Assert.DoesNotContain("ItemsSource=\"{Binding SearchSeriesChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Series\">\r\n              <Button", searchSource);
        Assert.DoesNotContain("ItemsSource=\"{Binding SearchVodChannels}\">\r\n          <ItemsControl.ItemTemplate>\r\n            <DataTemplate x:DataType=\"models:Channel\">\r\n              <Button", searchSource);
    }

    [Fact]
    public void MobilePosterGrids_UseFlexibleCardSizing()
    {
        var repositoryRoot = FindRepositoryRoot();
        var moviesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMoviesView.axaml"));
        var seriesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSeriesView.axaml"));
        var vodCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileVodCard.axaml"));
        var seriesCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileSeriesCard.axaml"));

        Assert.DoesNotContain("Width=\"154\"", vodCardSource);
        Assert.DoesNotContain("Width=\"154\"", seriesCardSource);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", vodCardSource);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", seriesCardSource);
        Assert.Contains("ResponsiveCardMetricConverter", moviesSource);
        Assert.Contains("ConverterParameter=posterWidth", moviesSource);
        Assert.Contains("ConverterParameter=posterHeight", moviesSource);
        Assert.Contains("ResponsiveCardMetricConverter", seriesSource);
        Assert.Contains("ConverterParameter=posterWidth", seriesSource);
        Assert.Contains("ConverterParameter=posterHeight", seriesSource);
    }

    [Fact]
    public void AndroidEntryPoint_UsesAvalonia12ActivityLifetime()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activitySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "MainActivity.cs"));
        var androidApplicationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Application.cs"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml.cs"));

        Assert.Contains("MainActivity : AvaloniaMainActivity", activitySource);
        Assert.DoesNotContain("AvaloniaMainActivity<App>", activitySource);
        Assert.Contains("[Application]", androidApplicationSource);
        Assert.Contains("AvaloniaAndroidApplication<App>", androidApplicationSource);
        Assert.Contains("CustomizeAppBuilder", androidApplicationSource);
        Assert.Contains("IActivityApplicationLifetime", appSource);
        Assert.Contains("MainViewFactory", appSource);
    }

    [Fact]
    public void AndroidDebugPackage_StoresEmbeddedAssembliesUncompressed()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Noctra.Android.csproj"));

        Assert.Contains("<AndroidEnableAssemblyCompression Condition=\"'$(Configuration)' == 'Debug'\">false</AndroidEnableAssemblyCompression>", projectSource);
        Assert.Contains("<AndroidStoreUncompressedFileExtensions Condition=\"'$(Configuration)' == 'Debug'\">.so;$(AndroidStoreUncompressedFileExtensions)</AndroidStoreUncompressedFileExtensions>", projectSource);
    }

    [Fact]
    public void AndroidAppPaths_UseApplicationPrivateStorage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pathServicePath = Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidAppPathService.cs");

        Assert.True(File.Exists(pathServicePath));

        var source = File.ReadAllText(pathServicePath);
        Assert.Contains("IAppPathService", source);
        Assert.Contains("FilesDir", source);
        Assert.Contains("CacheDir", source);
        Assert.Contains("GetExternalFilesDir", source);
        Assert.DoesNotContain("SpecialFolder", source);
        Assert.DoesNotContain("ExternalStorageDirectory", source);
    }

    [Fact]
    public void AndroidPlatformServices_AreRegisteredAndUseNativeFacilities()
    {
        var repositoryRoot = FindRepositoryRoot();
        var androidRoot = Path.Combine(repositoryRoot, "Noctra.Android");
        var registrationSource = File.ReadAllText(Path.Combine(
            androidRoot,
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));
        var securitySource = File.ReadAllText(Path.Combine(
            androidRoot,
            "Services",
            "AndroidSecurityService.cs"));
        var networkSource = File.ReadAllText(Path.Combine(
            androidRoot,
            "Services",
            "AndroidNetworkService.cs"));
        var dispatcherSource = File.ReadAllText(Path.Combine(
            androidRoot,
            "Services",
            "AndroidDispatcherService.cs"));
        var activitySource = File.ReadAllText(Path.Combine(androidRoot, "MainActivity.cs"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml.cs"));

        Assert.Contains("AddNoctraAndroidServices", registrationSource);
        Assert.Contains("AddNoctraCoreServices", registrationSource);
        Assert.Contains("AndroidAppPathService", registrationSource);
        Assert.Contains("AndroidSecurityService", registrationSource);
        Assert.Contains("AndroidNetworkService", registrationSource);
        Assert.Contains("AndroidDispatcherService", registrationSource);

        Assert.Contains("AndroidKeyStore", securitySource);
        Assert.Contains("AES/GCM/NoPadding", securitySource);
        Assert.DoesNotContain("plainText;", securitySource);

        Assert.Contains("ConnectivityManager", networkSource);
        Assert.Contains("RegisterDefaultNetworkCallback", networkSource);
        Assert.Contains("Dispatcher.UIThread", dispatcherSource);

        Assert.Contains("ServiceProviderFactory", activitySource);
        Assert.Contains("ServiceProviderFactory", appSource);
    }

    [Fact]
    public void AndroidM3uFilePicker_UsesStorageAccessFrameworkAndPrivateImportCopy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var interfaceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Services",
            "Interfaces",
            "IPlaylistFilePickerService.cs"));
        var androidRoot = Path.Combine(repositoryRoot, "Noctra.Android");
        var pickerSource = File.ReadAllText(Path.Combine(
            androidRoot,
            "Services",
            "AndroidFilePickerService.cs"));
        var activitySource = File.ReadAllText(Path.Combine(androidRoot, "MainActivity.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(
            androidRoot,
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));

        Assert.Contains("PickM3uFileAsync", interfaceSource);
        Assert.Contains("Intent.ActionOpenDocument", pickerSource);
        Assert.Contains("CategoryOpenable", pickerSource);
        Assert.Contains("TakePersistableUriPermission", pickerSource);
        Assert.Contains("OpenInputStream", pickerSource);
        Assert.Contains("\"Imports\"", pickerSource);
        Assert.Contains("IPlaylistFilePickerService", registrationSource);
        Assert.Contains("OnActivityResult", activitySource);
        Assert.Contains("TryHandleActivityResult", activitySource);
    }

    [Fact]
    public void MobileProfileSetup_ReusesDesktopAddProfileBehaviorContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileSetupView.axaml"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var profileListSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileListView.axaml"));
        var profileListCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileListView.axaml.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));

        Assert.Contains("vm:AddProfileViewModel", viewSource);
        Assert.Contains("xmlns:loc=\"using:Noctra.Mobile.Localization\"", viewSource);
        Assert.Contains("{loc:Translate Profiles.Add.Title}", viewSource);
        Assert.Contains("{loc:Translate Profiles.Account.Analyze}", viewSource);
        Assert.Contains("{loc:Translate Profiles.Add.Save}", viewSource);
        Assert.Contains("Profiles.Add.IsChildTooltip", viewSource);
        Assert.DoesNotContain("Profiles.Add.IsChildDescription", viewSource);
        foreach (var binding in new[]
        {
            "ProfileName",
            "IsXtream",
            "IsM3U",
            "IsStalker",
            "Url",
            "Username",
            "Password",
            "IsChild",
            "HasPin",
            "PinCode",
            "PinConfirm",
            "AnalyzeConnectionCommand",
            "PickM3uFileCommand",
            "SaveCommand",
            "CancelCommand",
            "UrlError",
            "PinError",
            "StatusMessage"
        })
        {
            Assert.Contains($"{{Binding {binding}", viewSource);
        }

        Assert.Contains("ProfileListView", mainViewSource);
        Assert.Contains("OpenProfileSetup", profileListCode);
        Assert.Contains("ProfileSetupHost", profileListSource);
        Assert.Contains("AddTransient<AddProfileViewModel>", registrationSource);
        Assert.Contains("IDialogService", registrationSource);
    }

    [Fact]
    public void MobileProfileSetup_UsesDesktopProfileSetupVisualContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileSetupView.axaml"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));

        Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", viewSource);
        Assert.Contains("Classes=\"AvatarBtn\"", viewSource);
        Assert.Contains("Classes=\"EditIcon\"", viewSource);
        Assert.Contains("Kind=\"PencilOutline\"", viewSource);
        Assert.Contains("Text=\"PREMIUM\"", viewSource);
        Assert.Contains("IsVisible=\"{Binding !IsPinAvailable}\"", viewSource);
        Assert.Contains("Kind=\"ShieldCheckOutline\"", viewSource);
        Assert.Contains("Profiles.Add.PinActive", viewSource);
        Assert.Contains("Profiles.Add.PinChangeInfo", viewSource);
        Assert.Contains("Profiles.Add.NewPin", viewSource);
        Assert.Contains("Profiles.Add.ConfirmNewPin", viewSource);
        Assert.Contains("Profiles.Add.PinWarning", viewSource);
        Assert.Contains("PasswordChar=\"&#x2022;\"", viewSource);
        Assert.Contains("LetterSpacing=\"8\"", viewSource);
        Assert.Contains("PlaceholderText=\"&#x2022;&#x2022;&#x2022;&#x2022;\"", viewSource);
        Assert.DoesNotContain("PasswordChar=\"*\"", viewSource);
        Assert.Contains("Profiles.Account.M3uLink", viewSource);
        Assert.Contains("ConnectionHealthToVisibilityConverter", appSource);
        Assert.Contains("ConnectionHealthToIconConverter", appSource);
        Assert.Contains("ConnectionHealthToBrushConverter", appSource);
        Assert.Contains("ConnectionHealth", viewSource);
        Assert.Contains("Profiles.Account.HealthTooltip", viewSource);
        Assert.Contains("Kind=\"TrashCanOutline\"", viewSource);
    }

    [Fact]
    public void MobileProfiles_ReusesDesktopProfilesAndAvatarContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var profileListSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileListView.axaml"));
        var profileListCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileListView.axaml.cs"));
        var setupSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileSetupView.axaml"));
        var setupCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileSetupView.axaml.cs"));
        var avatarSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "AvatarPickerView.axaml"));
        var mobileProjectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Noctra.Mobile.csproj"));
        var mobileAppSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));

        Assert.Contains("vm:ProfilesViewModel", profileListSource);
        Assert.Contains("DisplayItems", profileListSource);
        Assert.Contains("SelectProfileCommand", profileListCode);
        Assert.Contains("AddProfile_Click", profileListSource);
        Assert.Contains("AddProfileCommand", profileListCode);
        Assert.Contains("ToggleManageModeCommand", profileListSource);
        Assert.Contains("OnProfileAddRequested", profileListCode);
        Assert.Contains("OnProfileEditRequested", profileListCode);
        Assert.Contains("RefreshProfilesAsync", profileListCode);

        Assert.Contains("OpenAvatarPickerCommand", setupSource);
        Assert.Contains("SelectedAvatar", setupSource);
        Assert.Contains("AvatarPathConverter", setupSource);
        Assert.Contains("RequestAvatarPicker", setupCode);
        Assert.Contains("AvatarPickerViewModel", setupCode);
        Assert.Contains("SetAvatar", setupCode);

        Assert.Contains("vm:AvatarPickerViewModel", avatarSource);
        Assert.Contains("SelectAvatarCommand", avatarSource);
        Assert.Contains("AvatarPathConverter", avatarSource);
        Assert.Contains("Noctra.Avalonia\\Assets\\Avatars", mobileProjectSource);
        Assert.Contains("AvatarPathConverter", mobileAppSource);
        Assert.Contains("AddSingleton<ProfilesViewModel>", registrationSource);
        Assert.Contains("AddTransient<AvatarPickerViewModel>", registrationSource);
    }

    [Fact]
    public void MobileAvatarPicker_UsesDesktopAvatarColorContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var avatarSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "AvatarPickerView.axaml"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));

        Assert.Contains("ProfileColorConverter", avatarSource);
        Assert.Contains("Background=\"{Binding Converter={StaticResource ProfileColorConverter}}\"", avatarSource);
        Assert.Contains("ProfileColorConverter", appSource);
        Assert.DoesNotContain("Background=\"{DynamicResource Surface1Brush}\"", avatarSource);
    }

    [Fact]
    public void MobileProfileList_UsesDesktopProfileCardVisualContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var profileListSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileListView.axaml"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));

        Assert.Contains("IsVisible=\"{Binding IsChild}\"", profileListSource);
        Assert.Contains("BoxShadow=\"0 0 16 0 #708B5CF6\"", profileListSource);
        Assert.Contains("Background=\"{DynamicResource IconGradientBrush}\"", profileListSource);
        Assert.Contains("IsVisible=\"{Binding #ProfileListRoot.DataContext.IsManageMode}\"", profileListSource);
        Assert.Contains("Kind=\"PencilOutline\"", profileListSource);
        Assert.Contains("IsVisible=\"{Binding IsPendingDeletion}\"", profileListSource);
        Assert.Contains("Kind=\"TimerSandFull\"", profileListSource);
        Assert.Contains("UrgencyToColorConverter", profileListSource);
        Assert.Contains("TimeSpanToCountdownConverter", profileListSource);
        Assert.Contains("Kind=\"Lock\"", profileListSource);
        Assert.Contains("PinHash, Converter={StaticResource StringNotEmptyToVisibilityConverter}", profileListSource);
        Assert.Contains("StrokeDashArray=\"4,2\"", profileListSource);
        Assert.Contains("Kind=\"Plus\"", profileListSource);
        Assert.Contains("DisplayItems.Count, Converter={StaticResource CountToVisibilityConverter}, ConverterParameter=invert", profileListSource);
        Assert.Contains("x:Name=\"FallbackAddProfileButton\"", profileListSource);
        Assert.Contains("Button.ProfileCardStyle:pointerover", profileListSource);
        Assert.Contains("Button.ProfileCardStyle:focus", profileListSource);
        Assert.Contains("Classes=\"ProfileCardStyle\"", profileListSource);
        Assert.DoesNotContain("Classes=\"ProfileCard\"", profileListSource);
        Assert.Contains("FocusGlow", profileListSource);
        Assert.DoesNotContain("Text=\"+\"", profileListSource);

        Assert.Contains("UrgencyToColorConverter", appSource);
        Assert.Contains("TimeSpanToCountdownConverter", appSource);
    }

    [Fact]
    public void MobileProfileSelection_ReusesDesktopPinAndLoadingContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var profileListSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileListView.axaml"));
        var profileListCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileListView.axaml.cs"));
        var pinEntrySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "PinEntryView.axaml"));
        var pinEntryCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "PinEntryView.axaml.cs"));
        var profileLoadingSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileLoadingView.axaml"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));

        Assert.Contains("Click=\"SelectProfile_Click\"", profileListSource);
        Assert.Contains("PinEntryHost", profileListSource);
        Assert.Contains("ProfileLoadingHost", profileListSource);
        Assert.Contains("PinEntryView", profileListSource);
        Assert.Contains("ProfileLoadingView", profileListSource);

        Assert.Contains("new PinEntryViewModel", profileListCode);
        Assert.Contains("VerifyPinIfRequired", profileListCode);
        Assert.Contains("CancelProfileDeletionAsync", profileListCode);
        Assert.Contains("ScheduleProfileDeletionAsync", profileListCode);
        Assert.Contains("LoadProfileAsync", profileListCode);
        Assert.Contains("ProfileLoadingViewModel", profileListCode);
        Assert.Contains("OnProfileSelected", profileListCode);

        Assert.Contains("vm:PinEntryViewModel", pinEntrySource);
        Assert.Contains("PressDigitCommand", pinEntrySource);
        Assert.Contains("BackspaceCommand", pinEntrySource);
        Assert.Contains("CancelCommand", pinEntrySource);
        Assert.Contains("ForgotPinCommand", pinEntrySource);
        Assert.Contains("AvatarPathConverter", pinEntrySource);
        Assert.Contains("KeyDown", pinEntryCode);

        Assert.Contains("vm:ProfileLoadingViewModel", profileLoadingSource);
        Assert.Contains("StatusMessage", profileLoadingSource);
        Assert.Contains("LoadingWarningMessage", profileLoadingSource);
        Assert.Contains("IsError", profileLoadingSource);
        Assert.Contains("AvatarPathConverter", profileLoadingSource);
        Assert.Contains("PremiumSpinner", profileLoadingSource);
        Assert.Contains("Square150x150Logo.png", profileLoadingSource);
        Assert.Contains("StringNotEmptyToVisibilityConverter", profileLoadingSource);
        Assert.Contains("Background=\"{DynamicResource Bg1Brush}\"", profileLoadingSource);
        Assert.Contains("Style Selector=\"TextBlock[Tag=True]\"", profileLoadingSource);
        Assert.DoesNotContain("<ProgressBar", profileLoadingSource);

        Assert.Contains("AddTransient<ProfileLoadingViewModel>", registrationSource);
        Assert.Contains("CoreMainViewModel", registrationSource);
    }

    [Fact]
    public void MobilePinEntry_UsesDesktopPinVisualContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pinEntrySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "PinEntryView.axaml"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));

        Assert.Contains("xmlns:icons=\"clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia\"", pinEntrySource);
        Assert.Contains("BrushTransition Property=\"Background\"", pinEntrySource);
        Assert.Contains("Background=\"{DynamicResource IconGradientBrush}\"", pinEntrySource);
        Assert.Contains("<BoxShadows>0 20 60 0 #90000000</BoxShadows>", pinEntrySource);
        Assert.Contains("PinDotConverter", pinEntrySource);
        Assert.Contains("ConverterParameter=1", pinEntrySource);
        Assert.Contains("ConverterParameter=4", pinEntrySource);
        Assert.Contains("Kind=\"BackspaceOutline\"", pinEntrySource);
        Assert.DoesNotContain("StringFormat='{}{0}/4'", pinEntrySource);
        Assert.DoesNotContain("Content=\"âŒ«\"", pinEntrySource);

        Assert.Contains("PinDotConverter", appSource);
    }

    [Fact]
    public void AndroidDialogService_UsesLocalizedDialogChrome()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dialogServiceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidDialogService.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));
        var enTranslations = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Localization",
            "Translations",
            "en-US.json"));

        Assert.Contains("ILocalizationService", dialogServiceSource);
        Assert.Contains("_localizationService.GetString(\"Dialog.Ok\")", dialogServiceSource);
        Assert.Contains("_localizationService.GetString(\"Dialog.Cancel\")", dialogServiceSource);
        Assert.Contains("_localizationService.GetString(\"Upsell.Title\")", dialogServiceSource);
        Assert.Contains("_localizationService.GetString(\"Android.Dialog.PremiumRequired\")", dialogServiceSource);
        Assert.Contains("AddSingleton<IDialogService, AndroidDialogService>", registrationSource);
        Assert.Contains("\"Android.Dialog.PremiumRequired\"", enTranslations);
        Assert.DoesNotContain("SetPositiveButton(\"OK\"", dialogServiceSource);
        Assert.DoesNotContain("SetNegativeButton(\"Cancel\"", dialogServiceSource);
        Assert.DoesNotContain("ShowAlertAsync(\"Noctra Premium\"", dialogServiceSource);
        Assert.DoesNotContain("This feature requires Noctra Premium.", dialogServiceSource);
    }

    [Fact]
    public void MobileSearch_UsesDesktopSearchInteractionAndHeaderContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var searchSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSearchView.axaml"));
        var searchCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSearchView.axaml.cs"));

        Assert.Contains("x:Name=\"SearchInput\"", searchSource);
        Assert.Contains("KeyDown=\"SearchInput_KeyDown\"", searchSource);
        Assert.Contains("x:Name=\"SearchScrollViewer\"", searchSource);
        Assert.Contains("ScrollChanged=\"SearchScrollViewer_ScrollChanged\"", searchSource);
        Assert.Contains("FontSize=\"18\"", searchSource);
        Assert.Contains("FontSize=\"15\"", searchSource);
        Assert.Contains("Search.Button", searchSource);
        Assert.Contains("Kind=\"Magnify\"", searchSource);
        Assert.Contains("SearchInput_KeyDown", searchCode);
        Assert.Contains("Key.Enter", searchCode);
        Assert.Contains("CommitSearchCommand.Execute(null)", searchCode);
        Assert.Contains("SearchScrollViewer_ScrollChanged", searchCode);
        Assert.Contains("LoadMoreChannelsIfNeededAsync", searchCode);
        Assert.Contains("LoadMoreSeriesIfNeededAsync", searchCode);
    }

    [Fact]
    public void MobileContentViews_ReusesDesktopMainViewModelContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));
        var mobileViewModelResolverSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Services",
            "MobileViewModelResolver.cs"));
        var liveSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLiveView.axaml"));
        var liveCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLiveView.axaml.cs"));
        var moviesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMoviesView.axaml"));
        var moviesCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMoviesView.axaml.cs"));
        var seriesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSeriesView.axaml"));
        var seriesDetailSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSeriesDetailView.axaml"));
        var seriesCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSeriesView.axaml.cs"));
        var searchSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSearchView.axaml"));
        var favoritesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileFavoritesView.axaml"));
        var favoritesCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileFavoritesView.axaml.cs"));
        var myListSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMyListView.axaml"));
        var myListCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMyListView.axaml.cs"));
        var historySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileHistoryView.axaml"));
        var historyCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileHistoryView.axaml.cs"));
        var downloadsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileDownloadsView.axaml"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));
        var mobileTabSlideBehaviorSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Behaviors",
            "MobileTabSlideTransitionBehavior.cs"));
        var mobileSlideBehaviorSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Behaviors",
            "MobileSlideTransitionBehavior.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));

        Assert.Contains("CoreContentHost", mainViewSource);
        Assert.Contains("MobileLiveView", mainViewSource);
        Assert.Contains("MobileMoviesView", mainViewSource);
        Assert.Contains("MobileSeriesView", mainViewSource);
        Assert.Contains("MobileSeriesDetailView", mainViewSource);
        Assert.Contains("MobileSearchView", mainViewSource);
        Assert.Contains("MobileFavoritesView", mainViewSource);
        Assert.Contains("MobileMyListView", mainViewSource);
        Assert.Contains("MobileHistoryView", mainViewSource);
        Assert.Contains("MobileDownloadsView", mainViewSource);
        Assert.Contains("MobileSettingsView", mainViewSource);
        Assert.Contains("CoreMainViewModel", mainViewCode);
        Assert.Contains("NavigateCommand", mainViewCode);
        Assert.Contains("CoreContentHost.DataContext", mainViewCode);
        Assert.Contains("MobileViewModelResolver", mainViewCode);
        Assert.Contains("GetCoreMainViewModel", mobileViewModelResolverSource);
        Assert.Contains("GetProfilesViewModel", mobileViewModelResolverSource);
        Assert.Contains("GetSettingsViewModel", mobileViewModelResolverSource);
        Assert.Contains("GetPlayerViewModel", mobileViewModelResolverSource);
        Assert.Contains("xmlns:loc=\"using:Noctra.Mobile.Localization\"", mainViewSource);
        Assert.Contains("Shell.Nav.Home", mainViewSource);
        Assert.Contains("Shell.Nav.Live", mainViewSource);
        Assert.Contains("Shell.Nav.Movies", mainViewSource);
        Assert.Contains("Shell.Nav.Series", mainViewSource);
        Assert.Contains("Shell.Search.Tooltip", mainViewSource);
        Assert.Contains("Shell.Nav.Favorites", mainViewSource);
        Assert.Contains("Shell.Nav.MyList", mainViewSource);
        Assert.Contains("Shell.Nav.History", mainViewSource);
        Assert.Contains("Shell.Nav.Downloads", mainViewSource);
        Assert.Contains("Settings.Title", mainViewSource);
        Assert.Contains("Mobile.Nav.More", mainViewSource);
        Assert.Contains("Mobile.More.Shortcuts", mainViewSource);
        Assert.Contains("Settings.Profile.Management", mainViewSource);
        Assert.Contains("Settings.Profile.ManagementDetail", mainViewSource);
        Assert.DoesNotContain("Content=\"Home\"", mainViewSource);
        Assert.DoesNotContain("Content=\"Settings\"", mainViewSource);
        Assert.DoesNotContain("Content=\"More\"", mainViewSource);
        Assert.DoesNotContain("Text=\"Continue watching\"", mainViewSource);
        Assert.DoesNotContain("Text=\"Your recent content will appear here after profile setup.\"", mainViewSource);
        Assert.True(
            CountOccurrences(mainViewCode, "GetRequiredService<") <= 2,
            "MainView should only resolve the mobile resolver facades directly, not individual view models or platform services.");
        Assert.Contains("AddSingleton<MobileViewModelResolver>", registrationSource);

        foreach (var contentSource in new[] { liveSource, moviesSource })
        {
            Assert.Contains("vm:MainViewModel", contentSource);
            Assert.Contains("FilteredChannels", contentSource);
            Assert.Contains("Groups", contentSource);
            Assert.Contains("SelectedGroup", contentSource);
            Assert.Contains("SelectedSortOrder", contentSource);
            Assert.Contains("IsContentLoading", contentSource);
            Assert.Contains("ShowEmptyChannels", contentSource);
        }

        Assert.Contains("ConverterParameter=liveWidth", liveSource);
        Assert.Contains("LoadMoreChannelsIfNeededAsync", liveCode);
        Assert.Contains("LoadMoreChannelsIfNeededAsync", moviesCode);

        Assert.Contains("vm:MainViewModel", seriesSource);
        Assert.Contains("SeriesViewItems", seriesSource);
        Assert.Contains("Groups", seriesSource);
        Assert.Contains("SelectedGroup", seriesSource);
        Assert.Contains("SelectedSortOrder", seriesSource);
        Assert.Contains("IsContentLoading", seriesSource);
        Assert.Contains("ShowEmptyChannels", seriesSource);
        Assert.Contains("LoadMoreSeriesIfNeededAsync", seriesCode);
        Assert.Contains("SelectedSeries", seriesDetailSource);
        Assert.Contains("IsSeriesDetailVisible", seriesDetailSource);
        Assert.Contains("CloseSeriesDetailCommand", seriesDetailSource);
        Assert.Contains("PlayEpisodeCommand", seriesDetailSource);
        Assert.Contains("SelectedSeason", seriesDetailSource);
        Assert.Contains("SelectedSeason.Episodes", seriesDetailSource);
        Assert.Contains("MobileSlideTransitionBehavior.TriggerValue", seriesDetailSource);
        Assert.Contains("ShowSelectedSeriesEpisodesLoading", seriesDetailSource);
        Assert.Contains("ShowSelectedSeriesNoEpisodes", seriesDetailSource);
        Assert.Contains("HasSelectedSeriesPlayableEpisode", seriesDetailSource);
        Assert.Contains("SelectedSeriesContinueText", seriesDetailSource);
        Assert.Contains("WatchTrailerCommand", seriesDetailSource);
        Assert.Contains("SelectedSeriesTrailerUrl", seriesDetailSource);
        Assert.Contains("DownloadSelectedSeasonCommand", seriesDetailSource);
        Assert.Contains("SelectedSeriesYears", seriesDetailSource);
        Assert.Contains("SelectedSeriesGenres", seriesDetailSource);
        Assert.Contains("SelectedSeriesAgeRating", seriesDetailSource);
        Assert.Contains("SelectedSeriesNetworkLogoUrl", seriesDetailSource);
        Assert.Contains("Series.WatchTrailer", seriesDetailSource);
        Assert.Contains("Series.Detail.DownloadSeason", seriesDetailSource);
        Assert.Contains("Context.MyList.Toggle", seriesDetailSource);
        Assert.Contains("Context.Favorite.Toggle", seriesDetailSource);
        Assert.DoesNotContain("Content=\"Back\"", seriesDetailSource);
        Assert.DoesNotContain("Content=\"My List\"", seriesDetailSource);
        Assert.DoesNotContain("Content=\"Favorite\"", seriesDetailSource);

        Assert.Contains("vm:MainViewModel", searchSource);
        Assert.Contains("SearchQuery", searchSource);
        Assert.Contains("CommitSearchCommand", searchSource);
        Assert.Contains("ApplySearchSuggestionCommand", searchSource);
        Assert.Contains("SearchLiveChannels", searchSource);
        Assert.Contains("SearchSeriesChannels", searchSource);
        Assert.Contains("SearchVodChannels", searchSource);
        Assert.Contains("ShowSearchEmptyState", searchSource);

        Assert.Contains("vm:MainViewModel", favoritesSource);
        Assert.Contains("FavoriteLiveChannels", favoritesSource);
        Assert.Contains("FavoriteSeriesItems", favoritesSource);
        Assert.Contains("FavoriteVodChannels", favoritesSource);
        Assert.Contains("ShowFavoritesEmptyState", favoritesSource);
        Assert.Contains("FavoritesScrollViewer_ScrollChanged", favoritesSource);
        Assert.Contains("ConverterParameter=liveWidth", favoritesSource);
        Assert.Contains("LoadMoreChannelsIfNeededAsync", favoritesCode);
        Assert.Contains("LoadMoreSeriesIfNeededAsync", favoritesCode);

        Assert.Contains("vm:MainViewModel", myListSource);
        Assert.Contains("MyListLiveChannels", myListSource);
        Assert.Contains("MyListSeriesItems", myListSource);
        Assert.Contains("MyListVodChannels", myListSource);
        Assert.Contains("ShowMyListEmptyState", myListSource);
        Assert.Contains("MyListScrollViewer_ScrollChanged", myListSource);
        Assert.Contains("ConverterParameter=liveWidth", myListSource);
        Assert.Contains("LoadMoreChannelsIfNeededAsync", myListCode);
        Assert.Contains("LoadMoreSeriesIfNeededAsync", myListCode);

        Assert.Contains("vm:MainViewModel", historySource);
        Assert.Contains("HistoryLiveChannels", historySource);
        Assert.Contains("HistorySeriesItems", historySource);
        Assert.Contains("HistoryVodChannels", historySource);
        Assert.Contains("ShowHistoryEmptyState", historySource);
        Assert.Contains("LoadMoreHistoryIfNeededAsync", historyCode);
        Assert.Contains("ConverterParameter=liveWidth", historySource);

        Assert.Contains("vm:MainViewModel", downloadsSource);
        Assert.Contains("TotalDownloadsInfoText", downloadsSource);
        Assert.Contains("SelectedDownloadSortOrder", downloadsSource);
        Assert.Contains("Theme=\"{StaticResource ModernComboBox}\"", downloadsSource);
        Assert.Contains("DownloadedSeriesItems", downloadsSource);
        Assert.Contains("DownloadedVodChannels", downloadsSource);
        Assert.Contains("DeleteDownloadedMediaCommand", downloadsSource);
        Assert.Contains("ShowDownloadsEmptyState", downloadsSource);
        Assert.Contains("DownloadTabIndex", downloadsSource);
        Assert.Contains("ActiveDownloadCount", downloadsSource);
        Assert.Contains("Downloads.Stats.Active.Format", downloadsSource);
        Assert.DoesNotContain("StringFormat='{}{0} active'", downloadsSource);
        Assert.Contains("ActiveDownloadsTotalSpeedText", downloadsSource);
        Assert.Contains("ActiveDownloadingItems", downloadsSource);
        Assert.Contains("QueuedDownloadItems", downloadsSource);
        Assert.Contains("StopAllDownloadsCommand", downloadsSource);
        Assert.Contains("ClearQueueCommand", downloadsSource);
        Assert.Contains("TogglePauseDownloadCommand", downloadsSource);
        Assert.Contains("CancelDownloadCommand", downloadsSource);
        Assert.Contains("DownloadStatusToBrushConverter", appSource);
        Assert.Contains("BoolToMaterialIconKindConverter", appSource);
        Assert.Contains("BytesToHumanConverter", appSource);
        Assert.Contains("DoubleToStarGridLengthConverter", appSource);
        Assert.Contains("WidthToColumnsConverter", appSource);
        Assert.Contains("WatchedProgressVisibilityConverter", appSource);
        Assert.Contains("Foreground=\"{Binding Status, Converter={StaticResource DownloadStatusToBrushConverter}}\"", downloadsSource);
        Assert.Contains("Kind=\"{Binding IsPaused, Converter={StaticResource BoolToMaterialIconKindConverter}, ConverterParameter='Play|Pause'}\"", downloadsSource);
        Assert.Contains("StorageOtherPercent", downloadsSource);
        Assert.Contains("StorageNoctraPercent", downloadsSource);
        Assert.Contains("StoragePendingPercent", downloadsSource);
        Assert.Contains("StorageFreePercent", downloadsSource);
        Assert.Contains("DoubleToStarGridLengthConverter", downloadsSource);
        Assert.Contains("Url=\"{Binding PosterUrl}\"", downloadsSource);
        Assert.Contains("Url=\"{Binding CoverUrl}\"", downloadsSource);
        Assert.Contains("BytesTotal, Converter={StaticResource BytesToHumanConverter}", downloadsSource);

        Assert.Contains("xmlns:behaviors=\"using:Noctra.Mobile.Behaviors\"", downloadsSource);
        Assert.Contains("behaviors:MobileTabSlideTransitionBehavior.IsEnabled=\"True\"", downloadsSource);
        Assert.Contains("namespace Noctra.Mobile.Behaviors", mobileTabSlideBehaviorSource);
        Assert.Contains("RegisterAttached<TabControl, bool>", mobileTabSlideBehaviorSource);
        Assert.Contains("PART_SelectedContentHost", mobileTabSlideBehaviorSource);
        Assert.Contains("SelectionChanged", mobileTabSlideBehaviorSource);
        Assert.Contains("namespace Noctra.Mobile.Behaviors", mobileSlideBehaviorSource);
        Assert.Contains("RegisterAttached<MobileSlideTransitionBehavior, Control, object?>", mobileSlideBehaviorSource);
        Assert.Contains("TriggerValue", mobileSlideBehaviorSource);
        Assert.DoesNotContain("Noctra.Avalonia.Behaviors", downloadsSource);
    }

    [Fact]
    public void MobileStartup_UsesProfilesAsInitialGate()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));
        var legalConsentSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLegalConsentView.axaml"));
        var legalConsentCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLegalConsentView.axaml.cs"));
        var legalDocumentSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLegalDocumentView.axaml"));

        Assert.Contains("x:Name=\"LegalConsentOverlay\"", mainViewSource);
        Assert.Contains("MobileLegalDocumentView", legalConsentSource);
        Assert.Contains("LegalDocumentHost", legalConsentSource);
        Assert.Contains("ShowDocument(", legalConsentCode);
        Assert.Contains("ScrollViewer", legalDocumentSource);
        Assert.DoesNotContain("ShowLegalDocumentAsync", legalConsentCode);
        Assert.Contains("ProfilesOverlay", mainViewSource);
        Assert.Contains("Profiles.Title", mainViewSource);
        Assert.Contains("Profiles.SelectProfile", mainViewSource);
        Assert.DoesNotContain("x:Name=\"SplashOverlay\"", mainViewSource);
        Assert.DoesNotContain("Splash.Initializing", mainViewSource);
        Assert.DoesNotContain("Text=\"Loading...\"", mainViewSource);
        Assert.Contains("RunStartupFlowAsync", mainViewCode);
        Assert.DoesNotContain("Task.Delay(1500)", mainViewCode);
        Assert.DoesNotContain("SplashOverlay", mainViewCode);
        Assert.Contains("await ShowLegalConsentIfNeededAsync()", mainViewCode);
        Assert.Contains("ShowProfileSelection()", mainViewCode);
        Assert.DoesNotContain("_ = ShowLegalConsentIfNeededAsync()", mainViewCode);
        Assert.DoesNotContain("_ = DismissSplashAndShowProfilesAsync()", mainViewCode);
    }

    [Fact]
    public void MobileStartup_HidesHomeShellUntilAProfileLoads()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("x:Name=\"HeaderBar\"", mainViewSource);
        Assert.Contains("x:Name=\"ShellContent\"", mainViewSource);
        Assert.Contains("x:Name=\"BottomNavigation\"", mainViewSource);
        Assert.Contains("x:Name=\"CoreContentHost\"", mainViewSource);
        Assert.Matches("x:Name=\"HeaderBar\"[\\s\\S]*?IsVisible=\"False\"", mainViewSource);
        Assert.Matches("x:Name=\"ShellContent\"[\\s\\S]*?IsVisible=\"False\"", mainViewSource);
        Assert.Matches("x:Name=\"BottomNavigation\"[\\s\\S]*?IsVisible=\"False\"", mainViewSource);
        Assert.Contains("CoreContentHost.IsVisible = true", mainViewCode);
        Assert.Contains("NavigateToDestination(\"Home\")", mainViewCode);
    }

    [Fact]
    public void MobileNavigation_ClosesSeriesDetailWhenLeavingCoreContentForSettings()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("<views:MobileSeriesDetailView x:Name=\"MobileSeriesDetailContent\"", mainViewSource);
        Assert.DoesNotContain("MobileSeriesDetailContent\"\r\n                                      IsVisible=\"False\"", mainViewSource);
        Assert.Contains("CloseSeriesDetailIfOpen()", mainViewCode);
        Assert.Contains("!string.Equals(destination, \"Series\", StringComparison.Ordinal)", mainViewCode);
        Assert.Contains("_coreMainViewModel ??= GetViewModelResolver()?.GetCoreMainViewModel()", mainViewCode);
        Assert.Contains("_coreMainViewModel?.CloseSeriesDetailCommand.Execute(null)", mainViewCode);
        Assert.Contains("OnSettingsClick", mainViewCode);
        Assert.Contains("CloseSeriesDetailIfOpen();", mainViewCode[
            mainViewCode.IndexOf("private void OnSettingsClick", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void AndroidNativeSplash_UsesStaticNoctraSplashBeforeAvaloniaStartup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var stylesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Resources",
            "values",
            "styles.xml"));
        var splashSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Resources",
            "drawable",
            "splash_screen.xml"));
        var colorsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Resources",
            "values",
            "colors.xml"));
        var activitySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "MainActivity.cs"));

        Assert.Contains("<item name=\"android:windowBackground\">@drawable/splash_screen</item>", stylesSource);
        Assert.Contains("Theme = \"@style/MyTheme.NoActionBar\"", activitySource);
        Assert.Contains("android:color=\"@color/splash_background\"", splashSource);
        Assert.Contains("android:drawable=\"@mipmap/ic_launcher\"", splashSource);
        Assert.Contains("android:width=\"220dp\"", splashSource);
        Assert.Contains("android:height=\"220dp\"", splashSource);
        Assert.Contains("<color name=\"splash_background\">#0B0616</color>", colorsSource);
    }

    [Fact]
    public void AndroidProject_ExcludesPlayStoreMarketingImagesFromCompiledResources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Noctra.Android.csproj"));

        Assert.Contains(@"<AndroidResource Remove=""Resources\PlayStore_Assets\**\*"" />", projectSource);
    }

    [Fact]
    public void MobileLocalizationSource_UsesCoreFallbackBeforeDependencyInjectionInitializes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var localizationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Localization",
            "LocalizationSource.cs"));

        Assert.Contains("using Noctra.Services;", localizationSource);
        Assert.Contains("private readonly ILocalizationService _fallbackLocalizationService = new LocalizationService();", localizationSource);
        Assert.Contains("_localizationService ?? _fallbackLocalizationService", localizationSource);
    }

    [Fact]
    public void MobileProfilesOverlay_BindsManageCommandToProfilesViewModel()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("x:Name=\"ProfilesOverlay\"", mainViewSource);
        Assert.Contains("Command=\"{Binding ToggleManageModeCommand}\"", mainViewSource);
        Assert.DoesNotContain("DataContext.ToggleManageModeCommand", mainViewSource);
        Assert.Contains("ProfilesOverlay.DataContext = _activeProfilesViewModel", mainViewCode);
        Assert.Contains("OverlayProfileList.ProfileLoaded += OverlayProfileList_ProfileLoaded", mainViewCode);
    }

    [Fact]
    public void MobileProfilesOverlay_ClosesOnlyAfterProfileLoadCompletes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));
        var profileListCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "ProfileListView.axaml.cs"));

        Assert.Contains("public event EventHandler? ProfileLoaded", profileListCode);
        Assert.Contains("ProfileLoaded?.Invoke(this, EventArgs.Empty)", profileListCode);
        Assert.Contains("OverlayProfileList_ProfileLoaded", mainViewCode);
        Assert.DoesNotContain("_activeProfilesViewModel.RequestClose += ProfilesViewModel_RequestClose", mainViewCode);
        Assert.DoesNotContain("private void ProfilesViewModel_RequestClose()", mainViewCode);
    }

    [Fact]
    public void MobileBackNavigation_ConsumesBackWhenProfilesOverlayIsVisible()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("if (ProfilesOverlay.IsVisible)", mainViewCode);
        Assert.Contains("_activeProfilesViewModel is { IsManageMode: true }", mainViewCode);
        Assert.Contains("_activeProfilesViewModel.ToggleManageModeCommand.Execute(null)", mainViewCode);
        Assert.Contains("return true;", mainViewCode);
    }

    [Fact]
    public void MobileHome_UsesPulsingLogoForEmptyState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var homeSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileHomeView.axaml"));

        Assert.Contains("Square150x150Logo.Gray.png", homeSource);
        Assert.Contains("ScaleTransform.ScaleX", homeSource);
        Assert.Contains("ScaleTransform.ScaleY", homeSource);
        Assert.Contains("IterationCount=\"Infinite\"", homeSource);
        Assert.DoesNotContain("Kind=\"Home\"", homeSource);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", homeSource);
    }

    [Fact]
    public void MobileSettings_ReusesDesktopSettingsViewModelContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var settingsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSettingsView.axaml"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));
        var stringFormatConverterSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Converters",
            "StringFormatConverter.cs"));
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));
        var settingsCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSettingsView.axaml.cs"));
        var darkThemeSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Resources",
            "Themes",
            "DarkTheme.axaml"));
        var lightThemeSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Resources",
            "Themes",
            "LightTheme.axaml"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));

        Assert.Contains("vm:SettingsViewModel", settingsSource);
        Assert.Contains("StringFormatConverter", appSource);
        Assert.Contains("BooleanToSuccessWarningBrushConverter", appSource);
        Assert.Contains("Resources/Styles.axaml", appSource);
        Assert.Contains("class StringFormatConverter", stringFormatConverterSource);
        Assert.Contains("LocalizationSource.Instance", stringFormatConverterSource);
        Assert.Contains("CurrentProfileName", settingsSource);
        Assert.Contains("CurrentProfileAvatar", settingsSource);
        Assert.Contains("ProfileCreatedAt", settingsSource);
        Assert.Contains("Settings.Profile.CreatedAtFormat", settingsSource);
        Assert.Contains("Settings.Title", settingsSource);
        Assert.Contains("Settings.Profile.Management", settingsSource);
        Assert.Contains("Settings.Profile.ManagementDetail", settingsSource);
        Assert.Contains("Settings.Profile.BackToProfiles", settingsSource);
        Assert.Contains("BackToProfiles_Click", settingsSource);
        Assert.Contains("BackToProfilesRequested", settingsCode);
        Assert.Contains("Settings.Account.Title", settingsSource);
        Assert.Contains("Settings.Account.Password", settingsSource);
        Assert.Contains("Settings.Account.Expiry", settingsSource);
        Assert.Contains("ProviderUrlLabel", settingsSource);
        Assert.Contains("ProviderUrl", settingsSource);
        Assert.Contains("ShowProviderIdentity", settingsSource);
        Assert.Contains("ProviderIdentityLabel", settingsSource);
        Assert.Contains("ProviderUsername", settingsSource);
        Assert.Contains("ShowProviderPassword", settingsSource);
        Assert.Contains("ProviderPassword", settingsSource);
        Assert.Contains("ShowProviderExpiration", settingsSource);
        Assert.Contains("ExpirationDate", settingsSource);
        Assert.Contains("Settings.Account.ExpiryFormat", settingsSource);
        Assert.Contains("ExpirationStatus", settingsSource);
        Assert.Contains("IsDarkTheme", settingsSource);
        Assert.Contains("GlobalSettings.Appearance.Title", settingsSource);
        Assert.Contains("DarkThemeButton", settingsSource);
        Assert.Contains("LightThemeButton", settingsSource);
        Assert.Contains("DarkTheme_PointerPressed", settingsSource);
        Assert.Contains("LightTheme_PointerPressed", settingsSource);
        Assert.Contains("DarkCheckmark", settingsSource);
        Assert.Contains("LightCheckmark", settingsSource);
        Assert.Contains("ThemePreviewDarkBg", settingsSource);
        Assert.Contains("ThemePreviewLightBg", settingsSource);
        Assert.Contains("DarkTheme_PointerPressed", settingsCode);
        Assert.Contains("LightTheme_PointerPressed", settingsCode);
        Assert.Contains("UpdateThemeSelection", settingsCode);
        Assert.Contains("ThemePreviewDarkBg", darkThemeSource);
        Assert.Contains("ThemePreviewLightBg", lightThemeSource);
        Assert.Contains("Settings.Theme.Dark", settingsSource);
        Assert.Contains("Settings.Theme.Light", settingsSource);
        Assert.Contains("Settings.Language.Title", settingsSource);
        Assert.Contains("Theme=\"{StaticResource ModernComboBox}\"", settingsSource);
        Assert.Contains("AppLanguage", settingsSource);
        Assert.True(
            CountOccurrences(settingsSource, "Tag=\"de\"") >= 3,
            "Mobile appearance language picker should include German in addition to subtitle and audio language pickers.");
        Assert.True(
            CountOccurrences(settingsSource, "Tag=\"fr\"") >= 3,
            "Mobile appearance language picker should include French in addition to subtitle and audio language pickers.");
        Assert.True(
            CountOccurrences(settingsSource, "Tag=\"es\"") >= 3,
            "Mobile appearance language picker should include Spanish in addition to subtitle and audio language pickers.");
        Assert.Contains("AutoPlayNext", settingsSource);
        Assert.Contains("UserAgent", settingsSource);
        Assert.Contains("Settings.Playback.Network", settingsSource);
        Assert.Contains("Settings.Playback.UserAgent", settingsSource);
        Assert.Contains("Settings.Playback.AutoPlayNext", settingsSource);
        Assert.Contains("Settings.Playback.Quality", settingsSource);
        Assert.Contains("Settings.Playback.Quality.Low", settingsSource);
        Assert.Contains("Settings.Playback.Quality.Medium", settingsSource);
        Assert.Contains("Settings.Playback.Quality.High", settingsSource);
        Assert.Contains("Settings.Playback.Quality.Auto", settingsSource);
        Assert.Contains("Settings.Playback.Buffer", settingsSource);
        Assert.Contains("Settings.Playback.Buffer.Small", settingsSource);
        Assert.Contains("Settings.Playback.Buffer.Normal", settingsSource);
        Assert.Contains("Settings.Playback.Buffer.Large", settingsSource);
        Assert.Contains("Settings.Privacy.SaveHistory", settingsSource);
        Assert.Contains("Settings.Privacy.ClearOnExit", settingsSource);
        Assert.Contains("SelectedDataUsage", settingsSource);
        var dataUsageIndex = settingsSource.IndexOf("SelectedIndex=\"{Binding SelectedDataUsage}", StringComparison.Ordinal);
        var bufferLabelIndex = settingsSource.IndexOf("Settings.Playback.Buffer", dataUsageIndex, StringComparison.Ordinal);
        Assert.True(dataUsageIndex >= 0 && bufferLabelIndex > dataUsageIndex);
        var dataUsageBlock = settingsSource[dataUsageIndex..bufferLabelIndex];
        Assert.Contains("Settings.Playback.Quality.Auto", dataUsageBlock);
        Assert.Contains("IsBufferSmall", settingsSource);
        Assert.Contains("IsBufferNormal", settingsSource);
        Assert.Contains("IsBufferLarge", settingsSource);
        Assert.True(
            CountOccurrences(settingsSource, "IsEnabled=\"{Binding IsPremium}\"") >= 16,
            "Mobile settings should gate premium-only buffer and refresh frequency options with IsPremium.");
        Assert.Contains("SubtitleEnabled", settingsSource);
        Assert.Contains("Settings.Playback.AudioSubtitle", settingsSource);
        Assert.Contains("Settings.Playback.SubtitlesAuto", settingsSource);
        Assert.Contains("Settings.Playback.PreferredSubtitle", settingsSource);
        Assert.Contains("Settings.Playback.PreferredAudio", settingsSource);
        Assert.Contains("SubtitleLanguage", settingsSource);
        Assert.Contains("PreferredAudioLanguage", settingsSource);
        Assert.True(
            CountOccurrences(settingsSource, "Tag=\"ru\"") >= 2,
            "Mobile subtitle and audio language pickers should include Russian.");
        Assert.True(
            CountOccurrences(settingsSource, "Tag=\"ar\"") >= 2,
            "Mobile subtitle and audio language pickers should include Arabic.");
        Assert.True(
            CountOccurrences(settingsSource, "Tag=\"nl\"") >= 2,
            "Mobile subtitle and audio language pickers should include Dutch.");
        Assert.Contains("SaveWatchHistory", settingsSource);
        Assert.Contains("ClearHistoryOnExit", settingsSource);
        Assert.Contains("DownloadWifiOnly", settingsSource);
        Assert.Contains("Settings.Download.Title", settingsSource);
        Assert.Contains("Settings.Download.WifiOnly", settingsSource);
        Assert.Contains("Settings.Download.Quality", settingsSource);
        Assert.Contains("Settings.Download.Quality.Standard", settingsSource);
        Assert.Contains("Settings.Download.Quality.High", settingsSource);
        Assert.Contains("Settings.Download.Path", settingsSource);
        Assert.Contains("Mobile.Settings.Download.AndroidStorage", settingsSource);
        Assert.Contains("Settings.Notifications.Download", settingsSource);
        Assert.Contains("SelectedDownloadQuality", settingsSource);
        Assert.Contains("DownloadPath", settingsSource);
        Assert.Contains("ShowDownloadNotification", settingsSource);
        Assert.Contains("RefreshChannelListNowCommand", settingsSource);
        Assert.Contains("Settings.Channels.Title", settingsSource);
        Assert.Contains("Settings.Channels.RefreshNow", settingsSource);
        Assert.Contains("Settings.Channels.Count", settingsSource);
        Assert.Contains("Settings.Channels.LastUpdate", settingsSource);
        Assert.Contains("Settings.Channels.Frequency", settingsSource);
        Assert.Contains("TotalChannels", settingsSource);
        Assert.Contains("ChannelListLastUpdated", settingsSource);
        Assert.Contains("Settings.Channels.DateFormat", settingsSource);
        Assert.Contains("ChannelListLastError", settingsSource);
        Assert.Contains("ChannelListRefreshFrequencyIndex", settingsSource);
        Assert.Contains("HiddenLiveGroups", settingsSource);
        Assert.Contains("Settings.Channels.Hidden.Live", settingsSource);
        Assert.Contains("Settings.Channels.Hidden.Show", settingsSource);
        Assert.Contains("Settings.Channels.Hidden.Empty.Live", settingsSource);
        Assert.Contains("HiddenLiveGroups.Count", settingsSource);
        Assert.Contains("HiddenMovieGroups", settingsSource);
        Assert.Contains("Settings.Channels.Hidden.Movies", settingsSource);
        Assert.Contains("Settings.Channels.Hidden.Empty.Movies", settingsSource);
        Assert.Contains("HiddenMovieGroups.Count", settingsSource);
        Assert.Contains("HiddenSeriesGroups", settingsSource);
        Assert.Contains("Settings.Channels.Hidden.Series", settingsSource);
        Assert.Contains("Settings.Channels.Hidden.Empty.Series", settingsSource);
        Assert.Contains("HiddenSeriesGroups.Count", settingsSource);
        Assert.Contains("UnhideGroupCommand", settingsSource);
        Assert.Contains("EpgEnabled", settingsSource);
        Assert.Contains("Settings.Epg.Title", settingsSource);
        Assert.Contains("Settings.Epg.Enabled", settingsSource);
        Assert.Contains("Settings.Epg.Count", settingsSource);
        Assert.Contains("Settings.Epg.RefreshNow", settingsSource);
        Assert.Contains("Mobile.Settings.Epg.Channels", settingsSource);
        Assert.Contains("Mobile.Settings.Epg.LastUpdate", settingsSource);
        Assert.Contains("Settings.Epg.Refresh", settingsSource);
        Assert.Contains("Settings.Epg.Timezone", settingsSource);
        Assert.Contains("RefreshEpgNowCommand", settingsSource);
        Assert.Contains("EpgRefreshFrequencyIndex", settingsSource);
        Assert.Contains("EpgTimeOffsetIndex", settingsSource);
        Assert.Contains("Settings.Epg.Timezone.Format.Negative", settingsSource);
        Assert.Contains("Settings.Epg.Timezone.Format.Positive", settingsSource);
        Assert.Contains("xmlns:loc=\"using:Noctra.Mobile.Localization\"", settingsSource);
        Assert.Contains("Settings.Epg.Timezone.Auto", settingsSource);
        Assert.Contains("EpgLastError", settingsSource);
        Assert.Contains("TotalEpgPrograms", settingsSource);
        Assert.Contains("TotalEpgChannels", settingsSource);
        Assert.Contains("LastEpgUpdate", settingsSource);
        Assert.Contains("CustomEpgUrls", settingsSource);
        Assert.Contains("Settings.Epg.CustomSources", settingsSource);
        Assert.Contains("Settings.Epg.SourcesCountFormat", settingsSource);
        Assert.Contains("Settings.Epg.AddNew", settingsSource);
        Assert.Contains("Settings.Epg.PerformanceWarning", settingsSource);
        Assert.Contains("Settings.Epg.UrlPlaceholder", settingsSource);
        Assert.Contains("Settings.Epg.RemoveSource.Tooltip", settingsSource);
        Assert.Contains("AddCustomEpgCommand", settingsSource);
        Assert.Contains("RemoveCustomEpgCommand", settingsSource);
        Assert.Contains("IsGlobalLoading", settingsSource);
        Assert.Contains("GlobalLoadingMessage", settingsSource);
        Assert.Contains("CancelRefreshOperationCommand", settingsSource);
        Assert.Contains("WatchHistoryRetentionIndex", settingsSource);
        Assert.Contains("Settings.Privacy.Retention", settingsSource);
        Assert.Contains("Settings.Privacy.Retention.Forever", settingsSource);
        Assert.Contains("Settings.Privacy.Retention.3d", settingsSource);
        Assert.Contains("Settings.Privacy.Retention.7d", settingsSource);
        Assert.Contains("Settings.Privacy.Retention.14d", settingsSource);
        Assert.Contains("Settings.Privacy.Retention.30d", settingsSource);
        Assert.Contains("Settings.Privacy.ClearAllNow", settingsSource);
        Assert.Contains("CurrentVersion", settingsSource);
        Assert.Contains("Mobile.Settings.About.Title", settingsSource);
        Assert.Contains("GlobalSettings.Update.Title", settingsSource);
        Assert.Contains("GlobalSettings.Update.Checking", settingsSource);
        Assert.Contains("Settings.About.VersionFormat", settingsSource);
        Assert.Contains("Settings.About.PremiumVersionFormat", settingsSource);
        Assert.Contains("IsPremium", settingsSource);
        Assert.Contains("ShowUpsellCommand", settingsSource);
        Assert.Contains("GlobalSettings.About.Upgrade", settingsSource);
        Assert.Contains("IsPromoCodeStatusSuccess", settingsSource);
        Assert.Contains("BooleanToSuccessWarningBrushConverter", settingsSource);
        Assert.Contains("UpdateStatusText", settingsSource);
        Assert.Contains("IsCheckingUpdates", settingsSource);
        Assert.Contains("IsIdle", settingsSource);
        Assert.Contains("IsUpdateAvailable", settingsSource);
        Assert.Contains("CheckForUpdatesCommand", settingsSource);
        Assert.Contains("GlobalSettings.Update.Check", settingsSource);
        Assert.Contains("StartUpdateCommand", settingsSource);
        Assert.Contains("GlobalSettings.Update.Now", settingsSource);
        Assert.Contains("ReportBugCommand", settingsSource);
        Assert.Contains("GlobalSettings.Support.Report", settingsSource);
        Assert.Contains("StatusMessage", settingsSource);
        Assert.Contains("SaveSettingsCommand", settingsSource);
        Assert.Contains("Settings.Action.SaveSettings", settingsSource);
        Assert.Contains("ResetToDefaultsCommand", settingsSource);
        Assert.Contains("Settings.Action.ResetDefaults", settingsSource);
        Assert.Contains("ClearHistoryCommand", settingsSource);
        Assert.Contains("MobileLegalDocumentView", settingsSource);
        Assert.Contains("LegalDocumentHost", settingsSource);
        Assert.Contains("PrivacyPolicy_Click", settingsSource);
        Assert.Contains("Terms_Click", settingsSource);
        Assert.Contains("GlobalSettings.Privacy.Message.Current", settingsCode);
        Assert.Contains("GlobalSettings.Terms.Message.Current", settingsCode);
        Assert.Contains("Common.Loading", settingsSource);
        Assert.Contains("Dialog.Cancel", settingsSource);
        Assert.DoesNotContain("Text=\"Settings\"", settingsSource);
        Assert.DoesNotContain("Text=\"Account\"", settingsSource);
        Assert.DoesNotContain("Text=\"Downloads\"", settingsSource);
        Assert.DoesNotContain("Text=\"Channels\"", settingsSource);
        Assert.DoesNotContain("Text=\"Appearance\"", settingsSource);
        Assert.DoesNotContain("Content=\"Dark theme\"", settingsSource);
        Assert.DoesNotContain("Text=\"Playback\"", settingsSource);
        Assert.DoesNotContain("Content=\"Auto play next episode\"", settingsSource);
        Assert.DoesNotContain("Text=\"Audio and subtitles\"", settingsSource);
        Assert.DoesNotContain("Text=\"Android stores downloads in the app storage location shown above.\"", settingsSource);
        Assert.DoesNotContain("Text=\"Hidden live groups\"", settingsSource);
        Assert.DoesNotContain("Content=\"Show\"", settingsSource);
        Assert.DoesNotContain("Text=\"EPG channels\"", settingsSource);
        Assert.DoesNotContain("Text=\"Custom EPG sources\"", settingsSource);
        Assert.DoesNotContain("PlaceholderText=\"EPG URL\"", settingsSource);
        Assert.DoesNotContain("Text=\"About\"", settingsSource);
        Assert.DoesNotContain("Text=\"Updates\"", settingsSource);
        Assert.DoesNotContain("Text=\"Checking for updates...\"", settingsSource);
        Assert.DoesNotContain("Content=\"Save\"", settingsSource);
        Assert.DoesNotContain("Content=\"Cancel\"", settingsSource);

        Assert.Contains("Tag=\"Settings\"", mainViewSource);
        Assert.Contains("MobileSettingsView", mainViewSource);
        Assert.Contains("MobileSettingsContent.DataContext", mainViewCode);
        Assert.Contains("MobileSettingsContent.BackToProfilesRequested", mainViewCode);
        Assert.Contains("ShowProfileSelection", mainViewCode);
        Assert.DoesNotContain("SelectDestination(\"More\")", mainViewCode);
        Assert.Contains("SettingsViewModel", mainViewCode);
        Assert.Contains("destination == \"Settings\"", mainViewCode);
        Assert.Contains("SelectDestination(\"Settings\")", mainViewCode);
        Assert.Contains("AddTransient<SettingsViewModel>", registrationSource);
    }

    [Fact]
    public void MobileMediaSelection_ReusesDesktopSelectMediaContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));
        var liveSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLiveView.axaml"));
        var moviesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMoviesView.axaml"));
        var seriesSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSeriesView.axaml"));
        var continueWatchingCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileContinueWatchingCard.axaml"));
        var vodCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileVodCard.axaml"));
        var seriesCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileSeriesCard.axaml"));
        var liveCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileLiveTvCard.axaml"));

        Assert.Contains("MobileLiveTvCard", liveSource);
        Assert.Contains("MobileVodCard", moviesSource);
        Assert.Contains("MobileSeriesCard", seriesSource);
        Assert.Contains("Theme=\"{StaticResource ModernComboBox}\"", liveSource);
        Assert.Contains("Theme=\"{StaticResource ModernComboBox}\"", moviesSource);
        Assert.Contains("Theme=\"{StaticResource ModernComboBox}\"", seriesSource);
        Assert.Contains("ClearGroupSelection_Click", liveSource);
        Assert.Contains("ClearGroupSelection_Click", moviesSource);
        Assert.Contains("ClearGroupSelection_Click", seriesSource);
        Assert.Contains("Content=\"{loc:Translate Common.All}\"", liveSource);
        Assert.Contains("Content=\"{loc:Translate Common.All}\"", moviesSource);
        Assert.Contains("Content=\"{loc:Translate Common.All}\"", seriesSource);

        foreach (var contentSource in new[] { liveCardSource, continueWatchingCardSource, vodCardSource, seriesCardSource })
        {
            Assert.Contains("SelectMediaCommand", contentSource);
            Assert.Contains("CommandParameter=\"{Binding}\"", contentSource);
        }

        Assert.Contains("OnMediaSelected", mainViewCode);
        Assert.Contains("CoreMainViewModel_OnMediaSelected", mainViewCode);
        Assert.Contains("SelectedMediaHost", mainViewCode);
        Assert.Contains("SelectedMediaTitle", mainViewCode);
        Assert.Contains("SelectedMediaSubtitle", mainViewCode);
        Assert.Contains("Noctra.Mobile.Localization", mainViewCode);
        Assert.Contains("LocalizationSource.Instance", mainViewCode);
        Assert.Contains("Mobile.Status.Playback.Starting", mainViewCode);
        Assert.Contains("Mobile.Status.Media.Ready", mainViewCode);
        Assert.DoesNotContain("Starting Android playback.", mainViewCode);
        Assert.DoesNotContain("Media selection is ready.", mainViewCode);
    }

    [Fact]
    public void MobilePersonalViewsNavigation_ReusesDesktopAppViews()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("Tag=\"Search\"", mainViewSource);
        Assert.Contains("Tag=\"Favorites\"", mainViewSource);
        Assert.Contains("Tag=\"MyList\"", mainViewSource);
        Assert.Contains("Tag=\"History\"", mainViewSource);
        Assert.Contains("Tag=\"Downloads\"", mainViewSource);
        Assert.Contains("MobileSearchContent.DataContext", mainViewCode);
        Assert.Contains("MobileFavoritesContent.DataContext", mainViewCode);
        Assert.Contains("MobileMyListContent.DataContext", mainViewCode);
        Assert.Contains("MobileHistoryContent.DataContext", mainViewCode);
        Assert.Contains("MobileDownloadsContent.DataContext", mainViewCode);
        Assert.Contains("\"Search\" => AppView.Search", mainViewCode);
        Assert.Contains("\"Favorites\" => AppView.Favorites", mainViewCode);
        Assert.Contains("\"MyList\" => AppView.MyList", mainViewCode);
        Assert.Contains("\"History\" => AppView.History", mainViewCode);
        Assert.Contains("\"Downloads\" => AppView.Downloads", mainViewCode);
        Assert.Contains("destination is \"Home\" or \"Live\" or \"Movies\" or \"Series\" or \"Search\" or \"Favorites\" or \"MyList\" or \"History\" or \"Downloads\"", mainViewCode);
    }

    [Fact]
    public void AndroidPlayback_ReusesDesktopPlayerViewModelContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var androidVideoService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("IVideoPlayerService", androidVideoService);
        Assert.Contains("AndroidX.Media3.ExoPlayer", androidVideoService);
        Assert.Contains("IExoPlayer", androidVideoService);
        Assert.Contains("ExoPlayerBuilder", androidVideoService);
        Assert.Contains("PlayingChanged", androidVideoService);
        Assert.Contains("ErrorOccurred", androidVideoService);
        Assert.Contains("PositionChanged", androidVideoService);
        Assert.Contains("PlaybackEnded", androidVideoService);
        Assert.DoesNotContain("Android.Media.MediaPlayer", androidVideoService);
        Assert.DoesNotContain("MediaPlayer(", androidVideoService);
        Assert.DoesNotContain("player.Start();", androidVideoService);

        Assert.Contains("AddSingleton<IVideoPlayerService, AndroidVideoPlayerService>", registrationSource);
        Assert.Contains("AddSingleton<PlayerViewModel>", registrationSource);

        Assert.Contains("MobilePlayerView", mainViewSource);
        Assert.Contains("PlayerViewModel", mainViewCode);
        Assert.Contains("PlayChannelAsync", mainViewCode);
        Assert.Contains("PlayerHost", mainViewCode);
        Assert.Contains("MobilePlayerContent.DataContext", mainViewCode);

        Assert.Contains("vm:PlayerViewModel", playerViewSource);
        Assert.Contains("CurrentChannel.Name", playerViewSource);
        Assert.Contains("PauseCommand", playerViewSource);
        Assert.Contains("StopCommand", playerViewSource);
        Assert.Contains("CloseCommand", playerViewSource);
        Assert.Contains("IsPlaying", playerViewSource);
    }

    [Fact]
    public void MobilePlayerControls_ReusesDesktopPlayerControlContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("PlayPauseCommand", playerViewSource);
        Assert.Contains("ToggleMuteCommand", playerViewSource);
        Assert.Contains("Volume", playerViewSource);
        Assert.Contains("Position", playerViewSource);
        Assert.Contains("Duration", playerViewSource);
        Assert.Contains("PositionText", playerViewSource);
        Assert.Contains("DurationText", playerViewSource);
        Assert.Contains("RemainingTime", playerViewSource);
        Assert.Contains("StreamInfo", playerViewSource);
        Assert.Contains("QualityResolutionText", playerViewSource);
        Assert.Contains("QualityFpsText", playerViewSource);
        Assert.Contains("QualityAudioText", playerViewSource);
        Assert.Contains("StringFormatConverter", playerViewSource);
        Assert.Contains("Player.Mobile.LockFormat", playerViewSource);
        Assert.Contains("Player.Mobile.FullscreenFormat", playerViewSource);
        Assert.Contains("Player.Overlay.PiP.Tooltip", playerViewSource);
        Assert.Contains("Player.Episodes.Season", playerViewSource);
        Assert.Contains("SeasonNumber", playerViewSource);
        Assert.DoesNotContain("StringFormat='Lock: {0}'", playerViewSource);
        Assert.DoesNotContain("StringFormat='Fullscreen: {0}'", playerViewSource);
        Assert.DoesNotContain("Content=\"PiP\"", playerViewSource);
        Assert.DoesNotContain("StringFormat='Season {0}'", playerViewSource);
    }

    [Fact]
    public void MobilePlayerSecondaryControls_ReusesDesktopPlayerControlContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("SkipBackwardCommand", playerViewSource);
        Assert.Contains("SkipForwardCommand", playerViewSource);
        Assert.Contains("CommandParameter=\"10\"", playerViewSource);
        Assert.Contains("ToggleLiveFavoriteCommand", playerViewSource);
        Assert.Contains("CycleVideoFillModeCommand", playerViewSource);
        Assert.Contains("VideoFillMode", playerViewSource);
        Assert.Contains("ShowSleepTimerMenuCommand", playerViewSource);
        Assert.Contains("SleepTimerLabel", playerViewSource);
        Assert.Contains("SleepTimerCountdown", playerViewSource);
    }

    [Fact]
    public void MobilePlayerLiveChannelNavigation_ReusesDesktopEventBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("PlayPreviousLiveChannelCommand", playerViewSource);
        Assert.Contains("PlayNextLiveChannelCommand", playerViewSource);
        Assert.Contains("IsLiveContent", playerViewSource);

        Assert.Contains("NextLiveChannelRequested", mainViewCode);
        Assert.Contains("PreviousLiveChannelRequested", mainViewCode);
        Assert.Contains("PlayerViewModel_NextLiveChannelRequested", mainViewCode);
        Assert.Contains("PlayerViewModel_PreviousLiveChannelRequested", mainViewCode);
        Assert.Contains("PlayNextLiveChannelCommand.Execute(null)", mainViewCode);
        Assert.Contains("PlayPreviousLiveChannelCommand.Execute(null)", mainViewCode);
    }

    [Fact]
    public void MobilePlayerEpisodeNavigation_ReusesDesktopEventBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("PlayNextEpisodeCommand", playerViewSource);
        Assert.Contains("OpenEpisodesCommand", playerViewSource);
        Assert.Contains("IsSeriesContent", playerViewSource);

        Assert.Contains("NextEpisodeRequested", mainViewCode);
        Assert.Contains("EpisodeRequested", mainViewCode);
        Assert.Contains("PlayerViewModel_NextEpisodeRequested", mainViewCode);
        Assert.Contains("PlayerViewModel_EpisodeRequested", mainViewCode);
        Assert.Contains("PlayEpisodeCommand.Execute(episode)", mainViewCode);
    }

    [Fact]
    public void MobilePlayerSleepTimerPanel_ReusesDesktopSleepTimerContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("IsSleepTimerPanelOpen", playerViewSource);
        Assert.Contains("SetSleepTimerCommand", playerViewSource);
        Assert.Contains("CancelSleepTimerCommand", playerViewSource);
        Assert.Contains("PlayerViewModel+SleepTimerOption.Off", playerViewSource);
        Assert.Contains("PlayerViewModel+SleepTimerOption.Minutes15", playerViewSource);
        Assert.Contains("PlayerViewModel+SleepTimerOption.Minutes30", playerViewSource);
        Assert.Contains("PlayerViewModel+SleepTimerOption.Minutes60", playerViewSource);
        Assert.Contains("PlayerViewModel+SleepTimerOption.EndOfEpisode", playerViewSource);
        Assert.Contains("IsSleepTimerActive", playerViewSource);
        Assert.Contains("IsEnabled=\"{Binding IsPremium}\"", playerViewSource);
        Assert.Contains("IsVisible=\"{Binding !IsPremium}\"", playerViewSource);
        Assert.Contains("Kind=\"Lock\"", playerViewSource);
        Assert.Contains("MinHeight=\"48\"", playerViewSource);
    }

    [Fact]
    public void MobilePlayerInfoAndDownload_ReusesDesktopPlayerContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("DownloadCurrentContentCommand", playerViewSource);
        Assert.Contains("CanShowDownloadButton", playerViewSource);
        Assert.Contains("CanDownloadCurrentContent", playerViewSource);
        Assert.Contains("DownloadStatusMessage", playerViewSource);
        Assert.Contains("OpenInfoPanelCommand", playerViewSource);
        Assert.Contains("CanShowInfoButton", playerViewSource);
        Assert.Contains("IsInfoPanelOpen", playerViewSource);
        Assert.Contains("CurrentProgram.Title", playerViewSource);
        Assert.Contains("CurrentEpisodeDisplayTitle", playerViewSource);
        Assert.Contains("CurrentEpisodeMetaText", playerViewSource);
        Assert.Contains("CurrentChannel.Plot", playerViewSource);
    }

    [Fact]
    public void MobilePlayerAudioAndSubtitleSettings_ReusesDesktopPlayerContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("OpenAudioSettingsCommand", playerViewSource);
        Assert.Contains("IsAudioSettingsOpen", playerViewSource);
        Assert.Contains("SetSubtitleSizeCommand", playerViewSource);
        Assert.Contains("SetSubtitleBackgroundCommand", playerViewSource);
        Assert.Contains("SetSubtitlePositionCommand", playerViewSource);
        Assert.Contains("AudioTracks", playerViewSource);
        Assert.Contains("SetAudioTrackCommand", playerViewSource);
        Assert.Contains("SubtitleTracks", playerViewSource);
        Assert.Contains("SetSubtitleTrackCommand", playerViewSource);
    }

    [Fact]
    public void MobilePlayerQualityPanel_ReusesDesktopQualityContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));
        var mobileAppSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));
        var converterSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Converters",
            "DoubleToFloatConverter.cs"));

        Assert.Contains("OpenQualitySettingsCommand", playerViewSource);
        Assert.Contains("IsQualitySettingsOpen", playerViewSource);
        Assert.Contains("QualityResolutionText", playerViewSource);
        Assert.Contains("QualityFpsText", playerViewSource);
        Assert.Contains("QualityVideoCodecText", playerViewSource);
        Assert.Contains("QualityVideoBitrateText", playerViewSource);
        Assert.Contains("QualityAudioText", playerViewSource);
        Assert.Contains("SetPlaybackSpeedCommand", playerViewSource);
        Assert.Contains("ConverterParameter=0.5", playerViewSource);
        Assert.Contains("ConverterParameter=0.75", playerViewSource);
        Assert.Contains("ConverterParameter=1.0", playerViewSource);
        Assert.Contains("ConverterParameter=1.25", playerViewSource);
        Assert.Contains("ConverterParameter=1.5", playerViewSource);
        Assert.Contains("ConverterParameter=2.0", playerViewSource);
        Assert.Contains("DoubleToFloatConverter", playerViewSource);
        Assert.Contains("DoubleToFloatConverter", mobileAppSource);
        Assert.Contains("class DoubleToFloatConverter", converterSource);
    }

    [Fact]
    public void MobilePlayerWindowControls_ReusesDesktopPlayerContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));
        var mobilePlatformServiceResolverSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Services",
            "MobilePlatformServiceResolver.cs"));

        Assert.Contains("ToggleLockCommand", playerViewSource);
        Assert.Contains("ToggleFullScreenCommand", playerViewSource);
        Assert.Contains("EnterPiPCommand", playerViewSource);
        Assert.Contains("IsLocked", playerViewSource);
        Assert.Contains("IsFullScreen", playerViewSource);

        Assert.Contains("x:Name=\"HeaderBar\"", mainViewSource);
        Assert.Contains("PlayerViewModel_PropertyChanged", mainViewCode);
        Assert.Contains("UpdatePlayerChromeState", mainViewCode);
        Assert.Contains("nameof(PlayerViewModel.IsFullScreen)", mainViewCode);
        Assert.Contains("PiPRequested", mainViewCode);
        Assert.Contains("MobilePlatformServiceResolver", mainViewCode);
        Assert.Contains("GetPictureInPictureService", mobilePlatformServiceResolverSource);
        Assert.Contains("IPictureInPictureService", mobilePlatformServiceResolverSource);
    }

    [Fact]
    public void AndroidFullscreen_AllowsPortraitAndUserRotation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var playerWindowInterface = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Services",
            "Interfaces",
            "IPlayerWindowService.cs"));
        var androidPlayerWindowService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidPlayerWindowService.cs"));

        Assert.Contains("Tam ekran video moduna girer/çıkar.", playerWindowInterface);
        Assert.Contains("kullanıcı yön tercihi", playerWindowInterface);
        Assert.Contains("ScreenOrientation.FullUser", androidPlayerWindowService);
        Assert.Contains("ScreenOrientation.Unspecified", androidPlayerWindowService);
        Assert.DoesNotContain("ScreenOrientation.SensorLandscape", androidPlayerWindowService);
    }

    [Fact]
    public void MobileShellBackNavigation_UsesDoubleBackToExitToast()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));

        Assert.Contains("BackExitToast", mainViewSource);
        Assert.Contains("Shell.Mobile.BackToExit", mainViewSource);
        Assert.Contains("_lastBackExitPromptUtc", mainViewCode);
        Assert.Contains("BackExitPromptWindow", mainViewCode);
        Assert.Contains("ShowBackExitToast", mainViewCode);
        Assert.Contains("now - _lastBackExitPromptUtc <= BackExitPromptWindow", mainViewCode);
        Assert.Contains("_backExitToastTimer", mainViewCode);

        foreach (var culture in new[] { "en-US", "tr-TR", "de-DE", "es-ES", "fr-FR" })
        {
            var translations = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Noctra.Core",
                "Localization",
                "Translations",
                $"{culture}.json"));
            Assert.Contains("\"Shell.Mobile.BackToExit\"", translations);
        }
    }

    [Fact]
    public void MobilePlayerGestureHints_ShowOnceAndPersistDismissal()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSettingsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Models",
            "AppSettings.cs"));
        var playerViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml.cs"));
        var playerViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));

        Assert.Contains("HasSeenMobilePlayerGestureHints", appSettingsSource);
        Assert.Contains("TryShowGestureHintsOnceAsync", playerViewCode);
        Assert.Contains("ISettingsService", playerViewCode);
        Assert.Contains("Player.Mobile.Toast.GestureHints", playerViewCode);
        Assert.Contains("HasSeenMobilePlayerGestureHints = true", playerViewCode);
        Assert.Contains("SaveAsync", playerViewCode);
        Assert.Contains("IsGestureToastVisible", playerViewSource);
        Assert.Contains("GestureToastText", playerViewSource);

        foreach (var culture in new[] { "en-US", "tr-TR", "de-DE", "es-ES", "fr-FR" })
        {
            var translations = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Noctra.Core",
                "Localization",
                "Translations",
                $"{culture}.json"));
            Assert.Contains("\"Player.Mobile.Toast.GestureHints\"", translations);
        }
    }

    [Fact]
    public void AndroidPictureInPicture_ReusesPlayerPiPEventBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pipInterface = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Services",
            "Interfaces",
            "IPictureInPictureService.cs"));
        var pipService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidPictureInPictureService.cs"));
        var activitySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "MainActivity.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));

        Assert.Contains("interface IPictureInPictureService", pipInterface);
        Assert.Contains("EnterPictureInPictureAsync", pipInterface);
        Assert.Contains("PictureInPictureModeChanged", pipInterface);

        Assert.Contains("PictureInPictureParams", pipService);
        Assert.Contains("EnterPictureInPictureMode", pipService);
        Assert.Contains("Rational(16, 9)", pipService);
        Assert.Contains("OnPictureInPictureModeChanged", activitySource);
        Assert.Contains("SupportsPictureInPicture = true", activitySource);
        Assert.Contains("AndroidPictureInPictureService", registrationSource);
        Assert.Contains("AddSingleton<IPictureInPictureService>", registrationSource);
    }

    [Fact]
    public void AndroidPlaybackSpeed_ReappliesRateWhenPlaybackStartsAndResumes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var androidVideoService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs"));

        Assert.Contains("ApplyPlaybackRate", androidVideoService);
        Assert.Contains("ApplyPlaybackRate();", androidVideoService);
        Assert.Contains("_exoPlayer.PlayWhenReady = true;", androidVideoService);
        Assert.Contains("Resume()", androidVideoService);
        Assert.Contains("PlaybackParameters", androidVideoService);
        Assert.Contains("_exoPlayer.PlaybackParameters = playbackParameters", androidVideoService);

        var startIndex = androidVideoService.IndexOf("_exoPlayer.PlayWhenReady = true;", StringComparison.Ordinal);
        var preparedApplyIndex = androidVideoService.IndexOf("ApplyPlaybackRate();", startIndex, StringComparison.Ordinal);
        Assert.True(preparedApplyIndex > startIndex);

        var resumeIndex = androidVideoService.IndexOf("public void Resume()", StringComparison.Ordinal);
        var resumeApplyIndex = androidVideoService.IndexOf("ApplyPlaybackRate();", resumeIndex, StringComparison.Ordinal);
        Assert.True(resumeApplyIndex > resumeIndex);
    }

    [Fact]
    public void AndroidPlaybackQuality_PublishesPreparedVideoResolution()
    {
        var repositoryRoot = FindRepositoryRoot();
        var androidVideoService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs"));

        Assert.Contains("event EventHandler<StreamQualityInfo>? QualityDetected;", androidVideoService);
        Assert.Contains("UpdateStreamQuality", androidVideoService);
        Assert.Contains("_exoPlayer.VideoFormat", androidVideoService);
        Assert.Contains("_exoPlayer.AudioFormat", androidVideoService);
        Assert.Contains("new StreamQualityInfo", androidVideoService);
        Assert.Contains("StreamQuality =", androidVideoService);
        Assert.Contains("QualityDetected?.Invoke(this, quality)", androidVideoService);

        var readyIndex = androidVideoService.IndexOf("case BasePlayer.InterfaceConsts.StateReady:", StringComparison.Ordinal);
        var qualityIndex = androidVideoService.IndexOf("UpdateStreamQuality();", readyIndex, StringComparison.Ordinal);
        Assert.True(qualityIndex > readyIndex);
    }

    [Fact]
    public void AndroidPlaybackSurface_BindsExoPlayerToNativeTextureView()
    {
        var repositoryRoot = FindRepositoryRoot();
        var surfaceInterface = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Services",
            "Interfaces",
            "IVideoSurfaceService.cs"));
        var surfaceService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidVideoSurfaceService.cs"));
        var videoService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));
        var mainViewCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs"));
        var mobilePlatformServiceResolverSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Services",
            "MobilePlatformServiceResolver.cs"));

        Assert.Contains("interface IVideoSurfaceService", surfaceInterface);
        Assert.Contains("ShowAsync", surfaceInterface);
        Assert.Contains("Hide", surfaceInterface);

        Assert.Contains("TextureView", surfaceService);
        Assert.Contains("ISurfaceTextureListener", surfaceService);
        Assert.Contains("AndroidActivityProvider", surfaceService);
        Assert.Contains("WaitForSurfaceAsync", surfaceService);
        Assert.Contains("SurfaceTextureListener", surfaceService);

        Assert.Contains("AndroidVideoSurfaceService", videoService);
        Assert.Contains("WaitForSurfaceAsync", videoService);
        Assert.Contains("SetVideoSurface", videoService);
        Assert.Contains("ClearVideoSurface", videoService);
        Assert.Contains("SurfaceAvailable += VideoSurfaceService_SurfaceAvailable", videoService);
        Assert.Contains("SurfaceDestroyed += VideoSurfaceService_SurfaceDestroyed", videoService);
        Assert.DoesNotContain("SetSurface(", videoService);

        Assert.Contains("AddSingleton<AndroidVideoSurfaceService>", registrationSource);
        Assert.Contains("AddSingleton<IVideoSurfaceService>", registrationSource);

        Assert.Contains("MobilePlatformServiceResolver", mainViewCode);
        Assert.Contains("IVideoSurfaceService", mobilePlatformServiceResolverSource);
        Assert.Contains("GetVideoSurfaceService", mobilePlatformServiceResolverSource);
        Assert.Contains("ShowAsync", mainViewCode);
        Assert.Contains("Hide", mainViewCode);
    }

    [Fact]
    public void AndroidExoPlayerMigration_AppliesPlaybackSettingsAndRejectsLegacyMediaPlayerCode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var androidVideoService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs"));

        Assert.Contains("SetLoadControl(CreateLoadControl(_lastVideoBufferSize))", androidVideoService);
        Assert.Contains("DefaultLoadControl.Builder", androidVideoService);
        Assert.Contains("SetBufferDurationsMs", androidVideoService);

        Assert.Contains("ApplyDataUsageConstraints", androidVideoService);
        Assert.Contains("SetMaxVideoSize", androidVideoService);
        Assert.Contains("SetMaxVideoBitrate", androidVideoService);
        Assert.Contains("SetForceLowestBitrate", androidVideoService);

        Assert.Contains("NormalizeDurationSeconds", androidVideoService);
        Assert.Contains("C.TimeUnset", androidVideoService);
        Assert.Contains("_localizationService.GetString(\"Player.Error.NetworkOffline\")", androidVideoService);

        Assert.DoesNotContain("Android.Media.MediaPlayer", androidVideoService);
        Assert.DoesNotContain("UpdateStreamQualityFromPreparedPlayer", androidVideoService);
        Assert.DoesNotContain("OnPreparedListener", androidVideoService);
        Assert.DoesNotContain("player.Start();", androidVideoService);
    }

    [Fact]
    public void MobilePlayerPinchZoom_UsesNativeTextureViewTransform()
    {
        var repositoryRoot = FindRepositoryRoot();
        var surfaceInterface = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Services",
            "Interfaces",
            "IVideoSurfaceService.cs"));
        var mobilePlayerCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml.cs"));
        var androidSurfaceService = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "Services",
            "AndroidVideoSurfaceService.cs"));

        Assert.Contains("SetInteractionTransform", surfaceInterface);
        Assert.Contains("_activePointers", mobilePlayerCode);
        Assert.Contains("HandlePinchZoom", mobilePlayerCode);
        Assert.Contains("SetInteractionTransform", mobilePlayerCode);
        Assert.Contains("ResetInteractionTransform", mobilePlayerCode);
        Assert.Contains("_userZoom", androidSurfaceService);
        Assert.Contains("_userPanX", androidSurfaceService);
        Assert.Contains("ApplyInteractionTransform", androidSurfaceService);
        Assert.Contains("matrix.PostScale(_userZoom", androidSurfaceService);
        Assert.Contains("matrix.PostTranslate(_userPanX", androidSurfaceService);
        Assert.Contains("SetTransform(matrix)", androidSurfaceService);
    }

    [Fact]
    public void MobilePlayerVolumeToast_DoesNotDropFirstRealVolumeChange()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobilePlayerCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml.cs"));

        Assert.DoesNotContain("_isInitialVolumeEvent", mobilePlayerCode);
        Assert.Contains("_lastObservedVolume", mobilePlayerCode);
        Assert.Contains("_lastObservedIsMuted", mobilePlayerCode);
        Assert.Contains("ShowVolumeToastIfVolumeStateChanged", mobilePlayerCode);
        Assert.Contains("volumeChanged || muteChanged", mobilePlayerCode);
    }

    [Fact]
    public void MobilePlayerMaterialIcons_MatchDesktopActionSemantics()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobilePlayerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));
        var mobileAppSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml"));

        Assert.DoesNotContain("Kind=\"PlayPause\"", mobilePlayerSource);
        Assert.Contains("Kind=\"Pause\"", mobilePlayerSource);
        Assert.Contains("Kind=\"Play\"", mobilePlayerSource);
        Assert.Contains("Kind=\"FullscreenExit\"", mobilePlayerSource);
        Assert.Contains("IsVisible=\"{Binding IsFullScreen, Converter={StaticResource InverseBoolConverter}}\"", mobilePlayerSource);
        Assert.Contains("Kind=\"VolumeHigh\"", mobilePlayerSource);
        Assert.Contains("FillModeToIconConverter", mobileAppSource);
        Assert.Contains("Converter={StaticResource FillModeToIconConverter}", mobilePlayerSource);
    }

    [Fact]
    public void MobileUxPolish_UsesMobileFriendlyActionStatesAndTouchTargets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
        var mobilePlayerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));
        var downloadsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileDownloadsView.axaml"));
        var settingsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSettingsView.axaml"));
        var playerViewModelSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "ViewModels",
            "PlayerViewModel.cs"));

        Assert.DoesNotContain("Tag=\"Movies\" Click=\"OnDestinationClick\">\r\n          <StackPanel Spacing=\"2\" HorizontalAlignment=\"Center\">\r\n            <icons:MaterialIcon Kind=\"PlayBoxMultipleOutline\"", mainViewSource);
        Assert.DoesNotContain("Text=\"{loc:Translate Shell.Nav.Library}\" FontSize=\"11\"", mainViewSource);
        Assert.Contains("Text=\"{loc:Translate Shell.Nav.Movies}\" FontSize=\"11\"", mainViewSource);

        Assert.Contains("IsVisible=\"{Binding !IsLiveContent}\"", mobilePlayerSource);
        Assert.Contains("IsVisible=\"{Binding CanShowGoToLiveButton}\"", mobilePlayerSource);
        Assert.Contains("public bool CanShowGoToLiveButton", playerViewModelSource);

        Assert.Contains("Kind=\"Animation\"", mobilePlayerSource);
        Assert.Contains("Kind=\"StepForward\"", mobilePlayerSource);
        Assert.Contains("Kind=\"Download\"", mobilePlayerSource);
        Assert.Contains("Kind=\"InformationBoxOutline\"", mobilePlayerSource);
        Assert.Contains("Kind=\"Subtitles\"", mobilePlayerSource);

        Assert.DoesNotContain("MinHeight=\"34\"", downloadsSource);
        Assert.DoesNotContain("MinHeight=\"40\"", downloadsSource);

        Assert.Contains("Content=\"{loc:Translate Language.Turkish}\"", settingsSource);
        Assert.Contains("Content=\"{loc:Translate Language.English}\"", settingsSource);
        Assert.Contains("Content=\"{loc:Translate Language.French}\"", settingsSource);
        Assert.DoesNotContain("Content=\"Turkish\"", settingsSource);
        Assert.DoesNotContain("Content=\"Francais\"", settingsSource);
        Assert.DoesNotContain("Content=\"Espanol\"", settingsSource);
    }

    [Fact]
    public void MobileEpisodesOverlay_UsesSeasonTabsAndClearEpisodeStates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobilePlayerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml"));
        var playerViewModelSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "ViewModels",
            "PlayerViewModel.cs"));
        var episodeNavigatorSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "ViewModels",
            "Player",
            "PlayerEpisodeNavigator.cs"));

        Assert.Contains("SelectedEpisodeSeason", playerViewModelSource);
        Assert.Contains("SelectedEpisodeSeasonEpisodes", playerViewModelSource);
        Assert.Contains("SelectEpisodeSeason", playerViewModelSource);
        Assert.Contains("SelectedEpisodeSeason = selectedSeason", episodeNavigatorSource);

        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", mobilePlayerSource);
        Assert.Contains("ItemsSource=\"{Binding EpisodeSeasons}\"", mobilePlayerSource);
        Assert.Contains("Command=\"{Binding #PlayerRoot.DataContext.SelectEpisodeSeasonCommand}\"", mobilePlayerSource);
        Assert.Contains("ItemsSource=\"{Binding SelectedEpisodeSeasonEpisodes}\"", mobilePlayerSource);
        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding Episodes}\">", mobilePlayerSource);

        Assert.Contains("Kind=\"Close\"", mobilePlayerSource);
        Assert.Contains("Text=\"{loc:Translate Player.Info.NowPlaying}\"", mobilePlayerSource);
        Assert.Contains("Text=\"{loc:Translate Player.Overlay.Watched}\"", mobilePlayerSource);
        Assert.Contains("Height=\"6\"", mobilePlayerSource);
    }

    [Fact]
    public void MobileUxPolish_UsesAccessibleCardActionsAndReadableProgress()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mobileVodCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileVodCard.axaml"));
        var mobileSeriesCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileSeriesCard.axaml"));
        var mobileContinueWatchingCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileContinueWatchingCard.axaml"));
        var seriesDetailSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSeriesDetailView.axaml"));
        var settingsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSettingsView.axaml"));
        var liveSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileLiveView.axaml"));
        var mobileLiveTvCardSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileLiveTvCard.axaml"));
        var mobileLiveTvCardCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Controls",
            "MobileLiveTvCard.axaml.cs"));
        var historySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileHistoryView.axaml"));
        var myListSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileMyListView.axaml"));
        var searchSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MobileSearchView.axaml"));

        Assert.DoesNotContain("Width=\"36\"", mobileVodCardSource);
        Assert.DoesNotContain("Height=\"36\"", mobileVodCardSource);
        Assert.DoesNotContain("Width=\"36\"", mobileSeriesCardSource);
        Assert.DoesNotContain("Height=\"36\"", mobileSeriesCardSource);
        Assert.DoesNotContain("Width=\"36\"", mobileContinueWatchingCardSource);
        Assert.DoesNotContain("Height=\"36\"", mobileContinueWatchingCardSource);
        Assert.Contains("MinWidth=\"44\"", mobileVodCardSource);
        Assert.Contains("MinHeight=\"44\"", mobileSeriesCardSource);
        Assert.Contains("MinWidth=\"44\"", mobileContinueWatchingCardSource);

        Assert.DoesNotContain("MinHeight=\"34\"", settingsSource);
        Assert.DoesNotContain("MinWidth=\"28\"", seriesDetailSource);
        Assert.DoesNotContain("MinHeight=\"28\"", seriesDetailSource);
        Assert.Contains("Text=\"{loc:Translate Player.Overlay.Watched}\"", seriesDetailSource);
        Assert.Contains("Height=\"6\"", seriesDetailSource);

        Assert.Contains("<controls:MobileLiveTvCard", liveSource);
        Assert.Contains("Height=\"5\"", mobileLiveTvCardSource);
        Assert.DoesNotContain("MinHeight=\"40\"", searchSource);
        Assert.Contains("<controls:MobileLiveTvCard", searchSource);
        Assert.Contains("<controls:MobileSeriesCard", searchSource);
        Assert.Contains("<controls:MobileVodCard", searchSource);
        Assert.Contains("ToggleFavoriteCommand", mobileLiveTvCardSource);
        Assert.Contains("ShowHistoryMenuProperty", mobileLiveTvCardCode);
        Assert.Contains("ShowMyListMenuProperty", mobileLiveTvCardCode);
        Assert.Contains("RemoveFromHistoryCommand", mobileLiveTvCardSource);
        Assert.Contains("RemoveFromMyListCommand", mobileLiveTvCardSource);
        Assert.Contains("ShowHistoryMenu=\"True\"", historySource);
        Assert.Contains("ShowMyListMenu=\"True\"", myListSource);
        Assert.Contains("ToggleFavoriteCommand", mobileSeriesCardSource);
        Assert.Contains("AddToMyListCommand", mobileSeriesCardSource);
        Assert.Contains("ToggleFavoriteCommand", mobileVodCardSource);
        Assert.Contains("AddToMyListCommand", mobileVodCardSource);
        Assert.Contains("WatchedProgressVisibilityConverter", mobileVodCardSource);
    }

    [Fact]
    public void MainProfileLoading_SupportsLocalAndRemoteM3uSources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "ViewModels",
            "MainViewModel.cs"));

        Assert.Contains("IsHttpPlaylistSource", source);
        Assert.Contains("AddFromFileAsync(profile.Name, m3uSource, profile.Id)", source);
        Assert.Contains("AddFromUrlAsync(profile.Name, m3uSource, profile.Id)", source);
    }

    [Fact]
    public void SharedDownloadFlows_DoNotUseStaticDesktopPaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePaths = new[]
        {
            Path.Combine(repositoryRoot, "Noctra.Core", "Services", "ContentDownloadService.cs"),
            Path.Combine(repositoryRoot, "Noctra.Core", "ViewModels", "MainViewModel.cs"),
            Path.Combine(repositoryRoot, "Noctra.Core", "ViewModels", "SettingsViewModel.cs")
        };

        foreach (var sourcePath in sourcePaths)
        {
            var source = File.ReadAllText(sourcePath);
            Assert.DoesNotContain("AppPaths.", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DatabaseSchemaFixups_AreCentralizedInCoreService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var desktopAppSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Avalonia",
            "App.axaml.cs"));
        var mobileAppSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml.cs"));
        var schemaFixupSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "Services",
            "DatabaseSchemaFixupService.cs"));
        var serviceRegistrationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Core",
            "DependencyInjection",
            "ServiceCollectionExtensions.cs"));

        Assert.DoesNotContain("ALTER TABLE", desktopAppSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", mobileAppSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSqlRaw", desktopAppSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSqlRaw", mobileAppSource, StringComparison.Ordinal);

        Assert.Contains("IDatabaseSchemaFixupService", desktopAppSource);
        Assert.Contains("DatabaseSchemaFixupProfile.Desktop", desktopAppSource);
        Assert.Contains("IDatabaseSchemaFixupService", mobileAppSource);
        Assert.Contains("DatabaseSchemaFixupProfile.Mobile", mobileAppSource);

        Assert.Contains("ALTER TABLE", schemaFixupSource);
        Assert.Contains("ExecuteSqlRawAsync", schemaFixupSource);
        Assert.Contains("DatabaseSchemaFixupProfile.Mobile", schemaFixupSource);
        Assert.Contains("PRAGMA cache_size=-32000", schemaFixupSource);
        Assert.Contains("PRAGMA cache_size=-64000", schemaFixupSource);
        Assert.Contains("AddSingleton<IDatabaseSchemaFixupService, DatabaseSchemaFixupService>", serviceRegistrationSource);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
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
}
