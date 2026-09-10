namespace Noctra.Tests;

public sealed class SharedThemeCanonicalizationTests
{
    [Fact]
    public void BothHosts_LoadDarkAndLightThemesFromSharedUi()
    {
        var mobileApp = Read("Noctra.Mobile", "App.axaml");
        var desktopApp = Read("Noctra.Avalonia", "App.axaml");

        foreach (var app in new[] { mobileApp, desktopApp })
        {
            Assert.Contains("avares://Noctra.UI/Resources/Themes/DarkTheme.axaml", app, StringComparison.Ordinal);
            Assert.Contains("avares://Noctra.UI/Resources/Themes/LightTheme.axaml", app, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("avares://Noctra.Mobile/Resources/Themes/", mobileApp, StringComparison.Ordinal);
        Assert.DoesNotContain("avares://Noctra/Resources/Themes/", desktopApp, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedThemes_PreserveMobilePlayerSheetPaletteAndCrossPlatformSemanticSurfaces()
    {
        var dark = Read("Noctra.UI", "Resources", "Themes", "DarkTheme.axaml");
        var light = Read("Noctra.UI", "Resources", "Themes", "LightTheme.axaml");

        Assert.Contains("x:Key=\"PlayerOverlaySheetBrush\" Color=\"#F20A0A0A\"", dark, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PlayerOverlaySheetTextBrush\" Color=\"#FFFFFFFF\"", dark, StringComparison.Ordinal);

        Assert.Contains("x:Key=\"PlayerOverlaySheetBrush\" Color=\"#F2FAFAFA\"", light, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PlayerOverlaySheetTextBrush\" Color=\"#1A1535\"", light, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PlayerOverlaySheetSurface1Brush\" Color=\"#EBEBEB\"", light, StringComparison.Ordinal);

        foreach (var theme in new[] { dark, light })
        {
            Assert.Contains("x:Key=\"SuccessSurfaceBrush\"", theme, StringComparison.Ordinal);
            Assert.Contains("x:Key=\"WarningSurfaceBrush\"", theme, StringComparison.Ordinal);
            Assert.Contains("x:Key=\"ErrorSurfaceBrush\"", theme, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CanonicalSharedResources_RemoveDeadPlatformCopies()
    {
        foreach (var path in new[]
                 {
                     Path.Combine("Noctra.Mobile", "Resources", "Colors.axaml"),
                     Path.Combine("Noctra.Mobile", "Resources", "Tokens.axaml"),
                     Path.Combine("Noctra.Mobile", "Resources", "CommonStyles.axaml"),
                     Path.Combine("Noctra.Mobile", "Resources", "SettingsStyles.axaml"),
                     Path.Combine("Noctra.Mobile", "Resources", "Themes", "DarkTheme.axaml"),
                     Path.Combine("Noctra.Mobile", "Resources", "Themes", "LightTheme.axaml"),
                     Path.Combine("Noctra.Avalonia", "Resources", "Colors.axaml"),
                     Path.Combine("Noctra.Avalonia", "Resources", "Tokens.axaml"),
                     Path.Combine("Noctra.Avalonia", "Resources", "Themes", "DarkTheme.axaml"),
                     Path.Combine("Noctra.Avalonia", "Resources", "Themes", "LightTheme.axaml")
                 })
        {
            Assert.False(File.Exists(Path.Combine(Root(), path)), $"Dead platform resource copy still exists: {path}");
        }
    }

    [Fact]
    public void ThemePicker_StacksBothCardsFullWidthBelowBreakpoint()
    {
        var picker = Read("Noctra.UI", "Views", "SettingsThemePickerView.axaml.cs");

        Assert.Contains("width < 340", picker, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(DarkThemeButton, stacked ? 2 : 1)", picker, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(LightThemeButton, stacked ? 2 : 1)", picker, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopRail_DisablesTooltipsWhileExpandedWithoutBreakingLocalizationBinding()
    {
        var shell = Read("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");

        Assert.Contains("button.Bind(ToolTip.TipProperty", shell, StringComparison.Ordinal);
        Assert.Contains("ToolTip.ServiceEnabledProperty, !expanded", shell, StringComparison.Ordinal);
        Assert.Contains("ToolTip.IsOpenProperty, false", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopResumeAction_UsesSharedPillRadiusToken()
    {
        var mainWindow = Read("Noctra.Avalonia", "MainWindow.axaml");

        Assert.Contains("<Style Selector=\"Button.ResumePrimaryButton\">", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"{DynamicResource RadiusPill}\" />", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"CornerRadius\" Value=\"81\" />", mainWindow, StringComparison.Ordinal);
    }

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine([Root(), .. path]));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
