namespace Noctra.Tests;

public sealed class SharedUiHardeningTests
{
    [Fact]
    public void ThemePicker_ReconnectsAfterVisualTreeReattachment()
    {
        var source = Read("Noctra.UI", "Views", "SettingsThemePickerView.axaml.cs");

        Assert.Contains("OnAttachedToVisualTree", source, StringComparison.Ordinal);
        Assert.Contains("AttachViewModel", source, StringComparison.Ordinal);
        Assert.Contains("DetachViewModel", source, StringComparison.Ordinal);
        Assert.Contains("DataContext as SettingsViewModel", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemePicker_UsesBalancedAdaptiveCards()
    {
        var xaml = Read("Noctra.UI", "Views", "SettingsThemePickerView.axaml");
        var code = Read("Noctra.UI", "Views", "SettingsThemePickerView.axaml.cs");
        var desktopHost = Read("Noctra.Avalonia", "Views", "SettingsWindow.SharedUi.cs");

        Assert.Contains("MinWidth=\"160\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"104\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemeLayoutRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyAdaptiveLayout", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(LightThemeButton, stacked ? 1 : 0)", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 340", desktopHost, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 380", desktopHost, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicDesktopRailActions_BindToLocalizationKeys()
    {
        var shell = Read("Noctra.Avalonia", "MainWindow.MobileFirstShell.cs");

        Assert.Contains("CreateRailButton(MaterialIconKind.Magnify, \"Shell.Search.Tooltip\"", shell, StringComparison.Ordinal);
        Assert.Contains("CreateRailButton(MaterialIconKind.CogOutline, \"Settings.Title\"", shell, StringComparison.Ordinal);
        Assert.Contains("new Binding($\"[{localizationKey}]\")", shell, StringComparison.Ordinal);
        Assert.Contains("Source = LocalizationSource.Instance", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = label", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeAdapters_UseNamedAnchorsInsteadOfPositionalChildren()
    {
        var settingsXaml = Read("Noctra.Avalonia", "Views", "SettingsWindow.axaml");
        var settingsAdapter = Read("Noctra.Avalonia", "Views", "SettingsWindow.SharedUi.cs");
        var playerXaml = Read("Noctra.Avalonia", "Views", "VideoOverlayView.axaml");
        var playerAdapter = Read("Noctra.Avalonia", "Views", "VideoOverlayView.SharedPresentation.cs");

        foreach (var name in new[] { "ProfileSummaryCard", "ProfileManagementCard", "ProfileAccountCard", "ThemeOptionsHost" })
            Assert.Contains($"x:Name=\"{name}\"", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GetVisualDescendants", settingsAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Take(3)", settingsAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Children.Clear()", settingsAdapter, StringComparison.Ordinal);

        foreach (var name in new[] { "LegacyTopGradient", "LegacyTopBar", "LegacyTransportControls" })
            Assert.Contains($"x:Name=\"{name}\"", playerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Children[0]", playerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Children[1]", playerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Children[2]", playerAdapter, StringComparison.Ordinal);
    }

    [Fact]
    public void SupersededMobilePlayerComponents_AreRemoved()
    {
        foreach (var name in new[]
                 {
                     "MobilePlayerTimeline",
                     "MobilePlayerTransportBar",
                     "MobilePlayerMoreSheet",
                     "MobilePlayerTrackSheet",
                     "MobilePlayerQualitySheet",
                     "MobilePlayerSleepSheet",
                     "MobilePlayerSubtitleAppearanceSheet",
                     "MobilePlayerInfoSheet"
                 })
        {
            Assert.False(File.Exists(Path.Combine(Root(), "Noctra.Mobile", "Views", "Player", $"{name}.axaml")));
            Assert.False(File.Exists(Path.Combine(Root(), "Noctra.Mobile", "Views", "Player", $"{name}.axaml.cs")));
        }
    }

    [Fact]
    public void DesktopContinueWatchingRows_CreateTheCorrectCardAndAspectRatio()
    {
        var presenter = Read("Noctra.Avalonia", "Controls", "DesktopCardRowPresenter.cs");

        Assert.Contains("DesktopCardGridKind.ContinueWatching => new ContinueWatchingCard()", presenter, StringComparison.Ordinal);
        Assert.Contains("DesktopCardGridKind.ContinueWatching => Math.Round(cardWidth * 146d / 260d)", presenter, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([Root(), .. parts]));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
