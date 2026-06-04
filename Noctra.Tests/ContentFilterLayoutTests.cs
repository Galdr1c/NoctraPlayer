using System.Xml.Linq;

namespace Noctra.Tests;

public class ContentFilterLayoutTests
{
    [Theory]
    [InlineData("LiveView.axaml")]
    [InlineData("MoviesView.axaml")]
    [InlineData("SeriesView.axaml")]
    public void ContentFilterComboboxes_AreWideEnoughForCategoryAndSortLabels(string viewFile)
    {
        var view = LoadProjectXaml("Noctra.Avalonia", "Views", viewFile);
        var comboBoxes = view.Descendants()
            .Where(e => e.Name.LocalName == "ComboBox")
            .ToList();

        var groupComboBox = comboBoxes.Single(e =>
            ((string?)e.Attribute("SelectedItem") ?? string.Empty).Contains("SelectedGroup"));
        var sortComboBox = comboBoxes.Single(e =>
            ((string?)e.Attribute("SelectedValue") ?? string.Empty).Contains("SelectedSortOrder"));

        Assert.Equal("320", (string?)groupComboBox.Attribute("Width"));
        Assert.Equal("300", (string?)sortComboBox.Attribute("Width"));
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
