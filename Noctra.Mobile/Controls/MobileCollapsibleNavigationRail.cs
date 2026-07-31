using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    private const string RuntimeLayoutName = "NavigationRailRuntimeLayout";

    private readonly Border _navigationRail;
    private readonly List<NavigationItemVisualState> _items = new();
    private Button? _toggleButton;
    private MaterialIcon? _toggleIcon;
    private int _stateGeneration;

    public MobileCollapsibleNavigationRail(
        Border navigationRail,
        bool isExpanded = true)
    {
        _navigationRail = navigationRail ??
            throw new ArgumentNullException(nameof(navigationRail));
        IsExpanded = isExpanded;
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

        var generation = ++_stateGeneration;
        var expanding = IsExpanded;

        // Use CSS class for compact mode instead of hardcoded Width/Padding.
        // This preserves the DynamicResource NavRailWidth when expanded.
        _navigationRail.Classes.Set("compact", !expanding);

        foreach (var item in _items)
        {
            if (expanding)
            {
                // Expanded layout applies immediately; labels fade in after a
                // beat so their first paint happens while invisible.
                item.ContentPanel.Spacing = item.ExpandedSpacing;
                item.ContentPanel.Margin = item.ExpandedMargin;
                item.ContentPanel.HorizontalAlignment = item.ExpandedHorizontalAlignment;
                item.Label.IsVisible = true;
                item.Label.Opacity = 0;
                ToolTip.SetTip(item.Button, null);
            }
            else
            {
                // Fade labels out, then drop them from layout once invisible.
                item.Label.Opacity = 0;
            }
        }

        UpdateToggleIcon();

        _ = expanding
            ? FadeLabelsInAsync(generation)
            : FinalizeCollapseAsync(generation);
    }

    private async Task FadeLabelsInAsync(int generation)
    {
        await Task.Delay(30);
        if (generation != _stateGeneration)
        {
            return;
        }

        foreach (var item in _items)
        {
            if (item.Label.IsVisible)
            {
                item.Label.Opacity = 1;
            }
        }
    }

    private async Task FinalizeCollapseAsync(int generation)
    {
        await Task.Delay(170);
        if (generation != _stateGeneration)
        {
            return;
        }

        foreach (var item in _items)
        {
            item.Label.IsVisible = false;
            item.Label.Opacity = 1;
            item.ContentPanel.Spacing = 0;
            item.ContentPanel.Margin = default;
            item.ContentPanel.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(item.Button, item.ToolTipText);
        }
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

            label.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(140)
                }
            };

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
