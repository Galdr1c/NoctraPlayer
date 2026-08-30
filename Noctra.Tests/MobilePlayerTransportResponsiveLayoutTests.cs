using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Noctra.Tests;

public sealed class MobilePlayerTransportResponsiveLayoutTests
{
    [Fact]
    public void CompactThreshold_Covers411DpPhoneAndStopsAbove420Dp()
    {
        var source = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTransportBar.axaml.cs");

        var match = Regex.Match(
            source,
            @"NarrowLayoutMaxWidth\s*=\s*(?<value>\d+(?:\.\d+)?)d",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, "The narrow transport width threshold was not found.");

        var threshold = double.Parse(
            match.Groups["value"].Value,
            CultureInfo.InvariantCulture);

        Assert.Equal(420d, threshold);
        Assert.True(411d <= threshold, "A 411dp-wide phone must use the narrow layout.");
        Assert.True(420d <= threshold, "The threshold itself must remain narrow.");
        Assert.True(420.01d > threshold, "Widths above the threshold must keep the wide layout.");
        Assert.Contains(
            "width > 0d && width <= NarrowLayoutMaxWidth",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TransportXaml_SeparatesNarrowTimingAndKeepsTuneAccessible()
    {
        var path = FindProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTransportBar.axaml");
        var document = XDocument.Load(path);
        var source = File.ReadAllText(path);

        var root = document.Root!;
        Assert.Equal("TransportBarRoot", (string?)root.Attribute(XName.Get(
            "Name",
            "http://schemas.microsoft.com/winfx/2006/xaml")));

        var narrowTiming = FindNamedElement(document, "NarrowVodTimingRow");
        Assert.Equal("2", (string?)narrowTiming.Attribute("Grid.Row"));
        Assert.Contains("IsNarrowLayout", narrowTiming.ToString(), StringComparison.Ordinal);
        Assert.Contains("!IsLiveContent", narrowTiming.ToString(), StringComparison.Ordinal);

        var narrowBindings = narrowTiming.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .ToArray();
        Assert.Contains("{Binding DisplayedPositionText}", narrowBindings);
        Assert.Contains("{Binding DurationText}", narrowBindings);

        var wideTiming = FindNamedElement(document, "WideVodTimingRow");
        Assert.Contains("IsNarrowLayout", wideTiming.ToString(), StringComparison.Ordinal);
        Assert.Contains("!IsLiveContent", wideTiming.ToString(), StringComparison.Ordinal);

        var tuneButton = FindNamedElement(document, "TuneActionButton");
        Assert.Equal("7", (string?)tuneButton.Attribute("Grid.Column"));
        Assert.Contains(
            tuneButton.Descendants(),
            element => element.Name.LocalName == "MaterialIcon"
                       && (string?)element.Attribute("Kind") == "Tune");

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
