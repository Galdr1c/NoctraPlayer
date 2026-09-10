namespace Noctra.Tests;

public sealed class SharedMobileSearchInputContractTests
{
    [Fact]
    public void SharedSearch_PreservesMobileSearchKeyboardHintContract()
    {
        var mobileApp = Source("Noctra.Mobile", "App.axaml");
        var sharedSearch = Source("Noctra.UI", "Views", "AdaptiveSearchView.axaml");
        var mobileSearch = Source("Noctra.Mobile", "Views", "MobileSearchView.axaml");
        var categorySearch = Source("Noctra.Mobile", "Views", "MobileCategorySelectionView.axaml");

        Assert.Contains("<Style Selector=\"TextBox.search\">", mobileApp, StringComparison.Ordinal);
        Assert.Contains("TextInputOptions.ContentType\" Value=\"Search\"", mobileApp, StringComparison.Ordinal);
        Assert.Contains("TextInputOptions.ReturnKeyType\" Value=\"Search\"", mobileApp, StringComparison.Ordinal);

        Assert.Contains("Classes=\"search\"", sharedSearch, StringComparison.Ordinal);
        Assert.Contains("TextInputOptions.ReturnKeyType=\"Search\"", sharedSearch, StringComparison.Ordinal);
        Assert.Contains("<shared:AdaptiveSearchView", mobileSearch, StringComparison.Ordinal);
        Assert.Contains("Classes=\"search\"", categorySearch, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedSearch_TextBoxInheritsAccessibleMobileTouchAndSelectionPolicy()
    {
        var mobileApp = Source("Noctra.Mobile", "App.axaml");
        var sharedSearch = Source("Noctra.UI", "Views", "AdaptiveSearchView.axaml");
        var darkTheme = Source("Noctra.UI", "Resources", "Themes", "DarkTheme.axaml");
        var lightTheme = Source("Noctra.UI", "Resources", "Themes", "LightTheme.axaml");

        Assert.Contains("<Style Selector=\"TextBox\">", mobileApp, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"48\"", mobileApp, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CaretBrush\" Value=\"{DynamicResource AccentBrush}\"", mobileApp, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"SelectionBrush\" Value=\"{DynamicResource TextSelectionBrush}\"", mobileApp, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"SelectionForegroundBrush\" Value=\"{DynamicResource TextPrimaryBrush}\"", mobileApp, StringComparison.Ordinal);

        var textBoxStart = sharedSearch.IndexOf("<TextBox", StringComparison.Ordinal);
        var textBoxEnd = sharedSearch.IndexOf("/>", textBoxStart, StringComparison.Ordinal);
        Assert.True(textBoxStart >= 0 && textBoxEnd > textBoxStart);
        var searchTextBox = sharedSearch[textBoxStart..(textBoxEnd + 2)];
        Assert.DoesNotContain("MinHeight=\"44\"", searchTextBox, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"46\"", searchTextBox, StringComparison.Ordinal);

        foreach (var theme in new[] { darkTheme, lightTheme })
            Assert.Contains("<SolidColorBrush x:Key=\"TextSelectionBrush\" Color=\"#997C3AED\" />", theme, StringComparison.Ordinal);
    }

    private static string Source(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. path]));
    }
}
