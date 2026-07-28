namespace Noctra.ViewModels;

internal enum PlayerGestureIntent
{
    Pending,
    Rejected,
    Vertical
}

internal static class PlayerGesturePolicy
{
    internal static PlayerGestureIntent Classify(
        double deltaX,
        double deltaY,
        double activationThreshold,
        double verticalIntentRatio)
    {
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance < activationThreshold)
        {
            return PlayerGestureIntent.Pending;
        }

        return Math.Abs(deltaY) >= Math.Abs(deltaX) * verticalIntentRatio
            ? PlayerGestureIntent.Vertical
            : PlayerGestureIntent.Rejected;
    }
}
