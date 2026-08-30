using System.Xml.Linq;

namespace Noctra.Tests;

public sealed class MobilePlayerTransportResponsiveLayoutTests
{
    [Fact]
    public void TimingLayout_DoesNotDependOnOrientationSpecificWidthThreshold()
    {
        var source = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTransportBar.axaml.cs");

        Assert.DoesNotContain("NarrowLayoutMaxWidth", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsNarrowLayout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UsesNarrowLayout", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TransportXaml_UsesSharedVodTimingRowAcrossOrientations()
    {
        var path = FindProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTransportBar.axaml");
        var document = XDocument.Load(path);
        var source = File.ReadAllText(path);

        var timing = FindNamedElement(document, "VodTimingRow");
        Assert.Equal("2", (string?)timing.Attribute("Grid.Row"));
        Assert.DoesNotContain("IsNarrowLayout", timing.ToString(), StringComparison.Ordinal);
        Assert.Contains("!IsLiveContent", timing.ToString(), StringComparison.Ordinal);

        var timingBindings = timing.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .ToArray();
        Assert.Contains("{Binding DisplayedPositionText}", timingBindings);
        Assert.Contains("{Binding DurationText}", timingBindings);

        Assert.DoesNotContain("NarrowVodTimingRow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WideVodTimingRow", source, StringComparison.Ordinal);

        var tuneButton = FindNamedElement(document, "TuneActionButton");
        Assert.Equal("6", (string?)tuneButton.Attribute("Grid.Column"));
        Assert.Contains(
            tuneButton.Descendants(),
            element => element.Name.LocalName == "MaterialIcon"
                       && (string?)element.Attribute("Kind") == "Tune");

        var epgButton = document.Descendants()
            .Single(element => (string?)element.Attribute("Command") == "{Binding ToggleEpgPanelCommand}");
        Assert.Equal("5", (string?)epgButton.Attribute("Grid.Column"));

        Assert.Contains("<Setter Property=\"Width\" Value=\"48\" />", source, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"48\" />", source, StringComparison.Ordinal);
    }

    private static XElement FindNamedElement(XDocument document, string name)
    {
        var xName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        return document.Descendants().Single(element => (string?)element.Attribute(xName) == name);
    }

    private static string LoadProjectFile(params string[] pathParts) =>
        File.ReadAllText(FindProjectFile(pathParts));

    private static string FindProjectFile(params string[] pathParts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(pathParts).ToArray());
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"Could not find project file: {Path.Combine(pathParts)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
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
