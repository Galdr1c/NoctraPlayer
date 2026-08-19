using System;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Mobile.Services;

/// <summary>
/// Startup pipeline for advertising, independent of any ad provider:
/// consent/privacy refresh → provider initialization. Ad creation itself is
/// gated by entitlement inside the provider.
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
            // Consent/privacy lifecycle is independent of ad entitlement: UMP
            // consent info must be refreshed on every launch (Google
            // requirement) and privacy-options requirements apply to premium
            // users too. The provider decides whether ads are actually created
            // (free entitlement + consent granted).
            await _advertising.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MobileAdvertisingBootstrapper] stage failed: {ex.Message}");
        }
    }
}