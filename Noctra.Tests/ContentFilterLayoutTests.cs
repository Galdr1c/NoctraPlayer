using System.Xml.Linq;

namespace Noctra.Tests;

public class ContentFilterLayoutTests
{
    [Fact]
    public void ContentFilterButtons_UseTheSharedMobileFirstPresentation()
    {
        var view = LoadProjectXaml("Noctra.UI", "Views", "AdaptiveCatalogView.axaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var categoryButton = view.Descendants()
            .Single(e => (string?)e.Attribute(x + "Name") == "CategorySelectionButton");
        var sortButton = view.Descendants()
            .Single(e => (string?)e.Attribute(x + "Name") == "SortSelectionButton");

        Assert.Contains("SettingsSelectionButton", (string?)categoryButton.Attribute("Classes") ?? string.Empty);
        Assert.Contains("SortIconButton", (string?)sortButton.Attribute("Classes") ?? string.Empty);
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
