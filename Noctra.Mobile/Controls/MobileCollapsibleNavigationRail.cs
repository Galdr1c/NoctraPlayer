using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Noctra.Mobile.Controls;

/// <summary>
/// Adds an icon-only compact mode to the tablet/landscape navigation rail
/// without duplicating the navigation items that are declared in XAML.
/// </summary>
internal sealed class MobileCollapsibleNavigationRail
{
    private const double CompactWidth = 56;
    private const double FallbackExpandedWidth = 180;
    private const string RuntimeLayoutName = "NavigationRailRuntimeLayout";

    private readonly Border _navigationRail;
    private readonly List<NavigationItemVisualState> _items = new();
    private readonly double _expandedWidth;
    private readonly Thickness _expandedPadding;
    private Button? _toggleButton;
    private MaterialIcon? _toggleIcon;

    public MobileCollapsibleNavigationRail(
        Border navigationRail,
        bool isExpanded = true)
    {
        _navigationRail = navigationRail ??
            throw new ArgumentNullException(nameof(navigationRail));
        IsExpanded = isExpanded;
        _expandedWidth = double.IsFinite(navigationRail.Width) &&
            navigationRail.Width > CompactWidth + 20
                ? navigationRail.Width
                : FallbackExpandedWidth;
        _expandedPadding = navigationRail.Padding;

        Initialize();
    }

    public bool IsExpanded { get; private set; }

    public bool IsAttachedTo(Border navigationRail)
        => ReferenceEquals(_navigationRail, navigationRail);

    public void ApplyCurrentState()
    {
        if (_toggleButton is null)
        {
            return;
        }

        _navigationRail.Width = IsExpanded
            ? _expandedWidth
            : CompactWidth;
        _navigationRail.Padding = IsExpanded
            ? _expandedPadding
            : new Thickness(
                8,
                _expandedPadding.Top,
                8,
                _expandedPadding.Bottom);

        foreach (var item in _items)
        {
            item.Label.IsVisible = IsExpanded;
            item.ContentPanel.Spacing = IsExpanded
                ? item.ExpandedSpacing
                : 0;
            item.ContentPanel.Margin = IsExpanded
                ? item.ExpandedMargin
                : default;
            item.ContentPanel.HorizontalAlignment = IsExpanded
                ? item.ExpandedHorizontalAlignment
                : HorizontalAlignment.Center;

            ToolTip.SetTip(
                item.Button,
                IsExpanded ? null : item.ToolTipText);
        }

        UpdateToggleIcon();
    }

    private void Initialize()
    {
        // A controller is only created once per NavigationRail instance. This
        // guard keeps an unexpected re-entry from wrapping the rail twice.
        if (_navigationRail.Child is DockPanel { Name: RuntimeLayoutName })
        {
            return;
        }

        if (_navigationRail.Child is not StackPanel navigationItems)
        {
            return;
        }

        CaptureNavigationItems(navigationItems);

        _toggleIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.Menu,
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform(0),
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = RotateTransform.AngleProperty,
                    Duration = TimeSpan.FromMilliseconds(250)
                }
            }
        };

        _toggleButton = new Button
        {
            Name = "NavigationRailToggleButton",
            Content = _toggleIcon,
            MinHeight = 52,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        _toggleButton.Classes.Add("navRail");
        _toggleButton.Click += OnToggleClick;
        ToolTip.SetTip(_toggleButton, "Menu");

        _navigationRail.Child = null;

        var itemsScrollViewer = new ScrollViewer
        {
            Name = "NavigationRailItemsScrollViewer",
            Content = navigationItems,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            IsScrollChainingEnabled = true,
            IsScrollInertiaEnabled = true
        };

        var layout = new DockPanel
        {
            Name = RuntimeLayoutName,
            LastChildFill = true
        };
        DockPanel.SetDock(_toggleButton, Dock.Top);
        layout.Children.Add(_toggleButton);
        layout.Children.Add(itemsScrollViewer);
        _navigationRail.Child = layout;

        ApplyCurrentState();
    }

    private void CaptureNavigationItems(StackPanel navigationItems)
    {
        foreach (var button in navigationItems.Children.OfType<Button>())
        {
            if (button.Content is not DockPanel dockPanel)
            {
                continue;
            }

            var contentPanel = dockPanel.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal);
            var label = contentPanel?.Children
                .OfType<TextBlock>()
                .LastOrDefault();

            if (contentPanel is null || label is null)
            {
                continue;
            }

            var toolTipText = !string.IsNullOrWhiteSpace(label.Text)
                ? label.Text
                : button.Tag?.ToString();

            _items.Add(
                new NavigationItemVisualState(
                    button,
                    contentPanel,
                    label,
                    toolTipText));
        }
    }

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
        ApplyCurrentState();
    }

    private void UpdateToggleIcon()
    {
        if (_toggleIcon is null)
        {
            return;
        }

        // Rotate the icon 180° when collapsed to indicate direction.
        if (_toggleIcon.RenderTransform is RotateTransform rotate)
        {
            rotate.Angle = IsExpanded ? 0 : 180;
        }

        _toggleIcon.Kind = IsExpanded
            ? MaterialIconKind.Menu
            : MaterialIconKind.MenuOpen;

        ToolTip.SetTip(
            _toggleButton,
            IsExpanded ? null : "Menu");
    }

    private sealed class NavigationItemVisualState
    {
        public NavigationItemVisualState(
            Button button,
            StackPanel contentPanel,
            TextBlock label,
            string? toolTipText)
        {
            Button = button;
            ContentPanel = contentPanel;
            Label = label;
            ToolTipText = toolTipText;
            ExpandedSpacing = contentPanel.Spacing;
            ExpandedMargin = contentPanel.Margin;
            ExpandedHorizontalAlignment = contentPanel.HorizontalAlignment;
        }

        public Button Button { get; }

        public StackPanel ContentPanel { get; }

        public TextBlock Label { get; }

        public string? ToolTipText { get; }

        public double ExpandedSpacing { get; }

        public Thickness ExpandedMargin { get; }

        public HorizontalAlignment ExpandedHorizontalAlignment { get; }
    }
}
