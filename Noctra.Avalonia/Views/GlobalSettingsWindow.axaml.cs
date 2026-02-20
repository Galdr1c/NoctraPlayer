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

    private GlobalSettingsViewModel? _viewModel;

    public GlobalSettingsWindow(GlobalSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        
        UpdateThemeSelection(viewModel.Settings.IsDarkTheme);

        // İsimlendirilmiş metotlarla abone ol
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        if (_viewModel.Settings != null)
        {
            _viewModel.Settings.PropertyChanged += Settings_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalSettingsViewModel.Settings))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateThemeSelection(_viewModel!.Settings.IsDarkTheme));
        }
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalSettings.IsDarkTheme))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateThemeSelection(_viewModel!.Settings.IsDarkTheme));
        }
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

    protected override void OnClosed(EventArgs e)
    {
        // Pencere kapanırken abonelikleri KESİNLİKLE kaldır
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            if (_viewModel.Settings != null)
            {
                _viewModel.Settings.PropertyChanged -= Settings_PropertyChanged;
            }
        }
        base.OnClosed(e);
    }
}
