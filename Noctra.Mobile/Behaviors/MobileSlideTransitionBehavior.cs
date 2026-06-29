using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noctra.Mobile.Behaviors;

public sealed class MobileSlideTransitionBehavior : AvaloniaObject
{
    private static readonly string[] NavigationOrder =
    [
        "Home",
        "Live",
        "Movies",
        "Series",
        "Search",
        "Favorites",
        "MyList",
        "History",
        "Downloads",
        "Settings",
        "More"
    ];

    public static readonly AttachedProperty<object?> TriggerValueProperty =
        AvaloniaProperty.RegisterAttached<MobileSlideTransitionBehavior, Control, object?>("TriggerValue");

    private static readonly AttachedProperty<CancellationTokenSource?> CancellationProperty =
        AvaloniaProperty.RegisterAttached<MobileSlideTransitionBehavior, Control, CancellationTokenSource?>("_cancellation");

    static MobileSlideTransitionBehavior()
    {
        TriggerValueProperty.Changed.AddClassHandler<Control>(OnTriggerValueChanged);
    }

    public static object? GetTriggerValue(Control element)
        => element.GetValue(TriggerValueProperty);

    public static void SetTriggerValue(Control element, object? value)
        => element.SetValue(TriggerValueProperty, value);

    private static void OnTriggerValueChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (Equals(e.OldValue, e.NewValue))
        {
            return;
        }

        if (e.NewValue is null)
        {
            Reset(control);
            return;
        }

        var previous = control.GetValue(CancellationProperty);
        previous?.Cancel();
        previous?.Dispose();

        var cts = new CancellationTokenSource();
        control.SetValue(CancellationProperty, cts);

        var oldOrder = GetTransitionOrder(e.OldValue);
        var newOrder = GetTransitionOrder(e.NewValue);
        var distance = e.OldValue is null ? 12.0 : 32.0;
        var startX = e.OldValue is null || newOrder >= oldOrder ? distance : -distance;

        control.RenderTransform = new TranslateTransform { X = startX };
        control.Opacity = 0;

        var animation = CreateSlideAnimation(startX);
        _ = RunAsync(animation, control, cts.Token);
    }

    private static int GetTransitionOrder(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is string destination)
        {
            var index = Array.IndexOf(NavigationOrder, destination);
            return index >= 0 ? index : 0;
        }

        var property = value.GetType().GetProperty("SeasonNumber");
        return property?.PropertyType == typeof(int)
            ? (int)(property.GetValue(value) ?? 0)
            : 0;
    }

    private static Animation CreateSlideAnimation(double startX)
        => new()
        {
            Duration = TimeSpan.FromMilliseconds(320),
            Easing = new SplineEasing(0.25, 1.0, 0.3, 1.0),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.0d),
                        new Setter(TranslateTransform.XProperty, startX)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.35d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1.0d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1.0d),
                        new Setter(TranslateTransform.XProperty, 0.0d)
                    }
                }
            }
        };

    private static async System.Threading.Tasks.Task RunAsync(
        Animation animation,
        Control control,
        CancellationToken cancellationToken)
    {
        try
        {
            await animation.RunAsync(control, cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                Reset(control);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void Reset(Control control)
    {
        control.Opacity = 1.0;
        control.RenderTransform = null;
    }
}
