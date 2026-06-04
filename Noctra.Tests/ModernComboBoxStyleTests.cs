using System.Xml.Linq;

namespace Noctra.Tests;

public class ModernComboBoxStyleTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ModernComboBox_KeepsSelectedContentInsideTextColumn()
    {
        var styles = LoadProjectXaml("Noctra.Avalonia", "Resources", "Styles.axaml");
        var comboTheme = styles.Descendants()
            .Single(e => e.Name.LocalName == "ControlTheme"
                         && (string?)e.Attribute(XamlNamespace + "Key") == "ModernComboBox");

        var border = comboTheme.Descendants()
            .Single(e => e.Name.LocalName == "Border" && (string?)e.Attribute(XamlNamespace + "Name") == "PART_Border");
        Assert.Equal("{TemplateBinding Padding}", (string?)border.Attribute("Padding"));

        var selectedContent = comboTheme.Descendants()
            .Single(e => e.Name.LocalName == "ContentPresenter" && (string?)e.Attribute(XamlNamespace + "Name") == "ContentPresenter");
        Assert.Equal("0", (string?)selectedContent.Attribute("Grid.Column"));
        Assert.Equal("Left", (string?)selectedContent.Attribute("HorizontalAlignment"));
        Assert.Null(selectedContent.Attribute("ClipToBounds"));
        Assert.Null(selectedContent.Attribute("Margin"));
    }

    [Fact]
    public void Application_AppliesModernComboBoxAndTextTrimmingGlobally()
    {
        var app = LoadProjectXaml("Noctra.Avalonia", "App.axaml");

        var comboStyle = app.Descendants()
            .Single(e => e.Name.LocalName == "Style"
                         && (string?)e.Attribute("Selector") == "ComboBox");
        var comboSetters = comboStyle.Elements().Where(e => e.Name.LocalName == "Setter").ToList();

        Assert.Contains(comboSetters, setter =>
            (string?)setter.Attribute("Property") == "Theme"
            && (string?)setter.Attribute("Value") == "{StaticResource ModernComboBox}");
        Assert.Contains(comboSetters, setter =>
            (string?)setter.Attribute("Property") == "Cursor"
            && (string?)setter.Attribute("Value") == "Hand");

        var comboTextStyle = app.Descendants()
            .Single(e => e.Name.LocalName == "Style"
                         && (string?)e.Attribute("Selector") == "ComboBox TextBlock");
        var textSetters = comboTextStyle.Elements().Where(e => e.Name.LocalName == "Setter").ToList();

        Assert.Contains(textSetters, setter =>
            (string?)setter.Attribute("Property") == "TextTrimming"
            && (string?)setter.Attribute("Value") == "CharacterEllipsis");
        Assert.Contains(textSetters, setter =>
            (string?)setter.Attribute("Property") == "MaxLines"
            && (string?)setter.Attribute("Value") == "1");
    }

    [Fact]
    public void SettingsRefreshFrequencyItems_DoNotUseFixedWidthContent()
    {
        var settings = LoadProjectXaml("Noctra.Avalonia", "Views", "SettingsWindow.axaml");
        var refreshComboboxes = settings.Descendants()
            .Where(e => e.Name.LocalName == "ComboBox"
                        && ((string?)e.Attribute("SelectedIndex") ?? string.Empty) is var selectedIndex
                        && (selectedIndex.Contains("ChannelListRefreshFrequencyIndex")
                            || selectedIndex.Contains("EpgRefreshFrequencyIndex")))
            .ToList();

        Assert.Equal(2, refreshComboboxes.Count);

        foreach (var comboBox in refreshComboboxes)
        {
            var fixedWidthGrids = comboBox.Descendants()
                .Where(e => e.Name.LocalName == "Grid"
                            && (string?)e.Attribute("ColumnDefinitions") == "*,Auto"
                            && e.Attribute("Width") is not null)
                .ToList();

            Assert.Empty(fixedWidthGrids);
        }
    }

    private static XDocument LoadProjectXaml(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find project XAML: {Path.Combine(relativeParts)}");
    }
}
