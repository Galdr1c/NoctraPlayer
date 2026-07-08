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
    private Func<bool>? _surfaceReadyCheck;

    /// <summary>
    /// True when MainView has registered its Avalonia overlay handler.
    /// </summary>
    public bool HasHandler => _handler is not null;

    /// <summary>
    /// Optional callback that checks whether the current UI surface is
    /// suitable for showing a review prompt (player not visible, no import
    /// in progress, no profile/legal overlay, etc.).
    /// Called right before the prompt is displayed, not just at schedule time.
    /// </summary>
    public bool IsSurfaceReady => _surfaceReadyCheck?.Invoke() ?? true;

    /// <summary>
    /// Register the Avalonia overlay handler that shows the in-app
    /// review prompt bottom sheet and returns the user's choice.
    /// </summary>
    public void Register(Func<CancellationToken, Task<ReviewPromptResult>> handler)
        => _handler = handler;

    /// <summary>
    /// Register a surface-state check that runs right before the prompt
    /// is displayed. Return false to suppress the prompt.
    /// </summary>
    public void RegisterSurfaceCheck(Func<bool> check)
        => _surfaceReadyCheck = check;

    /// <summary>
    /// Unregister all handlers (e.g., during teardown).
    /// </summary>
    public void Unregister()
    {
        _handler = null;
        _surfaceReadyCheck = null;
    }

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
