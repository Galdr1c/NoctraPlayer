using System.Text.RegularExpressions;

namespace Noctra.Tests;

public sealed class PlayBillingCatalogTests
{
    [Fact]
    public void AndroidCatalog_QueriesMonthlyAndLifetimeIndependently()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Android", "Services", "AndroidStorePurchaseService.cs"));

        Assert.Matches(new Regex(
            @"QueryAvailableProductDetailsAsync\(\s*client,\s*StoreProducts\.MonthlySubscription,\s*BillingClient\.ProductType\.Subs",
            RegexOptions.CultureInvariant), source);
        Assert.Matches(new Regex(
            @"QueryAvailableProductDetailsAsync\(\s*client,\s*StoreProducts\.LifetimePurchase,\s*BillingClient\.ProductType\.Inapp",
            RegexOptions.CultureInvariant), source);
        Assert.DoesNotContain("var entries = new List<QueryProductDetailsParams.Product>", source);
    }

    [Fact]
    public void MobileUpsell_EmptyCatalogExposesRetry()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var emptyCatalogBranch = source.IndexOf("if (_products.Count == 0)", StringComparison.Ordinal);
        var catchBlock = source.IndexOf("catch (Exception ex)", emptyCatalogBranch, StringComparison.Ordinal);

        Assert.True(emptyCatalogBranch >= 0, "The empty catalog branch must remain explicit.");
        Assert.True(catchBlock > emptyCatalogBranch,
            "The empty catalog branch must be before the exception fallback.");
        var emptyCatalogBody = source.Substring(emptyCatalogBranch, catchBlock - emptyCatalogBranch);
        Assert.Contains("RetryPricingButton.IsVisible = true;", emptyCatalogBody);
        Assert.True(emptyCatalogBody.Contains("RetryPricingButton.IsVisible = true;", StringComparison.Ordinal),
            "An empty Play catalog must expose the pricing retry action.");
    }

    [Fact]
    public void PlayBundlePackaging_RejectsMissingBillingEndpointBeforePublish()
    {
        var source = File.ReadAllText(ProjectFile("build", "package-play.ps1"));
        var error = source.IndexOf(
            "[ERROR] NOCTRA_BILLING_VERIFY_URL is missing",
            StringComparison.Ordinal);
        var publish = source.IndexOf("& dotnet @publishArgs", StringComparison.Ordinal);

        Assert.True(error >= 0, "Play packaging must reject a missing billing verification URL.");
        Assert.True(publish > error, "The billing endpoint guard must run before dotnet publish.");
        Assert.Contains("-p:NOCTRA_BILLING_VERIFY_URL=$BillingVerifyUrl", source);
    }

    [Fact]
    public void BillingDeployScript_UsesGcloudCmdInsteadOfPowerShellWrapper()
    {
        var source = File.ReadAllText(ProjectFile("deploy-billing.ps1"));

        Assert.Contains("Get-Command gcloud.cmd", source);
        Assert.Contains("$GcloudPath", source);
        Assert.DoesNotContain("    gcloud @Args", source);
        Assert.Contains("$output = & $GcloudPath @Args 2>&1", source);
        Assert.Contains("$saDescribeExit = $LASTEXITCODE", source);
        Assert.Contains("$previousErrorActionPreference = $ErrorActionPreference", source);
        Assert.Contains("$ErrorActionPreference = \"Continue\"", source);
    }

    private static string ProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
