using System;
using System.Threading;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Noctra.Mobile.Controls;

/// <summary>
/// Native Android ad views have an airspace/z-order boundary. Any Avalonia sheet
/// that covers feed content acquires a suppression lease so a future NativeControlHost
/// provider cannot punch through the overlay.
/// </summary>
public static class MobileNativeAdSurfaceCoordinator
{
    private static int _suppressionCount;

    public static bool IsSuppressed => Volatile.Read(ref _suppressionCount) > 0;
    public static event EventHandler? Changed;

    public static IDisposable Suppress()
    {
        Interlocked.Increment(ref _suppressionCount);
        Changed?.Invoke(null, EventArgs.Empty);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Interlocked.Decrement(ref _suppressionCount);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}

/// <summary>
/// Attached behavior used from XAML on bottom sheets/full-screen overlays.
/// </summary>
public sealed class MobileNativeAdOverlayGuard : AvaloniaObject
{
    private MobileNativeAdOverlayGuard() { }
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<MobileNativeAdOverlayGuard, Control, bool>(
            "IsEnabled");

    private static readonly ConditionalWeakTable<Control, GuardState> States = new();

    static MobileNativeAdOverlayGuard()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (control.GetValue(IsEnabledProperty))
            {
                if (!States.TryGetValue(control, out var state))
                {
                    state = new GuardState(control);
                    States.Add(control, state);
                }
                state.Attach();
                return;
            }

            if (States.TryGetValue(control, out var existing))
            {
                existing.Dispose();
                States.Remove(control);
            }
        });
    }

    public static void SetIsEnabled(AvaloniaObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(AvaloniaObject element)
        => element.GetValue(IsEnabledProperty);

    private sealed class GuardState : IDisposable
    {
        private readonly Control _control;
        private IDisposable? _lease;
        private bool _attached;

        public GuardState(Control control)
        {
            _control = control;
        }

        public void Attach()
        {
            if (_attached)
                return;

            _control.PropertyChanged += Control_PropertyChanged;
            _control.AttachedToVisualTree += Control_AttachedToVisualTree;
            _control.DetachedFromVisualTree += Control_DetachedFromVisualTree;
            _attached = true;
            Update();
        }

        public void Dispose()
        {
            if (_attached)
            {
                _control.PropertyChanged -= Control_PropertyChanged;
                _control.AttachedToVisualTree -= Control_AttachedToVisualTree;
                _control.DetachedFromVisualTree -= Control_DetachedFromVisualTree;
                _attached = false;
            }

            Release();
        }

        private void Control_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.IsVisibleProperty)
            {
                Update();
            }
        }

        private void Control_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
            => Update();

        private void Control_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
            => Release();

        private void Update()
        {
            if (TopLevel.GetTopLevel(_control) is not null && _control.IsVisible)
            {
                _lease ??= MobileNativeAdSurfaceCoordinator.Suppress();
            }
            else
            {
                Release();
            }
        }

        private void Release()
        {
            _lease?.Dispose();
            _lease = null;
        }
    }
}
