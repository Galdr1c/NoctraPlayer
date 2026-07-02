using Noctra.Models;

namespace Noctra.Services;

/// <summary>
/// Shared mediator that allows the platform-specific review prompt service
/// (e.g., AndroidReviewPromptService) to delegate fallback UI to the
/// Avalonia layer (MainView) without cross-project references.
///
/// Registration flow:
///   1. Core DI registers this as a singleton.
///   2. MainView sets <see cref="Register"/> with its overlay handler.
///   3. AndroidReviewPromptService checks <see cref="HasHandler"/> and
///      calls <see cref="ShowAsync"/> when the Play Review API fails.
/// </summary>
public sealed class ReviewPromptFallbackHandler
{
    private Func<CancellationToken, Task<ReviewPromptResult>>? _handler;

    /// <summary>
    /// True when MainView has registered its Avalonia overlay handler.
    /// </summary>
    public bool HasHandler => _handler is not null;

    /// <summary>
    /// Register the Avalonia overlay handler that shows the in-app
    /// review prompt bottom sheet and returns the user's choice.
    /// </summary>
    public void Register(Func<CancellationToken, Task<ReviewPromptResult>> handler)
        => _handler = handler;

    /// <summary>
    /// Unregister the handler (e.g., during teardown).
    /// </summary>
    public void Unregister()
        => _handler = null;

    /// <summary>
    /// Show the Avalonia fallback overlay and return the user's result.
    /// If no handler is registered, returns <see cref="ReviewPromptResult.Later"/>.
    /// </summary>
    public async Task<ReviewPromptResult> ShowAsync(CancellationToken cancellationToken = default)
    {
        if (_handler is null)
        {
            return ReviewPromptResult.Later;
        }

        return await _handler(cancellationToken);
    }
}
