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
    public void AndroidCatalog_ReadsProductDetailsFromAsyncResultCallbackList()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Android", "Services", "AndroidStorePurchaseService.cs"));

        Assert.Contains("result.ProductDetails?.ToArray()", source);
        Assert.DoesNotContain("result.ProductDetailsList?.ToArray()", source);
    }

    [Fact]
    public void AndroidCatalog_UsesNestedPricingPhasesJniSignature()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Android", "Services", "AndroidStorePurchaseService.cs"));

        Assert.Contains(
            "getPricingPhases", source);
        Assert.Contains(
            "()Lcom/android/billingclient/api/ProductDetails$PricingPhases;",
            source);
        Assert.DoesNotContain(
            "()Lcom/android/billingclient/api/PricingPhases;",
            source);
    }

    [Fact]
    public void AndroidPurchases_AcknowledgeOnlyAfterBackendVerification()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Android", "Services", "AndroidStorePurchaseService.cs"));
        var entitlementStart = source.IndexOf(
            "public async Task<StoreEntitlement> GetEntitlementAsync",
            StringComparison.Ordinal);
        var verifierStart = source.IndexOf(
            "private async Task<BillingVerifiedEntitlement?> VerifyTokenAsync",
            entitlementStart,
            StringComparison.Ordinal);
        var callbackStart = source.IndexOf(
            "private void OnPurchasesUpdated",
            StringComparison.Ordinal);
        var callbackEnd = source.IndexOf(
            "// ==========================================",
            callbackStart,
            StringComparison.Ordinal);

        Assert.True(entitlementStart >= 0, "The entitlement query method must remain explicit.");
        Assert.True(verifierStart > entitlementStart, "Token verification must remain below entitlement queries.");
        Assert.True(callbackStart > verifierStart, "The purchase callback must remain below the entitlement logic.");
        Assert.True(callbackEnd > callbackStart, "The purchase callback boundary must remain explicit.");

        var entitlementBody = source.Substring(entitlementStart, verifierStart - entitlementStart);
        var firstVerification = entitlementBody.IndexOf(
            "var verified = await VerifyTokenAsync(purchase",
            StringComparison.Ordinal);
        var firstAcknowledgement = entitlementBody.IndexOf(
            "AcknowledgeIfNeeded(purchase);",
            StringComparison.Ordinal);
        var secondVerification = entitlementBody.IndexOf(
            "var verified = await VerifyTokenAsync(purchase",
            firstVerification + 1,
            StringComparison.Ordinal);
        var secondAcknowledgement = entitlementBody.IndexOf(
            "AcknowledgeIfNeeded(purchase);",
            firstAcknowledgement + 1,
            StringComparison.Ordinal);

        Assert.True(firstVerification >= 0, "The lifetime token must be backend-verified.");
        Assert.True(secondVerification > firstVerification, "The subscription token must be backend-verified separately.");
        Assert.True(firstAcknowledgement > firstVerification,
            "The lifetime purchase must be acknowledged only after backend verification.");
        Assert.True(secondAcknowledgement > secondVerification,
            "The subscription purchase must be acknowledged only after backend verification.");

        var callbackBody = source.Substring(callbackStart, callbackEnd - callbackStart);
        Assert.DoesNotContain("AcknowledgeIfNeeded(purchase);", callbackBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobileUpsell_OffersPurchaseRestoreInsteadOfFreeContinuation()
    {
        var xaml = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml"));

        Assert.Contains("x:Name=\"RestorePurchasesButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"RestorePurchases_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Upsell.Action.Restore", xaml, StringComparison.Ordinal);
        var restoreStart = xaml.IndexOf(
            "x:Name=\"RestorePurchasesButton\"",
            StringComparison.Ordinal);
        var restoreEnd = xaml.IndexOf("</Button>", restoreStart, StringComparison.Ordinal);
        Assert.True(restoreEnd > restoreStart, "The restore button must have a complete XAML element.");
        var restoreMarkup = xaml.Substring(restoreStart, restoreEnd - restoreStart);
        Assert.DoesNotContain("Upsell.Action.Dismiss", restoreMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Close_Click", restoreMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileUpsell_RestoreRefreshesAuthoritativeStoreEntitlement()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var restoreStart = source.IndexOf(
            "private async void RestorePurchases_Click",
            StringComparison.Ordinal);
        var nextHandlerStart = source.IndexOf(
            "private async void MonthlyBuy_Click",
            restoreStart,
            StringComparison.Ordinal);

        Assert.True(restoreStart >= 0, "The mobile upsell must expose a restore handler.");
        Assert.True(nextHandlerStart > restoreStart, "The restore handler must have a bounded method body.");

        var restoreBody = source.Substring(restoreStart, nextHandlerStart - restoreStart);
        var restoreCall = restoreBody.IndexOf(
            "RestorePurchasesAsync",
            StringComparison.Ordinal);
        var refreshCall = restoreBody.IndexOf(
            "RefreshSubscriptionStatusAsync",
            StringComparison.Ordinal);

        Assert.True(restoreCall >= 0, "Restore must query the platform purchase service.");
        Assert.True(refreshCall > restoreCall,
            "Restore must refresh the shared entitlement after the store query.");
        Assert.Contains("Upsell.Restore.Unavailable", restoreBody, StringComparison.Ordinal);
        Assert.Contains("Upsell.Restore.NotFound", restoreBody, StringComparison.Ordinal);
        Assert.Contains("Upsell.Restore.Failed", restoreBody, StringComparison.Ordinal);
        Assert.Contains("Upsell.Restore.Success", restoreBody, StringComparison.Ordinal);
        Assert.Contains("Upsell.Restore.SuccessTitle", restoreBody, StringComparison.Ordinal);
        var notificationCall = restoreBody.IndexOf("ShowNotificationAsync", StringComparison.Ordinal);
        var closeCall = restoreBody.IndexOf("TryClose();", notificationCall, StringComparison.Ordinal);
        Assert.True(notificationCall >= 0, "A successful restore must use the existing notification service.");
        Assert.True(closeCall > notificationCall,
            "The success notification must be scheduled before the upsell closes.");
        Assert.Contains("SetRestoring", restoreBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPurchaseFlowAsync", restoreBody, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidRestore_UsesAuthoritativeBackendEntitlementPath()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Android", "Services", "AndroidStorePurchaseService.cs"));
        var restoreStart = source.IndexOf(
            "public async Task RestorePurchasesAsync",
            StringComparison.Ordinal);
        var lifecycleStart = source.IndexOf(
            "// BillingClient lifecycle",
            restoreStart,
            StringComparison.Ordinal);

        Assert.True(restoreStart >= 0, "The Android restore entry point must remain explicit.");
        Assert.True(lifecycleStart > restoreStart, "The restore method must precede billing lifecycle helpers.");
        var restoreBody = source.Substring(restoreStart, lifecycleStart - restoreStart);
        Assert.Contains("GetEntitlementAsync(cancellationToken)", restoreBody, StringComparison.Ordinal);
        Assert.Contains("EntitlementChanged?.Invoke", restoreBody, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileUpsell_RestoreMessagesExistInEverySupportedTranslation()
    {
        var translations = new[] { "tr-TR.json", "en-US.json", "de-DE.json", "es-ES.json", "fr-FR.json" };
        var requiredKeys = new[]
        {
            "Upsell.Action.Restore",
            "Upsell.Restore.Unavailable",
            "Upsell.Restore.NotFound",
            "Upsell.Restore.Failed",
            "Upsell.Restore.Success",
            "Upsell.Restore.SuccessTitle"
        };

        foreach (var translation in translations)
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(ProjectFile(
                "Noctra.Core", "Localization", "Translations", translation)));
            foreach (var key in requiredKeys)
            {
                var value = document.RootElement.GetProperty(key).GetString();
                Assert.False(string.IsNullOrWhiteSpace(value),
                    $"{key} must be translated in {translation}.");
            }
        }
    }

    [Fact]
    public void MobileUpsell_SuccessStartsEntitlementCompletionWatch()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var successBranch = source.IndexOf("if (result.Success)", StringComparison.Ordinal);
        var cancelledBranch = source.IndexOf("if (result.CancelledByUser)", successBranch, StringComparison.Ordinal);

        Assert.True(successBranch >= 0, "The purchase success branch must remain explicit.");
        Assert.True(cancelledBranch > successBranch,
            "The success branch must precede cancellation handling.");
        var successBody = source.Substring(successBranch, cancelledBranch - successBranch);
        Assert.Contains("StartPurchaseCompletionWatch", successBody);
    }

    [Fact]
    public void MobileUpsell_HidesOnlyAfterPlayBillingFlowStartsSuccessfully()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var launch = source.IndexOf(
            "var result = await store.LaunchPurchaseAsync(product)",
            StringComparison.Ordinal);
        var success = source.IndexOf("if (result.Success)", launch, StringComparison.Ordinal);
        var cancelled = source.IndexOf(
            "if (result.CancelledByUser)", success, StringComparison.Ordinal);
        var hide = source.IndexOf("HideForPurchaseFlow();", success, StringComparison.Ordinal);

        Assert.True(launch >= 0, "The Play Billing launch call must remain explicit.");
        Assert.True(success > launch, "The success branch must follow the Billing launch call.");
        Assert.True(hide > success && hide < cancelled,
            "The sheet may be hidden only after Play reports that its billing flow started.");
        Assert.DoesNotContain(
            "HideForPurchaseFlow();\n            var result = await store.LaunchPurchaseAsync(product)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobileUpsell_CompletionWatchRefreshesAuthoritativeEntitlement()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var watch = source.IndexOf(
            "private async Task WatchForPurchaseCompletionAsync",
            StringComparison.Ordinal);
        var stop = source.IndexOf(
            "private void StopPurchaseCompletionWatch",
            watch,
            StringComparison.Ordinal);

        Assert.True(watch >= 0, "The post-purchase entitlement watcher must remain explicit.");
        Assert.True(stop > watch, "The watcher must precede its cancellation helper.");
        var body = source.Substring(watch, stop - watch);
        Assert.Contains("RefreshLicenseStatusAsync", body);
    }

    [Fact]
    public void AndroidActivity_AttachesAnInitialLicenseRefreshAfterActivityIsReady()
    {
        var source = File.ReadAllText(ProjectFile("Noctra.Android", "MainActivity.cs"));
        var setCurrent = source.IndexOf("SetCurrent(this)", StringComparison.Ordinal);
        var bootstrap = source.IndexOf(
            "RunAdvertisingBootstrapSafelyAsync",
            setCurrent,
            StringComparison.Ordinal);

        Assert.True(setCurrent >= 0, "The Android activity provider must be attached in OnCreate.");
        Assert.True(bootstrap > setCurrent, "The initial license refresh must be queued after Activity attachment.");
        var attachedBody = source.Substring(setCurrent, bootstrap - setCurrent);
        Assert.Contains("QueueInitialLicenseRefresh", attachedBody);
    }

    [Fact]
    public void MobileUpsell_ShowClosesImmediatelyForExistingPremiumLicense()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var showStart = source.IndexOf("public void Show()", StringComparison.Ordinal);
        var tryCloseStart = source.IndexOf("public bool TryClose()", showStart, StringComparison.Ordinal);

        Assert.True(showStart >= 0, "The upsell Show method must remain explicit.");
        Assert.True(tryCloseStart > showStart,
            "The Show method must precede TryClose.");
        var showBody = source.Substring(showStart, tryCloseStart - showStart);
        Assert.Contains("if (_licenseService?.IsPremium == true)", showBody);
        Assert.Contains("TryClose();", showBody);
    }

    [Fact]
    public void MobileUpsell_ShowRefreshesStoreEntitlement()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var showStart = source.IndexOf("public void Show()", StringComparison.Ordinal);
        var tryCloseStart = source.IndexOf("public bool TryClose()", showStart, StringComparison.Ordinal);

        Assert.True(showStart >= 0, "The upsell Show method must remain explicit.");
        Assert.True(tryCloseStart > showStart, "The Show method must precede TryClose.");
        var showBody = source.Substring(showStart, tryCloseStart - showStart);
        Assert.Contains("RefreshLicenseStatusAsync", showBody);
    }

    [Fact]
    public void MobileUpsell_AlreadyOwnedRefreshesEntitlementBeforeShowingError()
    {
        var source = File.ReadAllText(ProjectFile(
            "Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var ownedBranch = source.IndexOf("if (result.AlreadyOwned)", StringComparison.Ordinal);
        var failureLog = source.IndexOf("Purchase failed:", ownedBranch, StringComparison.Ordinal);

        Assert.True(ownedBranch >= 0, "The already-owned branch must remain explicit.");
        Assert.True(failureLog > ownedBranch,
            "The already-owned branch must precede the generic failure path.");
        var ownedBody = source.Substring(ownedBranch, failureLog - ownedBranch);
        Assert.Contains("RefreshSubscriptionStatusAsync", ownedBody);
        Assert.Contains("TryClose();", ownedBody);
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
    public void PlayBundlePackaging_OffersOfflineNoRestoreMode()
    {
        var source = File.ReadAllText(ProjectFile("build", "package-play.ps1"));

        Assert.Contains("[switch]$NoRestore", source);
        Assert.Contains("$publishArgs += \"--no-restore\"", source);
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

    [Fact]
    public void BillingDeployScript_EnablesAndroidPublisherApi()
    {
        var source = File.ReadAllText(ProjectFile("deploy-billing.ps1"));

        Assert.Contains("androidpublisher.googleapis.com", source);
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
