using Avalonia;
using Avalonia.Controls;

namespace Noctra.Mobile.Behaviors;

/// <summary>
/// Configures the mobile stretch-overscroll behavior for a visual subtree.
/// The value is inherited, so a page or individual ScrollViewer can
/// disable the effect without changing the global controller.
/// </summary>
public static class MobileOverscroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled",
            typeof(MobileOverscroll),
            defaultValue: true,
            inherits: true);

    public static bool GetIsEnabled(Control control)
        => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value)
        => control.SetValue(IsEnabledProperty, value);
}
