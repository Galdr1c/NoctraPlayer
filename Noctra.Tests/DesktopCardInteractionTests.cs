using System.Xml.Linq;

namespace Noctra.Tests;

public sealed class DesktopCardInteractionTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("LiveTvCard.axaml")]
    [InlineData("VodCard.axaml")]
    [InlineData("SeriesCard.axaml")]
    [InlineData("ContinueWatchingCard.axaml")]
    public void Cards_UseDesktopPressableCardWithSelectMediaCommand(string fileName)
    {
        var document = LoadProjectXaml(fileName);
        var container = document.Descendants()
            .Single(element => element.Name.LocalName == "DesktopPressableCard");

        Assert.Contains(
            "SelectMediaCommand",
            (string?)container.Attribute("Command") ?? string.Empty);
        Assert.Equal(
            "OnCardRightTapped",
            (string?)container.Attribute("CardRightTapped"));
    }

    private static XElement FindNamedElement(XDocument document, string name)
        => document.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static XDocument LoadProjectXaml(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "Noctra.Core")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return XDocument.Load(
            Path.Combine(
                directory!.FullName,
                "Noctra.Avalonia",
                "Controls",
                fileName));
    }
}
