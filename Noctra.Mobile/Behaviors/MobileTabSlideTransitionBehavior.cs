using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Noctra.Mobile.Behaviors;

public static class MobileTabSlideTransitionBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TabControl, bool>(
            "IsEnabled",
            typeof(MobileTabSlideTransitionBehavior));

    private static readonly AttachedProperty<int> PreviousIndexProperty =
        AvaloniaProperty.RegisterAttached<TabControl, int>(
            "_previousIndex",
            typeof(MobileTabSlideTransitionBehavior),
            defaultValue: -1);

    private static readonly AttachedProperty<CancellationTokenSource?> CancellationProperty =
        AvaloniaProperty.RegisterAttached<TabControl, CancellationTokenSource?>(
            "_cancellation",
            typeof(MobileTabSlideTransitionBehavior));

    static MobileTabSlideTransitionBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<TabControl>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(TabControl element)
        => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TabControl element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(TabControl tabControl, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            tabControl.SelectionChanged += OnSelectionChanged;
        }
        else
        {
            tabControl.SelectionChanged -= OnSelectionChanged;
        }
    }

    private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabControl || !ReferenceEquals(e.Source, tabControl))
        {
            return;
        }

        var newIndex = tabControl.SelectedIndex;
        var oldIndex = tabControl.GetValue(PreviousIndexProperty);
        tabControl.SetValue(PreviousIndexProperty, newIndex);

        if (oldIndex == -1)
        {
            return;
        }

        var target = FindSelectedContentHost(tabControl);
        if (target is null)
        {
            return;
        }

        var previous = tabControl.GetValue(CancellationProperty);
        previous?.Cancel();
        previous?.Dispose();

        var cts = new CancellationTokenSource();
        tabControl.SetValue(CancellationProperty, cts);

        var startX = newIndex > oldIndex ? 32.0 : -32.0;
        target.RenderTransform = new TranslateTransform { X = startX };
        target.Opacity = 0;

        var animation = CreateSlideAnimation(startX);
        _ = RunAsync(animation, target, cts.Token);
    }

    private static Control? FindSelectedContentHost(TabControl tabControl)
    {
        foreach (var child in tabControl.GetVisualDescendants())
        {
            if (child is ContentPresenter { Name: "PART_SelectedContentHost" } contentPresenter)
            {
                return contentPresenter;
            }

            if (child is Carousel carousel)
            {
                return carousel;
            }
        }

        return null;
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
        Control target,
        CancellationToken cancellationToken)
    {
        try
        {
            await animation.RunAsync(target, cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                target.Opacity = 1.0;
                target.RenderTransform = null;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
