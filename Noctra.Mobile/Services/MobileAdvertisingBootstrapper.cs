using System;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Core.Advertising;

namespace Noctra.Mobile.Services;

/// <summary>
/// Startup pipeline for advertising, independent of any ad provider:
/// remote configuration → entitlement → consent/provider initialization.
///
/// Runs fire-and-forget from startup (never on the UI thread). Fail-closed:
/// a config fetch failure keeps ConservativeDefault and a missing provider
/// (NoOp) simply exits after the config stage. Stage failures are caught here
/// so advertising problems can never break application startup.
/// </summary>
public sealed class MobileAdvertisingBootstrapper
{
    private readonly IMobileAdvertisingService _advertising;
    private readonly IRemoteAdvertisingConfigService _remoteConfig;

    public MobileAdvertisingBootstrapper(
        IMobileAdvertisingService advertising,
        IRemoteAdvertisingConfigService remoteConfig)
    {
        _advertising = advertising ?? throw new ArgumentNullException(nameof(advertising));
        _remoteConfig = remoteConfig ?? throw new ArgumentNullException(nameof(remoteConfig));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Stage 1 — remote config: fetched unconditionally so the latest placement
            // rules are already in place when entitlement flips to ads (e.g. a
            // subscription expires while the app is running). RefreshAsync is
            // fail-closed: any fetch/parse failure keeps ConservativeDefault.
            await _remoteConfig.RefreshAsync(cancellationToken).ConfigureAwait(false);

            // Stage 2 — entitlement: premium users never see ads, so skip the rest.
            if (!_advertising.CanServeAds)
            {
                return;
            }

            // Stage 3 — consent + provider initialization. The production Android
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