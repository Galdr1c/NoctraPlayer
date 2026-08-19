using System;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Mobile.Services;

/// <summary>
/// Startup pipeline for advertising, independent of any ad provider:
/// entitlement → consent/provider initialization.
///
/// Runs fire-and-forget from startup (never on the UI thread). Fail-closed:
/// a missing provider (NoOp) simply exits. Stage failures are caught here so
/// advertising problems can never break application startup.
/// </summary>
public sealed class MobileAdvertisingBootstrapper
{
    private readonly IMobileAdvertisingService _advertising;

    public MobileAdvertisingBootstrapper(IMobileAdvertisingService advertising)
    {
        _advertising = advertising ?? throw new ArgumentNullException(nameof(advertising));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Stage 1 — entitlement: premium users never see ads, so skip the rest.
            // NOTE: gate on IsAdsEligible, NOT CanServeAds. CanServeAds implies UMP
            // consent + SDK initialization, and UMP's CanRequestAds is false until
            // requestConsentInfoUpdate() has run — gating initialization on it would
            // deadlock the consent flow.
            if (!_advertising.IsAdsEligible)
            {
                return;
            }

            // Stage 2 — consent + provider initialization. The production Android
            // provider runs UMP/consent and Mobile Ads SDK initialization here.
            await _advertising.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MobileAdvertisingBootstrapper] stage failed: {ex.Message}");
        }
    }
}