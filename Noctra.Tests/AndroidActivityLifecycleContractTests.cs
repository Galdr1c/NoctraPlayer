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

    [Fact]
    public void Resume_LicenseRefresh_IsDeferredUntilForegroundSurfaceIsReady()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "MainActivity.cs"));
        var registration = File.ReadAllText(ProjectSource(
            "Noctra.Android", "DependencyInjection", "AndroidServiceCollectionExtensions.cs"));
        var license = File.ReadAllText(ProjectSource(
            "Noctra.Core", "Services", "LicenseService.cs"));

        Assert.Contains("QueueLicenseRefresh", source, StringComparison.Ordinal);
        Assert.Contains("PostDelayed", source, StringComparison.Ordinal);
        Assert.Contains("MobileAppLifecycle.IsGenerationCurrent", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_ = licenseService.RefreshSubscriptionStatusAsync();",
            source,
            StringComparison.Ordinal);
        Assert.Contains("deferInitialStoreRefresh: true", registration, StringComparison.Ordinal);
        Assert.Contains("if (!deferInitialStoreRefresh)", license, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
