using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Avalonia.Localization;
using Noctra.ViewModels;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow()
        : this(((App)Application.Current!).Services.GetRequiredService<SettingsViewModel>())
    {
    }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        UpdateThemeSelection(_viewModel.IsDarkTheme);
        RefreshSelectionLabels();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        LocalizationSource.Instance.PropertyChanged += LocalizationSource_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsDarkTheme))
        {
            Dispatcher.UIThread.Post(() => UpdateThemeSelection(_viewModel.IsDarkTheme));
        }

        if (IsSelectionProperty(e.PropertyName))
        {
            Dispatcher.UIThread.Post(RefreshSelectionLabels);
        }
    }

    private void LocalizationSource_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(RefreshSelectionLabels);

    private static bool IsSelectionProperty(string? propertyName)
        => propertyName is null
            or nameof(SettingsViewModel.SelectedDataUsage)
            or nameof(SettingsViewModel.SubtitleLanguage)
            or nameof(SettingsViewModel.PreferredAudioLanguage)
            or nameof(SettingsViewModel.SelectedDownloadQuality)
            or nameof(SettingsViewModel.ChannelListRefreshFrequencyIndex)
            or nameof(SettingsViewModel.EpgRefreshFrequencyIndex)
            or nameof(SettingsViewModel.EpgTimeOffsetIndex)
            or nameof(SettingsViewModel.WatchHistoryRetentionIndex)
            or nameof(SettingsViewModel.AppLanguage)
            or nameof(SettingsViewModel.IsPremium);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsSelectionSheet.TryClose())
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SettingsSelectionSheet.TryClose();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        LocalizationSource.Instance.PropertyChanged -= LocalizationSource_PropertyChanged;
        base.OnClosed(e);
    }

    private void RefreshSelectionLabels()
    {
        DataUsageSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetDataUsageLabel(_viewModel.SelectedDataUsage);
        SubtitleLanguageSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetPlaybackLanguageLabel(_viewModel.SubtitleLanguage);
        AudioLanguageSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetPlaybackLanguageLabel(
                _viewModel.PreferredAudioLanguage);
        DownloadQualitySelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetDownloadQualityLabel(
                _viewModel.SelectedDownloadQuality);
        ChannelRefreshSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetRefreshFrequencyLabel(
                _viewModel.ChannelListRefreshFrequencyIndex);
        EpgRefreshSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetRefreshFrequencyLabel(
                _viewModel.EpgRefreshFrequencyIndex);
        EpgTimezoneSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetTimezoneLabel(_viewModel.EpgTimeOffsetIndex);
        HistoryRetentionSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetHistoryRetentionLabel(
                _viewModel.WatchHistoryRetentionIndex);
        AppLanguageSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetAppLanguageLabel(_viewModel.AppLanguage);
    }

    private void OpenDataUsageSelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Playback.Quality"),
            DesktopSettingsSelectionCatalog.BuildDataUsage(_viewModel.SelectedDataUsage),
            option => _viewModel.SelectedDataUsage = (int)option.Value);

    private void OpenSubtitleLanguageSelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Playback.PreferredSubtitle"),
            DesktopSettingsSelectionCatalog.BuildPlaybackLanguages(_viewModel.SubtitleLanguage),
            option => _viewModel.SubtitleLanguage = (string)option.Value);

    private void OpenAudioLanguageSelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Playback.PreferredAudio"),
            DesktopSettingsSelectionCatalog.BuildPlaybackLanguages(
                _viewModel.PreferredAudioLanguage),
            option => _viewModel.PreferredAudioLanguage = (string)option.Value);

    private void OpenDownloadQualitySelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Download.Quality"),
            DesktopSettingsSelectionCatalog.BuildDownloadQualities(
                _viewModel.SelectedDownloadQuality),
            option => _viewModel.SelectedDownloadQuality = (int)option.Value);

    private void OpenChannelRefreshSelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Channels.Frequency"),
            DesktopSettingsSelectionCatalog.BuildRefreshFrequencies(
                _viewModel.ChannelListRefreshFrequencyIndex,
                _viewModel.IsPremium),
            option => _viewModel.ChannelListRefreshFrequencyIndex = (int)option.Value,
            ShowPremiumUpsell);

    private void OpenEpgRefreshSelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Channels.Frequency"),
            DesktopSettingsSelectionCatalog.BuildRefreshFrequencies(
                _viewModel.EpgRefreshFrequencyIndex,
                _viewModel.IsPremium),
            option => _viewModel.EpgRefreshFrequencyIndex = (int)option.Value,
            ShowPremiumUpsell);

    private void OpenEpgTimezoneSelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Epg.Timezone"),
            DesktopSettingsSelectionCatalog.BuildTimezones(_viewModel.EpgTimeOffsetIndex),
            option => _viewModel.EpgTimeOffsetIndex = (int)option.Value);

    private void OpenHistoryRetentionSelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Privacy.Retention"),
            DesktopSettingsSelectionCatalog.BuildHistoryRetention(
                _viewModel.WatchHistoryRetentionIndex),
            option => _viewModel.WatchHistoryRetentionIndex = (int)option.Value);

    private void OpenAppLanguageSelection_Click(object? sender, RoutedEventArgs e)
        => SettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("Settings.Language.Title"),
            DesktopSettingsSelectionCatalog.BuildAppLanguages(_viewModel.AppLanguage),
            option => _viewModel.AppLanguage = (string)option.Value);

    private void ShowPremiumUpsell(DesktopSelectionOption _)
        => _viewModel.ShowUpsellCommand.Execute(null);

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void ChangeDownloadPath_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "İndirme Klasörünü Seçin",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                _viewModel.DownloadPath = folders[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            var dialogService = ((App)Application.Current!).Services.GetService<IDialogService>();
            if (dialogService != null)
            {
                await dialogService.ShowErrorAsync("Hata", "Klasör seçilirken bir hata oluştu.", ex);
            }
        }
    }

    private void DarkTheme_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.IsDarkTheme = true;
        UpdateThemeSelection(true);
    }

    private void LightTheme_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.IsDarkTheme = false;
        UpdateThemeSelection(false);
    }

    private void UpdateThemeSelection(bool isDark)
    {
        if (DarkThemeButton == null || LightThemeButton == null) return;

        if (isDark)
        {
            DarkThemeButton.BorderBrush = (IBrush?)Application.Current?.FindResource("AccentBrush");
            LightThemeButton.BorderBrush = Brushes.Transparent;
            DarkCheckmark.IsVisible = true;
            LightCheckmark.IsVisible = false;
        }
        else
        {
            DarkThemeButton.BorderBrush = Brushes.Transparent;
            LightThemeButton.BorderBrush = (IBrush?)Application.Current?.FindResource("AccentBrush");
            DarkCheckmark.IsVisible = false;
            LightCheckmark.IsVisible = true;
        }
    }

    private void BackToProfiles_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (Owner is MainWindow ownerMainWindow)
        {
            ownerMainWindow.OpenProfileSelection();
            Close();
            return;
        }

        var mainWindow = ((App)Application.Current!).Services.GetService<MainWindow>();
        if (mainWindow != null)
        {
            mainWindow.OpenProfileSelection();
            Close();
            return;
        }

        var profilesWindow = ((App)Application.Current!).Services.GetRequiredService<ProfilesWindow>();
        profilesWindow.DisableAutoSelect = true;
        desktop.MainWindow = profilesWindow;
        profilesWindow.Show();
        Close();
    }
}
