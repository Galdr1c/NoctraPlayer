using System.Windows;
using System.Windows.Controls;
using Noctra.ViewModels;

namespace Noctra.Views;

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

        // Window sürükleme
        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        };
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
}


