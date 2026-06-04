using System.IO;
using Xunit;

namespace Noctra.Tests;

public class LegalConsentWindowTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void LegalConsentWindow_ShouldRequireAllMandatoryAcknowledgementsBeforeContinue()
    {
        var axaml = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "Views", "LegalConsentWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "Views", "LegalConsentWindow.axaml.cs"));

        Assert.Contains("x:Name=\"TermsAcceptedCheckBox\"", axaml);
        Assert.Contains("x:Name=\"NoContentCheckBox\"", axaml);
        Assert.Contains("x:Name=\"LawfulSourcesCheckBox\"", axaml);
        Assert.Contains("x:Name=\"PrivacyAcceptedCheckBox\"", axaml);
        Assert.Contains("x:Name=\"ContinueButton\"", axaml);
        Assert.Contains("IsEnabled=\"False\"", axaml);

        Assert.Contains("TermsAcceptedCheckBox.IsChecked == true", codeBehind);
        Assert.Contains("NoContentCheckBox.IsChecked == true", codeBehind);
        Assert.Contains("LawfulSourcesCheckBox.IsChecked == true", codeBehind);
        Assert.Contains("PrivacyAcceptedCheckBox.IsChecked == true", codeBehind);
        Assert.Contains("ContinueButton.IsEnabled = allRequiredAccepted", codeBehind);
    }

    [Fact]
    public void LegalConsentWindow_ShouldExposeLegalDocumentsAndReturnDecision()
    {
        var axaml = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "Views", "LegalConsentWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "Views", "LegalConsentWindow.axaml.cs"));

        Assert.Contains("Click=\"Terms_Click\"", axaml);
        Assert.Contains("Click=\"Privacy_Click\"", axaml);
        Assert.Contains("DiagnosticDataCheckBox", axaml);
        Assert.Contains("public bool DiagnosticDataConsent", codeBehind);
        Assert.Contains("new LegalDocumentWindow", codeBehind);
        Assert.Contains("Close(LegalConsentResult.Accepted", codeBehind);
        Assert.Contains("Close(LegalConsentResult.Declined", codeBehind);
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
