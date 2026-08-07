namespace Noctra.Billing.Api.Tests;

/// <summary>
/// BillingConfig RTDN fail-fast davranışı: audience/email eksikse FromEnvironment
/// throw eder; RTDN kullanılmayacaksa NOCTRA_RTDN_DISABLED=1 açıkça izin verir.
/// </summary>
[Collection(BillingEnvCollection.Name)]
public sealed class BillingConfigRtdnTests : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new();

    public BillingConfigRtdnTests()
    {
        Save("NOCTRA_PACKAGE_NAME");
        Save("NOCTRA_SUBSCRIPTION_PRODUCT_IDS");
        Save("NOCTRA_LIFETIME_PRODUCT_IDS");
        Save("NOCTRA_BILLING_DB_PATH");
        Save("NOCTRA_GOOGLE_CREDENTIALS_JSON");
        Save("NOCTRA_RTDN_AUDIENCE");
        Save("NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL");
        Save("NOCTRA_RTDN_DISABLED");

        Environment.SetEnvironmentVariable("NOCTRA_PACKAGE_NAME", "studio.kynora.noctra");
        Environment.SetEnvironmentVariable("NOCTRA_SUBSCRIPTION_PRODUCT_IDS", "noctra_premium_monthly");
        Environment.SetEnvironmentVariable("NOCTRA_LIFETIME_PRODUCT_IDS", "noctra_premium_lifetime");
        Environment.SetEnvironmentVariable("NOCTRA_BILLING_DB_PATH", ":memory:");
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_AUDIENCE", "https://test-pubsub.example.com/push");
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL", "push-sa@test-project.iam.gserviceaccount.com");
    }

    public void Dispose()
    {
        foreach (var pair in _previous)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [Fact]
    public void MissingAudience_Throws()
    {
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_AUDIENCE", null);
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_DISABLED", null);

        Assert.Throws<InvalidOperationException>(() => BillingConfig.FromEnvironment());
    }

    [Fact]
    public void MissingServiceAccountEmail_Throws()
    {
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL", null);
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_DISABLED", null);

        Assert.Throws<InvalidOperationException>(() => BillingConfig.FromEnvironment());
    }

    [Fact]
    public void ExplicitlyDisabled_AllowsMissingRtdnVars()
    {
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_AUDIENCE", null);
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL", null);
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_DISABLED", "1");

        var config = BillingConfig.FromEnvironment();

        Assert.True(config.RtdnDisabled);
        Assert.Equal(string.Empty, config.RtdnAudience);
        Assert.Equal(string.Empty, config.RtdnServiceAccountEmail);
    }

    [Fact]
    public void Configured_ReadsAudienceAndEmail()
    {
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_AUDIENCE", "https://push.example.com/noctra");
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_SERVICE_ACCOUNT_EMAIL", "rtdn-sa@example.iam.gserviceaccount.com");
        Environment.SetEnvironmentVariable("NOCTRA_RTDN_DISABLED", null);

        var config = BillingConfig.FromEnvironment();

        Assert.False(config.RtdnDisabled);
        Assert.Equal("https://push.example.com/noctra", config.RtdnAudience);
        Assert.Equal("rtdn-sa@example.iam.gserviceaccount.com", config.RtdnServiceAccountEmail);
    }

    private void Save(string name) => _previous[name] = Environment.GetEnvironmentVariable(name);
}
