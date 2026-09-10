using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using Noctra.Avalonia.Localization;
using Noctra.Avalonia.Views;
using Noctra.ViewModels;

namespace Noctra.Avalonia;

public partial class MainWindow
{
    private bool _mobileFirstDesktopShellInitialized;
    private Button? _desktopSearchNavButton;
    private Button? _desktopSettingsNavButton;
    private TextBlock? _desktopSearchNavText;
    private TextBlock? _desktopSettingsNavText;

    protected override void OnOpened(EventArgs e)
    {
        ApplyMobileFirstDesktopShell();
        base.OnOpened(e);
    }

    private void ApplyMobileFirstDesktopShell()
    {
        if (_mobileFirstDesktopShellInitialized)
            return;

        _mobileFirstDesktopShellInitialized = true;

        CollapseLegacyDesktopHeader();
        AddMobileFirstRailActions();
        EnableAdaptiveRailScrolling();

        _mainViewModel.PropertyChanged += MobileFirstShellViewModel_PropertyChanged;
        SideBar.PropertyChanged += MobileFirstShellSidebar_PropertyChanged;
        Closed += MobileFirstShell_Closed;

        SyncMobileFirstRailState();
    }

    private void CollapseLegacyDesktopHeader()
    {
        // Mobile is the shell source of truth: Search and Settings live in navigation,
        // not in a desktop-only top header.
        HeaderSearchBox.IsEnabled = false;
        HeaderSearchBox.IsVisible = false;
        HeaderSearchBox.Focusable = false;
        ClearSearchButton.IsEnabled = false;

        HeaderBar.Child = null;
        HeaderBar.Height = 0;
        HeaderBar.MinHeight = 0;
        HeaderBar.Padding = new Thickness(0);
        HeaderBar.Margin = new Thickness(0);
        HeaderBar.Opacity = 0;
        HeaderBar.Focusable = false;
        HeaderBar.IsEnabled = false;
        HeaderBar.IsHitTestVisible = false;
        HeaderBar.ClipToBounds = true;

        if (HeaderBar.Parent is Grid rootGrid && rootGrid.RowDefinitions.Count > 0)
            rootGrid.RowDefinitions[0].Height = new GridLength(0);
    }

    private void AddMobileFirstRailActions()
    {
        if (SideBar.Child is not StackPanel rail)
            return;

        _desktopSearchNavButton = CreateRailButton(
            MaterialIconKind.Magnify,
            LocalizationSource.Instance["Shell.Search.Tooltip"],
            out _desktopSearchNavText);
        _desktopSearchNavButton.Click += DesktopSearchNav_Click;

        var seriesIndex = rail.Children.IndexOf(NavSeriesBtn);
        rail.Children.Insert(seriesIndex >= 0 ? seriesIndex + 1 : rail.Children.Count, _desktopSearchNavButton);

        // Match the canonical mobile rail order: Favorites before My List.
        var favoritesIndex = rail.Children.IndexOf(NavFavBtn);
        var myListIndex = rail.Children.IndexOf(NavMyListBtn);
        if (favoritesIndex > myListIndex && myListIndex >= 0)
        {
            rail.Children.Remove(NavFavBtn);
            myListIndex = rail.Children.IndexOf(NavMyListBtn);
            rail.Children.Insert(myListIndex, NavFavBtn);
        }

        _desktopSettingsNavButton = CreateRailButton(
            MaterialIconKind.CogOutline,
            LocalizationSource.Instance["Settings.Title"],
            out _desktopSettingsNavText);
        _desktopSettingsNavButton.Click += DesktopSettingsNav_Click;

        var downloadsIndex = rail.Children.IndexOf(NavDownloadsBtn);
        var settingsIndex = downloadsIndex >= 0 ? downloadsIndex + 1 : rail.Children.Count;
        rail.Children.Insert(settingsIndex, _desktopSettingsNavButton);
    }

    private void EnableAdaptiveRailScrolling()
    {
        if (SideBar.Child is not StackPanel rail)
            return;

        SideBar.Child = null;
        SideBar.Child = new ScrollViewer
        {
            Content = rail,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
    }

    private static Button CreateRailButton(
        MaterialIconKind iconKind,
        string label,
        out TextBlock labelText)
    {
        var activeIndicator = new Border();
        activeIndicator.Classes.Add("ActiveIndicator");

        var icon = new MaterialIcon { Kind = iconKind };
        labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };

        var itemContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        itemContent.Classes.Add("ItemContent");
        itemContent.Children.Add(icon);
        itemContent.Children.Add(labelText);

        var content = new Grid();
        content.Children.Add(activeIndicator);
        content.Children.Add(itemContent);

        var button = new Button { Content = content };
        button.Classes.Add("navItem");
        button.SetValue(ToolTip.TipProperty, label);
        return button;
    }

    private void DesktopSearchNav_Click(object? sender, RoutedEventArgs e)
    {
        // Mobile parity: navigation only. Search is committed from the Search page,
        // never from a shell/header textbox.
        NavigateSearch_Click(sender, e);

        Dispatcher.UIThread.Post(() =>
        {
            this.GetVisualDescendants()
                .OfType<SearchView>()
                .FirstOrDefault()
                ?.FocusSearchInput();
        }, DispatcherPriority.Loaded);
    }

    private void DesktopSettingsNav_Click(object? sender, RoutedEventArgs e)
    {
        // Settings remains a complete, dedicated surface until every setting can be
        // migrated without loss. Do not show a partial in-shell page plus a "More"
        // escape hatch.
        CloseSidebar();
        SettingsButton_Click(sender, e);
    }

    private void MobileFirstShellViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveView))
            SyncMobileFirstRailState();
    }

    private void MobileFirstShellSidebar_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WidthProperty)
            SyncMobileFirstRailState();
    }

    private void SyncMobileFirstRailState()
    {
        if (_desktopSearchNavButton is not null)
        {
            var isActive = _mainViewModel.ActiveView == AppView.Search;
            if (isActive)
            {
                if (!_desktopSearchNavButton.Classes.Contains("active"))
                    _desktopSearchNavButton.Classes.Add("active");
            }
            else
            {
                _desktopSearchNavButton.Classes.Remove("active");
            }
        }

        var expanded = _isSidebarOpen;
        if (_desktopSearchNavText is not null)
            _desktopSearchNavText.IsVisible = expanded;
        if (_desktopSettingsNavText is not null)
            _desktopSettingsNavText.IsVisible = expanded;

        if (_desktopSearchNavButton is not null)
            _desktopSearchNavButton.SetValue(
                ToolTip.TipProperty,
                expanded ? null : LocalizationSource.Instance["Shell.Search.Tooltip"]);
        if (_desktopSettingsNavButton is not null)
            _desktopSettingsNavButton.SetValue(
                ToolTip.TipProperty,
                expanded ? null : LocalizationSource.Instance["Shell.Settings.Tooltip"]);
    }

    private void MobileFirstShell_Closed(object? sender, EventArgs e)
    {
        _mainViewModel.PropertyChanged -= MobileFirstShellViewModel_PropertyChanged;
        SideBar.PropertyChanged -= MobileFirstShellSidebar_PropertyChanged;
        Closed -= MobileFirstShell_Closed;
    }
}
