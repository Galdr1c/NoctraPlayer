namespace Noctra.Tests;

public sealed class AdultCategoryIntegrationContractTests
{
    [Fact]
    public void CategoryConsumersShareTheCentralAdultClassifier()
    {
        var mainViewModel = ReadProjectFile(
            "Noctra.Core", "ViewModels", "MainViewModel.cs");
        var organizer = ReadProjectFile(
            "Noctra.Core", "Services", "PlaylistOrganizerService.cs");
        var parser = ReadProjectFile(
            "Noctra.Core", "Services", "M3UParser.cs");

        Assert.Contains("AdultCategoryClassifier.IsAdultCategory", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("AdultCategoryClassifier.GetSortRank", organizer, StringComparison.Ordinal);
        Assert.Contains("AdultCategoryClassifier.IsAdultCategory", parser, StringComparison.Ordinal);
        Assert.DoesNotContain("AdultContentRegex", mainViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("AdultContentRegex", organizer, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
