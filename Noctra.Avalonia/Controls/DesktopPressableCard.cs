using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Noctra.Avalonia.Controls;

public sealed class DesktopPressableCard : Border
{
    private IPointer? _activePointer;

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<DesktopPressableCard, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<DesktopPressableCard, object?>(nameof(CommandParameter));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public static readonly RoutedEvent<RoutedEventArgs> CardRightTappedEvent =
        RoutedEvent.Register<DesktopPressableCard, RoutedEventArgs>(
            "CardRightTapped", RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs>? CardRightTapped
    {
        add => AddHandler(CardRightTappedEvent, value);
        remove => RemoveHandler(CardRightTappedEvent, value);
    }

    public DesktopPressableCard()
    {
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        DetachedFromVisualTree += (_, _) => ResetPressedState();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        PseudoClasses.Set(":pointerover", true);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        PseudoClasses.Set(":pointerover", false);
        if (ReferenceEquals(e.Pointer, _activePointer))
            ResetPressedState();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_activePointer is not null)
            return;

        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed)
        {
            RaiseEvent(new RoutedEventArgs(CardRightTappedEvent));
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || OriginatesFromNestedButton(e.Source))
            return;

        _activePointer = e.Pointer;
        PseudoClasses.Set(":pressed", true);

        var command = Command;
        if (command is not null && command.CanExecute(CommandParameter))
            command.Execute(CommandParameter);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _activePointer))
            ResetPressedState();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ResetPressedState();

    private bool OriginatesFromNestedButton(object? source)
    {
        for (var visual = source as Visual;
             visual is not null && !ReferenceEquals(visual, this);
             visual = visual.GetVisualParent())
        {
            if (visual is Button)
                return true;
        }
        return false;
    }

    private void ResetPressedState()
    {
        _activePointer = null;
        PseudoClasses.Set(":pressed", false);
    }
}
