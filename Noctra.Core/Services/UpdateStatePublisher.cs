using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Creates and publishes consistent update-state event payloads for platform services.
/// </summary>
public sealed class UpdateStatePublisher
{
    public event EventHandler<UpdateStateChangedEventArgs>? UpdateStateChanged;

    public void Publish(
        object sender,
        UpdateCheckStatus status,
        double? progressPercent = null,
        string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(sender);

        UpdateStateChangedEventArgs args;
        if (progressPercent.HasValue)
        {
            args = new UpdateStateChangedEventArgs(status, progressPercent.Value);
        }
        else if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            args = new UpdateStateChangedEventArgs(status, errorMessage);
        }
        else
        {
            args = new UpdateStateChangedEventArgs(status);
        }

        UpdateStateChanged?.Invoke(sender, args);
    }
}
