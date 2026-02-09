using System.Windows;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer.Views;

public partial class GlobalSettingsWindow : Window
{
    private readonly GlobalSettingsViewModel _viewModel;

    public GlobalSettingsWindow(GlobalSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Set initial theme selection
        UpdateThemeSelection(_viewModel.Settings.IsDarkTheme);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void DarkTheme_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.Settings.IsDarkTheme = true;
        UpdateThemeSelection(true);
        _viewModel.ApplyThemeCommand.Execute(null);
    }

    private void LightTheme_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.Settings.IsDarkTheme = false;
        UpdateThemeSelection(false);
        _viewModel.ApplyThemeCommand.Execute(null);
    }

    private void UpdateThemeSelection(bool isDark)
    {
        if (isDark)
        {
            DarkThemeButton.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7C3AED"));
            LightThemeButton.BorderBrush = System.Windows.Media.Brushes.Transparent;
            LightCheckmark.Visibility = Visibility.Collapsed;
        }
        else
        {
            DarkThemeButton.BorderBrush = System.Windows.Media.Brushes.Transparent;
            LightThemeButton.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7C3AED"));
            LightCheckmark.Visibility = Visibility.Visible;
        }
    }
}
