using System.Xml.Linq;

namespace Noctra.Tests;

public sealed class DesktopSettingsSelectionSheetTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SettingsWindow_UsesRootLevelSelectionSheet()
    {
        var document = LoadProjectXaml("Noctra.Avalonia", "Views", "SettingsWindow.axaml");
        var sheet = FindNamedElement(document, "SettingsSelectionSheet");

        Assert.Equal("3", (string?)sheet.Attribute("Grid.RowSpan"));
        Assert.Equal("50000", (string?)sheet.Attribute("ZIndex"));
        Assert.Equal("DesktopSelectionSheet", sheet.Name.LocalName);
        Assert.Empty(document.Descendants().Where(element => element.Name.LocalName == "ComboBox"));
    }

    [Fact]
    public void GlobalSettingsWindow_UsesRootLevelSelectionSheet()
    {
        var document = LoadProjectXaml(
            "Noctra.Avalonia",
            "Views",
            "GlobalSettingsWindow.axaml");
        var sheet = FindNamedElement(document, "GlobalSettingsSelectionSheet");

        Assert.Equal("2", (string?)sheet.Attribute("Grid.RowSpan"));
        Assert.Equal("50000", (string?)sheet.Attribute("ZIndex"));
        Assert.Equal("DesktopSelectionSheet", sheet.Name.LocalName);
        Assert.Empty(document.Descendants().Where(element => element.Name.LocalName == "ComboBox"));

        var languageButton = FindNamedElement(document, "GlobalLanguageSelectionButton");
        Assert.Equal(
            "OpenGlobalLanguageSelection_Click",
            (string?)languageButton.Attribute("Click"));
        Assert.Contains(
            languageButton.Descendants(),
            element =>
                (string?)element.Attribute(Xaml + "Name") ==
                "GlobalLanguageSelectionValue");
    }

    [Fact]
    public void SelectionSheet_IsBottomAlignedBoundedAndVirtualized()
    {
        var document = LoadProjectXaml(
            "Noctra.Avalonia",
            "Views",
            "DesktopSelectionSheet.axaml");
        var surface = FindNamedElement(document, "SheetSurface");

        Assert.Equal("Bottom", (string?)surface.Attribute("VerticalAlignment"));
        Assert.Equal("Center", (string?)surface.Attribute("HorizontalAlignment"));
        Assert.Equal(
            "{DynamicResource DesktopSheetMaxWidth}",
            (string?)surface.Attribute("MaxWidth"));
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "VirtualizingStackPanel");
    }

    [Fact]
    public void SettingsCatalog_DefinesAllSelectionsAndTwentyFiveTimezones()
    {
        var source = ReadProjectFile(
            "Noctra.Avalonia",
            "Views",
            "DesktopSettingsSelectionCatalog.cs");

        Assert.Contains("BuildDataUsage", source);
        Assert.Contains("BuildDownloadQualities", source);
        Assert.Contains("BuildPlaybackLanguages", source);
        Assert.Contains("BuildRefreshFrequencies", source);
        Assert.Contains("BuildHistoryRetention", source);
        Assert.Contains("BuildAppLanguages", source);
        Assert.Contains("Enumerable.Range(0, 25)", source);
        Assert.Contains("index => index > 0 && !isPremium", source);
    }

    [Fact]
    public void Application_IncludesDesktopModernStyles()
    {
        var document = LoadProjectXaml("Noctra.Avalonia", "App.axaml");

        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "StyleInclude" &&
                (string?)element.Attribute("Source") ==
                "avares://Noctra/Resources/DesktopModernStyles.axaml");
    }

    [Theory]
    [InlineData("DataUsageSelectionButton", "OpenDataUsageSelection_Click")]
    [InlineData("SubtitleLanguageSelectionButton", "OpenSubtitleLanguageSelection_Click")]
    [InlineData("AudioLanguageSelectionButton", "OpenAudioLanguageSelection_Click")]
    [InlineData("DownloadQualitySelectionButton", "OpenDownloadQualitySelection_Click")]
    [InlineData("ChannelRefreshSelectionButton", "OpenChannelRefreshSelection_Click")]
    [InlineData("EpgRefreshSelectionButton", "OpenEpgRefreshSelection_Click")]
    [InlineData("EpgTimezoneSelectionButton", "OpenEpgTimezoneSelection_Click")]
    [InlineData("HistoryRetentionSelectionButton", "OpenHistoryRetentionSelection_Click")]
    [InlineData("AppLanguageSelectionButton", "OpenAppLanguageSelection_Click")]
    public void SettingsWindow_SelectionButtonsExposeCurrentValue(
        string buttonName,
        string clickHandler)
    {
        var document = LoadProjectXaml("Noctra.Avalonia", "Views", "SettingsWindow.axaml");
        var button = FindNamedElement(document, buttonName);

        Assert.Equal(clickHandler, (string?)button.Attribute("Click"));
        Assert.Contains(
            button.Descendants(),
            element =>
                element.Name.LocalName == "TextBlock" &&
                ((string?)element.Attribute(Xaml + "Name"))?.EndsWith(
                    "SelectionValue",
                    StringComparison.Ordinal) == true);
    }

    private static XElement FindNamedElement(XDocument document, string name)
        => document.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static XDocument LoadProjectXaml(params string[] relativeParts)
        => XDocument.Load(FindProjectFile(relativeParts));

    private static string ReadProjectFile(params string[] relativeParts)
        => File.ReadAllText(FindProjectFile(relativeParts));

    private static string FindProjectFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "Noctra.Core")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. relativeParts]);
    }
}
