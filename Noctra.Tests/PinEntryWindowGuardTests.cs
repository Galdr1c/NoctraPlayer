using System.IO;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// Masaüstü PIN penceresi davranış guard'ları. X ile kapatma (PinResult üretmeden)
/// iptal sayılmalıdır — "PIN'i unuttum / 3 gün içinde sil" onayı yalnızca açık
/// butonun null sonucuyla tetiklenir.
/// </summary>
public class PinEntryWindowGuardTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void ProfilesWindow_PinWindow_XClose_IsCancelNotForgotPin()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "Views", "ProfilesWindow.axaml.cs"));

        // X ile (PinResult üretmeden) kapanan pencere = iptal: "PIN'i unuttum"
        // onayı yalnızca açık butonun null sonucuyla tetiklenir.
        Assert.Contains("resultWasExplicit", source, StringComparison.Ordinal);
        Assert.Contains("if (!resultWasExplicit)", source, StringComparison.Ordinal);
        Assert.Contains("return false; // X ile kapatıldı", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PinEntryWindow_Escape_FiresCancelCommand()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Noctra.Avalonia", "Views", "PinEntryWindow.axaml.cs"));

        // X'i normalleştiren akışa ek olarak Escape de CancelCommand ile iptal eder.
        Assert.Contains("else if (e.Key == Key.Escape)", source, StringComparison.Ordinal);
        Assert.Contains("vm.CancelCommand.Execute(null)", source, StringComparison.Ordinal);
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
