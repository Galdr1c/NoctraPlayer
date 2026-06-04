using System.IO;
using Xunit;

namespace Noctra.Tests;

public class LegalConsentStartupTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void AppStartup_ShouldGateProfilesWindowBehindLegalConsent()
    {
        var appCode = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "App.axaml.cs"));

        Assert.Contains("services.AddTransient<LegalConsentWindow>()", appCode);
        Assert.Contains("RequiresLegalConsent(settingsService.Settings)", appCode);
        Assert.Contains("ShowLegalConsentAsync(settingsService, splashWindow)", appCode);

        var consentIndex = appCode.IndexOf("ShowLegalConsentAsync(settingsService, splashWindow)", StringComparison.Ordinal);
        var showProfilesIndex = appCode.IndexOf("desktop.MainWindow = profilesWindow", StringComparison.Ordinal);

        Assert.True(consentIndex >= 0);
        Assert.True(showProfilesIndex >= 0);
        Assert.True(consentIndex < showProfilesIndex);
    }

    [Fact]
    public void AppStartup_ShouldPersistAcceptedLegalConsentVersionAndTimestamp()
    {
        var appCode = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "App.axaml.cs"));

        Assert.Contains("settings.LegalConsentAccepted = true", appCode);
        Assert.Contains("settings.LegalConsentVersion = AppSettings.CurrentLegalConsentVersion", appCode);
        Assert.Contains("settings.PrivacyNoticeVersion = AppSettings.CurrentPrivacyNoticeVersion", appCode);
        Assert.Contains("settings.LegalConsentAcceptedAtUtc = DateTime.UtcNow", appCode);
        Assert.Contains("await settingsService.SaveAsync()", appCode);
        Assert.Contains("desktop.Shutdown()", appCode);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NoctraPlayer.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
