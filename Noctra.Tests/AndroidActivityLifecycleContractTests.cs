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

    [Fact]
    public void ColdStart_LicenseRefresh_RetriesUntilAvaloniaServicesExist()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "MainActivity.cs"));

        Assert.Contains("QueueLicenseRefreshWhenServicesReady", source, StringComparison.Ordinal);
        Assert.Contains("LicenseServiceReadyRetryDelayMs", source, StringComparison.Ordinal);
        Assert.Contains("LicenseServiceReadyMaxAttempts", source, StringComparison.Ordinal);
        Assert.Contains("TryResolveLicenseServices", source, StringComparison.Ordinal);
        Assert.Contains("decorView.PostDelayed(", source, StringComparison.Ordinal);
        Assert.Contains(
            "License refresh bootstrap exhausted before Avalonia services became ready",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OnStop_PausesOpeningAndBufferingPlaybackOutsidePip()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "MainActivity.cs"));

        // Zero tolerance outside PiP: a stream still opening/buffering when the
        // app leaves the foreground must not start playing in the background.
        Assert.Contains("player.State == PlaybackState.Opening", source, StringComparison.Ordinal);
        Assert.Contains("player.State == PlaybackState.Buffering", source, StringComparison.Ordinal);

        // Only a player that was actually playing may auto-resume on return; a
        // paused-seek buffering window must not earn a resume.
        Assert.Contains("_pausedByLifecycle = player.IsPlaying;", source, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
