namespace Noctra.ViewModels;

/// <summary>
/// Describes the visual kind of a form status message so that the UI can
/// pick the appropriate icon and colour without hard-coding ErrorBrush for
/// every state.
/// </summary>
public enum FormStatusKind
{
    /// <summary>No message to show.</summary>
    None,

    /// <summary>An operation is in progress (spinner, neutral text).</summary>
    Progress,

    /// <summary>The operation completed successfully (green check).</summary>
    Success,

    /// <summary>A recoverable warning the user should be aware of (amber icon).</summary>
    Warning,

    /// <summary>An error that blocks the user from proceeding (red icon).</summary>
    Error,
}
