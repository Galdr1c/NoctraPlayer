using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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
using Noctra.Services;
using Noctra.UI.Views;
using Noctra.ViewModels;

namespace Noctra.Avalonia;

public partial class MainWindow
{
    private bool _mobileFirstDesktopShellInitialized;
    private Button? _desktopSearchNavButton;
    private Button? _desktopSettingsNavButton;
    private TextBlock? _desktopSearchNavText;
    private TextBlock? _desktopSettingsNavText;
    private Grid? _desktopSettingsPageHost;
    private AdaptiveSettingsOverviewView? _desktopSettingsPage;
    private ScopedServiceLease<SettingsViewModel>? _desktopSettingsLease;
    private Task _desktopSettingsRelease = Task.CompletedTask;
    private bool _desktopSettingsPageVisible;

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
        CreateDesktopSettingsPageHost();
        WireSettingsExitNavigation();

        _mainViewModel.PropertyChanged += MobileFirstShellViewModel_PropertyChanged;
        SideBar.PropertyChanged += MobileFirstShellSidebar_PropertyChanged;
        Closed += MobileFirstShell_Closed;

        SyncMobileFirstRailState();
    }

    private void CollapseLegacyDesktopHeader()
    {
        // Mobile is the shell source of truth: its header is hidden and navigation
        // owns Search/Settings. Keep the named HeaderBar itself only as a temporary
        // lifecycle anchor for existing review logic, but detach the entire legacy
        // visual subtree so the old search box/settings action cannot receive focus,
        // pointer input or keyboard input through an invisible surface.
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
        // The original desktop rail was a bare StackPanel. With Search + Settings
        // now matching the mobile destination set, compact-height windows could
        // clip the bottom destinations. Keep the same visual tree and animations,
        // but make the rail vertically scrollable when its content no longer fits.
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

    private void CreateDesktopSettingsPageHost()
    {
        _desktopSettingsPage = new AdaptiveSettingsOverviewView
        {
            ShowAdvancedSettingsAction = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _desktopSettingsPage.BackToProfilesRequested += DesktopSettingsBackToProfilesRequested;
        _desktopSettingsPage.AdvancedSettingsRequested += DesktopAdvancedSettingsRequested;

        _desktopSettingsPageHost = new Grid
        {
            Margin = new Thickness(60, 0, 0, 0),
            IsVisible = false,
            IsHitTestVisible = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = 150
        };
        _desktopSettingsPageHost.Children.Add(_desktopSettingsPage);
        MainContentArea.Children.Add(_desktopSettingsPageHost);
    }

    private void WireSettingsExitNavigation()
    {
        NavHomeBtn.Click += DesktopPrimaryNavigation_Click;
        NavLiveBtn.Click += DesktopPrimaryNavigation_Click;
        NavMoviesBtn.Click += DesktopPrimaryNavigation_Click;
        NavSeriesBtn.Click += DesktopPrimaryNavigation_Click;
        NavFavBtn.Click += DesktopPrimaryNavigation_Click;
        NavMyListBtn.Click += DesktopPrimaryNavigation_Click;
        NavHistoryBtn.Click += DesktopPrimaryNavigation_Click;
        NavDownloadsBtn.Click += DesktopPrimaryNavigation_Click;
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
        HideDesktopSettingsPage();

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

    private async void DesktopSettingsNav_Click(object? sender, RoutedEventArgs e)
    {
        CloseSidebar();
        await ShowDesktopSettingsPageAsync();
    }

    private void DesktopPrimaryNavigation_Click(object? sender, RoutedEventArgs e)
        => HideDesktopSettingsPage();

    private async Task ShowDesktopSettingsPageAsync()
    {
        if (_desktopSettingsPageVisible || _desktopSettingsPage is null || _desktopSettingsPageHost is null)
            return;

        try
        {
            await _desktopSettingsRelease;

            _desktopSettingsLease = ScopedServiceLease<SettingsViewModel>.Create(
                ((App)Application.Current!).Services);
            _desktopSettingsPage.DataContext = _desktopSettingsLease.Service;
            _desktopSettingsPageHost.IsVisible = true;
            _desktopSettingsPageVisible = true;
            SyncMobileFirstRailState();
        }
        catch (Exception ex)
        {
            _mainViewModel.StatusMessage =
                $"{LocalizationSource.Instance["Settings.Error.OpenFailed"]}: {ex.Message}";
        }
    }

    private void HideDesktopSettingsPage()
    {
        if (!_desktopSettingsPageVisible)
            return;

        _desktopSettingsPageVisible = false;
        if (_desktopSettingsPageHost is not null)
            _desktopSettingsPageHost.IsVisible = false;
        if (_desktopSettingsPage is not null)
            _desktopSettingsPage.DataContext = null;

        var lease = _desktopSettingsLease;
        _desktopSettingsLease = null;
        if (lease is not null)
            _desktopSettingsRelease = ReleaseDesktopSettingsLeaseAfterAsync(_desktopSettingsRelease, lease);

        SyncMobileFirstRailState();
    }

    private static async Task ReleaseDesktopSettingsLeaseAfterAsync(
        Task previousRelease,
        ScopedServiceLease<SettingsViewModel> lease)
    {
        await previousRelease.ConfigureAwait(false);
        await lease.DisposeAsync().ConfigureAwait(false);
    }

    private void DesktopSettingsBackToProfilesRequested(object? sender, RoutedEventArgs e)
    {
        HideDesktopSettingsPage();
        OpenProfileSelection();
    }

    private void DesktopAdvancedSettingsRequested(object? sender, RoutedEventArgs e)
    {
        // Transitional bridge while the remaining desktop-only settings controls
        // are migrated section-by-section into the shared mobile-first surface.
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
            var isActive = !_desktopSettingsPageVisible && _mainViewModel.ActiveView == AppView.Search;
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

        if (_desktopSettingsNavButton is not null)
        {
            if (_desktopSettingsPageVisible)
            {
                if (!_desktopSettingsNavButton.Classes.Contains("active"))
                    _desktopSettingsNavButton.Classes.Add("active");
            }
            else
            {
                _desktopSettingsNavButton.Classes.Remove("active");
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

    private async void MobileFirstShell_Closed(object? sender, EventArgs e)
    {
        _mainViewModel.PropertyChanged -= MobileFirstShellViewModel_PropertyChanged;
        SideBar.PropertyChanged -= MobileFirstShellSidebar_PropertyChanged;
        Closed -= MobileFirstShell_Closed;

        NavHomeBtn.Click -= DesktopPrimaryNavigation_Click;
        NavLiveBtn.Click -= DesktopPrimaryNavigation_Click;
        NavMoviesBtn.Click -= DesktopPrimaryNavigation_Click;
        NavSeriesBtn.Click -= DesktopPrimaryNavigation_Click;
        NavFavBtn.Click -= DesktopPrimaryNavigation_Click;
        NavMyListBtn.Click -= DesktopPrimaryNavigation_Click;
        NavHistoryBtn.Click -= DesktopPrimaryNavigation_Click;
        NavDownloadsBtn.Click -= DesktopPrimaryNavigation_Click;

        if (_desktopSettingsPage is not null)
        {
            _desktopSettingsPage.BackToProfilesRequested -= DesktopSettingsBackToProfilesRequested;
            _desktopSettingsPage.AdvancedSettingsRequested -= DesktopAdvancedSettingsRequested;
            _desktopSettingsPage.DataContext = null;
        }

        var lease = _desktopSettingsLease;
        _desktopSettingsLease = null;
        if (lease is not null)
            await lease.DisposeAsync();

        await _desktopSettingsRelease;
    }
}
