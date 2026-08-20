namespace Noctra.Tests;

public sealed class AndroidActivityLifecycleContractTests
{
    [Fact]
    public void LauncherActivityReusesExistingTaskOnRepeatedForegroundLaunches()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "MainActivity.cs"));

        Assert.Contains("LaunchMode = LaunchMode.SingleTask", source, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
