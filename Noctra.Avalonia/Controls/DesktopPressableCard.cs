using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Noctra.Avalonia.Controls;

/// <summary>
/// Button-like card surface that keeps ScrollViewer gestures intact.
/// A primary command is executed only after a valid release; pointer movement,
/// leaving the card, a nested button, or command invalidation cancels the click.
/// </summary>
public sealed class DesktopPressableCard : Border
{
    private const double ClickCancellationDistance = 12d;

    private IPointer? _activePointer;
    private Point _pressOrigin;
    private bool _keyboardPressActive;
    private ICommand? _subscribedCommand;

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
        Focusable = true;
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Button);

        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        LostFocus += OnLostFocus;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateCommandSubscription(Command);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CommandProperty)
        {
            UpdateCommandSubscription(change.GetNewValue<ICommand?>());
        }
        else if (change.Property == CommandParameterProperty)
        {
            UpdateCanExecuteState();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || OriginatesFromNestedButton(e.Source) || !CanExecuteCommand())
            return;

        if (e.Key == Key.Enter)
        {
            ExecuteCommand();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && !_keyboardPressActive)
        {
            _keyboardPressActive = true;
            PseudoClasses.Set(":pressed", true);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key != Key.Space || !_keyboardPressActive)
            return;

        _keyboardPressActive = false;
        PseudoClasses.Set(":pressed", false);
        ExecuteCommand();
        e.Handled = true;
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        ResetPressedState();
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

        if (!point.Properties.IsLeftButtonPressed ||
            OriginatesFromNestedButton(e.Source) ||
            !CanExecuteCommand())
        {
            return;
        }

        _activePointer = e.Pointer;
        _pressOrigin = point.Position;
        PseudoClasses.Set(":pressed", true);

        // Do not capture or mark handled. Parent ScrollViewer must remain able to
        // claim a horizontal/vertical drag and cancel this pending click.
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _activePointer))
            return;

        var current = e.GetPosition(this);
        var deltaX = current.X - _pressOrigin.X;
        var deltaY = current.Y - _pressOrigin.Y;
        if ((deltaX * deltaX) + (deltaY * deltaY) >=
            ClickCancellationDistance * ClickCancellationDistance)
        {
            ResetPressedState();
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _activePointer))
            return;

        var isInside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        ResetPressedState();

        if (!isInside || OriginatesFromNestedButton(e.Source))
            return;

        Focus(NavigationMethod.Pointer);
        ExecuteCommand();
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ResetPressedState();

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        UpdateCommandSubscription(Command);

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ResetPressedState();
        UpdateCommandSubscription(null);
    }

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

    private bool CanExecuteCommand() =>
        Command?.CanExecute(CommandParameter) == true;

    private void ExecuteCommand()
    {
        var command = Command;
        var parameter = CommandParameter;
        if (command?.CanExecute(parameter) == true)
            command.Execute(parameter);
    }

    private void UpdateCommandSubscription(ICommand? command)
    {
        if (ReferenceEquals(_subscribedCommand, command))
        {
            UpdateCanExecuteState();
            return;
        }

        if (_subscribedCommand is not null)
            _subscribedCommand.CanExecuteChanged -= OnCommandCanExecuteChanged;

        _subscribedCommand = command;

        if (_subscribedCommand is not null)
            _subscribedCommand.CanExecuteChanged += OnCommandCanExecuteChanged;

        UpdateCanExecuteState();
    }

    private void OnCommandCanExecuteChanged(object? sender, EventArgs e) =>
        UpdateCanExecuteState();

    private void UpdateCanExecuteState()
    {
        var canExecute = CanExecuteCommand();
        PseudoClasses.Set(":command-disabled", !canExecute);
        if (!canExecute)
            ResetPressedState();
    }

    private void ResetPressedState()
    {
        _activePointer = null;
        _keyboardPressActive = false;
        PseudoClasses.Set(":pressed", false);
    }
}
