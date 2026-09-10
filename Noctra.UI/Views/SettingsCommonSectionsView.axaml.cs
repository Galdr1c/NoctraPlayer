using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Noctra.ViewModels;

namespace Noctra.UI.Views;

public partial class SettingsCommonSectionsView : UserControl
{
    private SettingsViewModel? _viewModel;

    public SettingsCommonSectionsView()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? BackToProfilesRequested;

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        base.OnDataContextChanged(e);

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateThemeSelection(_viewModel.IsDarkTheme);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsDarkTheme) && _viewModel is not null)
            UpdateThemeSelection(_viewModel.IsDarkTheme);
    }

    private void DarkTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        _viewModel.IsDarkTheme = true;
        UpdateThemeSelection(true);
    }

    private void LightTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        _viewModel.IsDarkTheme = false;
        UpdateThemeSelection(false);
    }

    private void UpdateThemeSelection(bool isDark)
    {
        var accent = Application.Current?.FindResource("AccentBrush") as IBrush;
        var normal = Application.Current?.FindResource("BorderBrush") as IBrush;

        DarkThemeButton.BorderBrush = isDark ? accent : normal;
        LightThemeButton.BorderBrush = isDark ? normal : accent;
        DarkCheckmark.IsVisible = isDark;
        LightCheckmark.IsVisible = !isDark;
    }

    private void BackToProfiles_Click(object? sender, RoutedEventArgs e)
        => BackToProfilesRequested?.Invoke(this, e);
}
