using System.Xml.Linq;

namespace Noctra.Tests;

public sealed class DesktopCardInteractionTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LiveCard_UsesFullSurfacePlaybackButtonAndDoesNotOfferMyList()
    {
        var document = LoadProjectXaml("LiveTvCard.axaml");
        var playbackButton = FindNamedElement(document, "PlaybackButton");

        Assert.Equal("3", (string?)playbackButton.Attribute("Grid.RowSpan"));
        Assert.Contains(
            "SelectMediaCommand",
            (string?)playbackButton.Attribute("Command") ?? string.Empty);
        Assert.DoesNotContain(
            document.Descendants().Where(element => element.Name.LocalName == "MenuItem"),
            item => (string?)item.Attribute("Click") == "Context_AddToMyList_Click");
    }

    [Theory]
    [InlineData("VodCard.axaml", "VodCardControl")]
    [InlineData("SeriesCard.axaml", "SeriesCardControl")]
    public void PersonalListCards_ShowOneContextActionPerMembership(
        string fileName,
        string controlName)
    {
        var document = LoadProjectXaml(fileName);
        var menuItems = document.Descendants()
            .Where(element => element.Name.LocalName == "MenuItem")
            .ToArray();

        var toggleMyList = menuItems.Single(
            item => (string?)item.Attribute("Click") == "Context_AddToMyList_Click");
        var removeMyList = menuItems.Single(
            item => (string?)item.Attribute("Click") == "Context_RemoveFromMyList_Click");
        var toggleFavorite = menuItems.Single(
            item => (string?)item.Attribute("Click") == "Context_ToggleFavorite_Click");
        var removeFavorite = menuItems.Single(
            item => (string?)item.Attribute("Click") == "Context_RemoveFromFavorites_Click");

        Assert.Contains(
            $"#{controlName}.ShowRemoveMyListMenu",
            (string?)toggleMyList.Attribute("IsVisible") ?? string.Empty);
        Assert.Contains(
            "InverseBoolConverter",
            (string?)toggleMyList.Attribute("IsVisible") ?? string.Empty);
        Assert.Contains("MyList.Remove", (string?)removeMyList.Attribute("Header") ?? string.Empty);

        Assert.Contains(
            $"#{controlName}.ShowRemoveFavoriteMenu",
            (string?)toggleFavorite.Attribute("IsVisible") ?? string.Empty);
        Assert.Contains(
            "InverseBoolConverter",
            (string?)toggleFavorite.Attribute("IsVisible") ?? string.Empty);
        Assert.Contains(
            "Context.Favorite.Toggle",
            (string?)removeFavorite.Attribute("Header") ?? string.Empty);
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
