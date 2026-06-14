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
        Assert.Contains("TabletBreakpoint = 720", codeSource);
        Assert.Contains("NavigationRail.IsVisible = useNavigationRail", codeSource);
        Assert.Contains("BottomNavigation.IsVisible = !useNavigationRail", codeSource);
    }

    [Fact]
    public void AndroidEntryPoint_UsesAvalonia12ActivityLifetime()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activitySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Android",
            "MainActivity.cs"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "App.axaml.cs"));

        Assert.Contains("MainActivity : AvaloniaMainActivity", activitySource);
        Assert.DoesNotContain("AvaloniaMainActivity<App>", activitySource);
        Assert.Contains("IActivityApplicationLifetime", appSource);
        Assert.Contains("MainViewFactory", appSource);
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
        var mainViewSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Noctra.Mobile",
            "Views",
            "MainView.axaml"));
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

        Assert.Contains("OpenProfileSetup", mainViewSource);
        Assert.Contains("ProfileSetupHost", mainViewSource);
        Assert.Contains("AddTransient<AddProfileViewModel>", registrationSource);
        Assert.Contains("IDialogService", registrationSource);
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
}
