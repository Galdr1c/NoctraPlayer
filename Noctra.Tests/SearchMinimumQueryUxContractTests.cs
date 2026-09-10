using System.Text.Json;

namespace Noctra.Tests;

public sealed class SearchMinimumQueryUxContractTests
{
    [Fact]
    public void SharedSearch_ExposesLocalizedMinimumLengthHintAndCommandGuard()
    {
        var source = File.ReadAllText(FindProjectFile(
            "Noctra.UI",
            "Views",
            "AdaptiveSearchView.axaml"));

        Assert.Contains("ShowSearchMinimumLengthHint", source, StringComparison.Ordinal);
        Assert.Contains("Search.MinimumLengthHint", source, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanCommitSearch}\"", source, StringComparison.Ordinal);
        Assert.Contains("Search.Placeholder", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileAndDesktopSearch_HostTheSameSharedSurface()
    {
        var mobile = File.ReadAllText(FindProjectFile(
            "Noctra.Mobile", "Views", "MobileSearchView.axaml"));
        var desktop = File.ReadAllText(FindProjectFile(
            "Noctra.Avalonia", "Views", "SearchView.axaml"));

        Assert.Contains("<shared:AdaptiveSearchView", mobile, StringComparison.Ordinal);
        Assert.Contains("<shared:AdaptiveSearchView", desktop, StringComparison.Ordinal);
        Assert.Contains("MobileSectionedCardFeed", mobile, StringComparison.Ordinal);
        Assert.Contains("DesktopSectionedCardFeed", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void MinimumLengthHint_IsLocalizedInEverySupportedLanguage()
    {
        foreach (var language in new[] { "en-US", "tr-TR", "de-DE", "fr-FR", "es-ES" })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(FindProjectFile(
                "Noctra.Core",
                "Localization",
                "Translations",
                $"{language}.json")));

            var value = document.RootElement.GetProperty("Search.MinimumLengthHint").GetString();
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.Contains("2", value!, StringComparison.Ordinal);
        }
    }

    private static string FindProjectFile(params string[] pathParts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(pathParts).ToArray());
        if (File.Exists(path))
            return path;

        throw new FileNotFoundException($"Could not find project file: {Path.Combine(pathParts)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
