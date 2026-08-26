namespace Noctra.Tests;

public sealed class MobileAppUpdateRemovalTests
{
    [Fact]
    public void MobileSettings_RemovesTheAppUpdateSurfaceAndAndroidWiring()
    {
        var settingsView = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var settingsViewModel = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");
        var androidProject = ReadProjectFile("Noctra.Android", "Noctra.Android.csproj");
        var androidDi = ReadProjectFile("Noctra.Android", "DependencyInjection", "AndroidServiceCollectionExtensions.cs");
        var androidActivity = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        Assert.DoesNotContain("GlobalSettings.Update.", settingsView, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckForUpdatesCommand", settingsView, StringComparison.Ordinal);
        Assert.DoesNotContain("IAppUpdateService", settingsViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("GooglePlayUpdateService", androidProject, StringComparison.Ordinal);
        Assert.DoesNotContain("GooglePlayUpdateService", androidDi, StringComparison.Ordinal);
        Assert.DoesNotContain("GooglePlayUpdateService", androidActivity, StringComparison.Ordinal);
        Assert.DoesNotContain("Xamarin.Google.Android.Play.App.Update", androidProject, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
