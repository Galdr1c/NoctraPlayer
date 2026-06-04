namespace Noctra.Tests;

public class TabSlideTransitionBehaviorTests
{
    [Fact]
    public void TabSlideTransitionBehavior_IgnoresSelectionChangedEventsFromChildControls()
    {
        var source = LoadProjectFile("Noctra.Avalonia", "Behaviors", "TabSlideTransitionBehavior.cs");

        Assert.Contains("ReferenceEquals(e.Source, tabControl)", source);
    }

    private static string LoadProjectFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find project file: {Path.Combine(relativeParts)}");
    }
}
