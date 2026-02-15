using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Noctra.ViewModels;

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

        // Set initial theme selection
        UpdateThemeSelection(_viewModel.IsDarkTheme);
    }

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
