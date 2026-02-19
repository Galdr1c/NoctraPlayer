using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class GlobalSettingsWindow : Window
{
    public GlobalSettingsWindow()
        : this(((App)Application.Current!).Services.GetRequiredService<GlobalSettingsViewModel>())
    {
    }

    public GlobalSettingsWindow(GlobalSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        UpdateThemeSelection(viewModel.Settings.IsDarkTheme);

        // Listen for changes to update UI selection
        viewModel.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(GlobalSettingsViewModel.Settings)) {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateThemeSelection(viewModel.Settings.IsDarkTheme));
            }
        };

        viewModel.Settings.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(GlobalSettings.IsDarkTheme)) {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateThemeSelection(viewModel.Settings.IsDarkTheme));
            }
        };
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

    private void DarkTheme_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not GlobalSettingsViewModel vm)
        {
            return;
        }

        vm.Settings.IsDarkTheme = true;
        UpdateThemeSelection(true);
        vm.ApplyThemeCommand.Execute(null);
    }

    private void LightTheme_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not GlobalSettingsViewModel vm)
        {
            return;
        }

        vm.Settings.IsDarkTheme = false;
        UpdateThemeSelection(false);
        vm.ApplyThemeCommand.Execute(null);
    }

    private void UpdateThemeSelection(bool isDark)
    {
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
}
