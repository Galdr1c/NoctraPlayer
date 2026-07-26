using System.Xml.Linq;

namespace Noctra.Tests;

public class ContentFilterLayoutTests
{
    [Theory]
    [InlineData("LiveView.axaml")]
    [InlineData("MoviesView.axaml")]
    [InlineData("SeriesView.axaml")]
    public void ContentFilterButtons_UseCategoryAndCompactSortPresentations(string viewFile)
    {
        var view = LoadProjectXaml("Noctra.Avalonia", "Views", viewFile);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var categoryButton = view.Descendants()
            .Single(e => (string?)e.Attribute(x + "Name") == "CategorySelectionButton");
        var sortButton = view.Descendants()
            .Single(e => (string?)e.Attribute(x + "Name") == "SortSelectionButton");

        Assert.Contains("DesktopFilterButton", (string?)categoryButton.Attribute("Classes") ?? string.Empty);
        Assert.Equal("44", (string?)sortButton.Attribute("Width"));
        Assert.Equal("44", (string?)sortButton.Attribute("Height"));
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
