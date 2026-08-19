using System;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Mobile.Services;

/// <summary>
/// Startup ordering gate between Noctra's own legal consent screen and the UMP
/// consent form. The production Android ad provider awaits
/// <see cref="WaitForLegalConsentAsync"/> before requesting UMP consent info;
/// MainView signals completion right after its legal consent flow finishes, so
/// the two modals can never stack or race each other at startup.
/// </summary>
public sealed class StartupPrivacyCoordinator
{
    private static readonly TimeSpan LegalConsentTimeout = TimeSpan.FromSeconds(90);

    private readonly object _gate = new();
    private TaskCompletionSource<bool> _legalConsentCompleted = CreateGate();

    private static TaskCompletionSource<bool> CreateGate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void MarkLegalConsentFlowCompleted()
    {
        lock (_gate)
        {
            _legalConsentCompleted.TrySetResult(true);
        }
    }

    /// <summary>
    /// Completes once Noctra's own consent screen has been shown/skipped. The
    /// timeout is only a safety valve for abnormal startup paths — a consent
    /// dialog stuck open must never block ad initialization forever.
    /// </summary>
    public Task WaitForLegalConsentAsync(CancellationToken cancellationToken = default)
    {
        Task gate;
        lock (_gate)
        {
            gate = _legalConsentCompleted.Task;
        }

        return gate.WaitAsync(LegalConsentTimeout, cancellationToken);
    }
}