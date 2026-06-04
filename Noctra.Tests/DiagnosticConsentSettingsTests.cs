using System.IO;
using Xunit;

namespace Noctra.Tests;

public class DiagnosticConsentSettingsTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void GlobalSettingsWindow_ShouldPresentDiagnosticsInsteadOfProductAnalytics()
    {
        var axaml = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "Views", "GlobalSettingsWindow.axaml"));

        Assert.Contains("GlobalSettings.General.Diagnostics.Title", axaml);
        Assert.Contains("GlobalSettings.General.Diagnostics.Description", axaml);
        Assert.Contains("IsChecked=\"{Binding Settings.DiagnosticDataConsent}\"", axaml);
        Assert.DoesNotContain("GlobalSettings.General.Analytics.Title", axaml);
        Assert.DoesNotContain("GlobalSettings.General.Analytics.Description", axaml);
    }

    [Fact]
    public void GlobalSettingsViewModel_ShouldLoadAndSaveDiagnosticConsent()
    {
        var viewModelCode = File.ReadAllText(Path.Combine(Root, "Noctra.Core", "ViewModels", "GlobalSettingsViewModel.cs"));

        Assert.Contains("DiagnosticDataConsent = s.DiagnosticDataConsent", viewModelCode);
        Assert.Contains("s.DiagnosticDataConsent = Settings.DiagnosticDataConsent", viewModelCode);
        Assert.DoesNotContain("s.Analytics = Settings.DiagnosticDataConsent", viewModelCode);
        Assert.Contains("private bool _diagnosticDataConsent = false", viewModelCode);
    }

    [Fact]
    public void Translations_ShouldUseDiagnosticsWordingForGlobalSetting()
    {
        var tr = File.ReadAllText(Path.Combine(Root, "Noctra.Core", "Localization", "Translations", "tr-TR.json"));
        var en = File.ReadAllText(Path.Combine(Root, "Noctra.Core", "Localization", "Translations", "en-US.json"));

        Assert.Contains("\"GlobalSettings.General.Diagnostics.Title\": \"Tanılama ve çökme raporları\"", tr);
        Assert.Contains("\"GlobalSettings.General.Diagnostics.Title\": \"Diagnostics and crash reports\"", en);
        Assert.DoesNotContain("\"GlobalSettings.General.Analytics.Title\": \"Kullanım istatistikleri\"", tr);
        Assert.DoesNotContain("\"GlobalSettings.General.Analytics.Title\": \"Usage analytics\"", en);
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
