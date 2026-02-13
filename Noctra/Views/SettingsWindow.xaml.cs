using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Noctra.ViewModels;
using Noctra;

namespace Noctra.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Set initial theme selection
        UpdateThemeSelection(_viewModel.IsDarkTheme);
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch
        {
            // Ignore DragMove edge cases.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
    
    private void ChangeDownloadPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "İndirme Klasörünü Seçin",
            InitialDirectory = _viewModel.DownloadPath
        };
        
        if (dialog.ShowDialog() == true)
        {
            _viewModel.DownloadPath = dialog.FolderName;
        }
    }

    private void DarkTheme_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.IsDarkTheme = true;
        // Apply theme via service if needed, ViewModel might already handle it in a setter but 
        // SettingsViewModel uses [ObservableProperty] and a ToggleTheme command.
        // Let's call the viewmodel's theme logic.
        UpdateThemeSelection(true);
        ApplyTheme(true);
    }

    private void LightTheme_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.IsDarkTheme = false;
        UpdateThemeSelection(false);
        ApplyTheme(false);
    }

    private void ApplyTheme(bool isDark)
    {
        // SettingsViewModel now handles the save automatically on property change
        _viewModel.IsDarkTheme = isDark;
    }

    private void UpdateThemeSelection(bool isDark)
    {
        if (DarkThemeButton == null || LightThemeButton == null) return;

        if (isDark)
        {
            DarkThemeButton.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            LightThemeButton.BorderBrush = System.Windows.Media.Brushes.Transparent;
            DarkCheckmark.Visibility = Visibility.Visible;
            LightCheckmark.Visibility = Visibility.Collapsed;
        }
        else
        {
            DarkThemeButton.BorderBrush = System.Windows.Media.Brushes.Transparent;
            LightThemeButton.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            DarkCheckmark.Visibility = Visibility.Collapsed;
            LightCheckmark.Visibility = Visibility.Visible;
        }
    }

    private void BackToProfiles_Click(object sender, RoutedEventArgs e)
    {
        var ownerMainWindow = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;

        Close();

        ownerMainWindow?.OpenProfileSelection();
    }
}


