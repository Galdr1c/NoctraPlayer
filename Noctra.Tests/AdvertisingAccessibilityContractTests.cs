namespace Noctra.Tests;

public sealed class AdvertisingAccessibilityContractTests
{
    [Fact]
    public void BannerNativeHost_UsesNonInteractiveAutomationPeer()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android", "Advertising", "AdMobMobileAdvertisingService.cs"));

        Assert.Contains("class BannerNativeControlHost : NativeControlHost", source, StringComparison.Ordinal);
        Assert.Contains("OnCreateAutomationPeer", source, StringComparison.Ordinal);
        Assert.Contains("new NoneAutomationPeer(this)", source, StringComparison.Ordinal);
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
