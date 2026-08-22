namespace Noctra.Core.Advertising;

public interface IInterstitialAdHistoryStore
{
    IReadOnlyList<DateTimeOffset> Read();
    void Write(IReadOnlyList<DateTimeOffset> impressions);
}

/// <summary>
/// Process-wide policy state shared by every mobile advertising provider.
/// The pure policy remains deterministic while this coordinator owns the
/// persisted rolling impression history and fails closed if it becomes
/// unavailable.
/// </summary>
public sealed class InterstitialAdPolicyCoordinator
{
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromHours(24);
    private readonly IInterstitialAdHistoryStore _historyStore;
    private readonly object _sync = new();
    private List<DateTimeOffset>? _impressions;
    private bool _historyUnavailable;

    public InterstitialAdPolicyCoordinator(IInterstitialAdHistoryStore historyStore)
    {
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
    }

    public AdDecision Evaluate(
        InterstitialAdContext context,
        AdRuntimeEligibility runtime,
        InterstitialAdOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        lock (_sync)
        {
            if (!TryEnsureHistoryLoaded())
            {
                return AdDecision.Deny(AdDecisionReason.HistoryUnavailable);
            }

            PruneHistory(context.Now);
            return InterstitialAdPolicy.Evaluate(
                context,
                runtime,
                options,
                new InterstitialAdHistory(_impressions!.ToArray()));
        }
    }

    public void RecordImpression(DateTimeOffset timestamp)
    {
        lock (_sync)
        {
            if (!TryEnsureHistoryLoaded())
            {
                return;
            }

            PruneHistory(timestamp);
            _impressions!.Add(timestamp);
            _impressions.Sort();

            try
            {
                _historyStore.Write(_impressions.ToArray());
            }
            catch
            {
                // The already-visible ad cannot be undone. Deny later ads in
                // this process so a persistence failure cannot bypass caps.
                _historyUnavailable = true;
            }
        }
    }

    private bool TryEnsureHistoryLoaded()
    {
        if (_historyUnavailable)
        {
            return false;
        }

        if (_impressions is not null)
        {
            return true;
        }

        try
        {
            _impressions = _historyStore.Read()
                .Distinct()
                .OrderBy(timestamp => timestamp)
                .ToList();
            return true;
        }
        catch
        {
            _historyUnavailable = true;
            return false;
        }
    }

    private void PruneHistory(DateTimeOffset now)
    {
        _impressions!.RemoveAll(timestamp =>
            timestamp > now ||
            now - timestamp >= RetentionWindow);
    }
}
