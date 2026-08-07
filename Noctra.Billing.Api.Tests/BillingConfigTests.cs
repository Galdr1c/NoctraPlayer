namespace Noctra.Billing.Api.Tests;

/// <summary>
/// BillingConfig fail-fast davranışı: aynı product ID hem subscription hem
/// lifetime listesindeyse FromEnvironment throw eder — yanlış config sessizce
/// tolere edilmez (ResolveEntitlementType subscription'ı kazanırdı).
/// </summary>
[Collection(BillingEnvCollection.Name)]
public sealed class BillingConfigTests : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new();

    public BillingConfigTests()
    {
        Save("NOCTRA_PACKAGE_NAME");
        Save("NOCTRA_SUBSCRIPTION_PRODUCT_IDS");
        Save("NOCTRA_LIFETIME_PRODUCT_IDS");

        Environment.SetEnvironmentVariable("NOCTRA_PACKAGE_NAME", "studio.kynora.noctra");
    }

    public void Dispose()
    {
        foreach (var pair in _previous)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [Fact]
    public void OverlappingProductId_Throws()
    {
        Environment.SetEnvironmentVariable("NOCTRA_SUBSCRIPTION_PRODUCT_IDS", "noctra_premium,noctra_premium_monthly");
        Environment.SetEnvironmentVariable("NOCTRA_LIFETIME_PRODUCT_IDS", "noctra_premium_lifetime,noctra_premium");

        var exception = Assert.Throws<InvalidOperationException>(() => BillingConfig.FromEnvironment());

        Assert.Contains("noctra_premium", exception.Message);
    }

    [Fact]
    public void DisjointProductIds_Loads()
    {
        Environment.SetEnvironmentVariable("NOCTRA_SUBSCRIPTION_PRODUCT_IDS", "noctra_premium_monthly");
        Environment.SetEnvironmentVariable("NOCTRA_LIFETIME_PRODUCT_IDS", "noctra_premium_lifetime");

        var config = BillingConfig.FromEnvironment();

        Assert.True(config.IsKnownProduct("noctra_premium_monthly"));
        Assert.True(config.IsKnownProduct("noctra_premium_lifetime"));
        Assert.Equal("Subscription", config.ResolveEntitlementType("noctra_premium_monthly"));
        Assert.Equal("Lifetime", config.ResolveEntitlementType("noctra_premium_lifetime"));
    }

    [Fact]
    public void BothListsEmpty_Throws()
    {
        Environment.SetEnvironmentVariable("NOCTRA_SUBSCRIPTION_PRODUCT_IDS", "");
        Environment.SetEnvironmentVariable("NOCTRA_LIFETIME_PRODUCT_IDS", "");

        Assert.Throws<InvalidOperationException>(() => BillingConfig.FromEnvironment());
    }

    private void Save(string name) => _previous[name] = Environment.GetEnvironmentVariable(name);
}
