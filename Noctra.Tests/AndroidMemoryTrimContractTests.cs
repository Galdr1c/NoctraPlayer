namespace Noctra.Tests;

public sealed class AndroidMemoryTrimContractTests
{
    [Fact]
    public void AndroidActivity_TrimsMobileImageCacheOnMemoryPressure()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");

        Assert.Contains("OnTrimMemory", activity, StringComparison.Ordinal);
        Assert.Contains("TrimImageCaches", activity, StringComparison.Ordinal);
        Assert.Contains("public static void TrimImageCaches", image, StringComparison.Ordinal);
        Assert.Contains("Cache.Clear()", image, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidActivity_SkipsInformationalTrimLevelsToAvoidResumeJank()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        // Real pressure levels must trim...
        foreach (var level in new[]
                 {
                     "TrimMemory.RunningLow",
                     "TrimMemory.RunningCritical",
                     "TrimMemory.Moderate",
                     "TrimMemory.Background",
                     "TrimMemory.Complete"
                 })
        {
            Assert.Contains(level, activity, StringComparison.Ordinal);
        }

        // ...while UiHidden must not: the user may return immediately and a
        // cleared cache turns that return into full poster re-decode jank.
        var trimStart = activity.IndexOf(
            "public override void OnTrimMemory", StringComparison.Ordinal);
        Assert.True(trimStart >= 0);
        var trimEnd = activity.IndexOf(
            "PerformanceTrace.Mark(\"android.memory.trim\"", trimStart, StringComparison.Ordinal);
        Assert.True(trimEnd > trimStart);
        Assert.DoesNotContain("TrimMemory.UiHidden", activity[trimStart..trimEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void CardControls_ReleaseViewModelSubscriptionsOnDetach()
    {
        var grid = ReadProjectFile(
            "Noctra.Mobile", "Controls", "MobileVirtualizingCardGrid.cs");
        var feed = ReadProjectFile(
            "Noctra.Mobile", "Controls", "MobileSectionedCardFeed.cs");

        // VM-owned collections must not keep detached controls alive through
        // CollectionChanged/PropertyChanged handler targets.
        Assert.Contains("UnsubscribeSourceCollection();", grid, StringComparison.Ordinal);
        Assert.Contains("ResubscribeSourceCollection();", grid, StringComparison.Ordinal);
        Assert.Contains("ClearSectionSubscriptions();", feed, StringComparison.Ordinal);
        // Re-attach must rebuild from current source state, since detached
        // changes were intentionally not observed.
        Assert.Contains("QueueFullRebuild();", grid, StringComparison.Ordinal);
        Assert.Contains("RefreshSectionSubscriptions();", feed, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindSolutionRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "NoctraPlayer.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate NoctraPlayer.sln.");
    }
}
