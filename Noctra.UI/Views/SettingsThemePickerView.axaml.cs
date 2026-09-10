using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Noctra.ViewModels;

namespace Noctra.UI.Views;

public partial class SettingsThemePickerView : UserControl
{
    private SettingsViewModel? _viewModel;

    public SettingsThemePickerView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ApplyAdaptiveLayout(args.NewSize.Width);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        DetachViewModel();
        base.OnDataContextChanged(e);
        AttachViewModel();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachViewModel();
        ApplyAdaptiveLayout(Bounds.Width);
    }

    private void AttachViewModel()
    {
        var viewModel = DataContext as SettingsViewModel;
        if (viewModel is null || ReferenceEquals(_viewModel, viewModel))
            return;

        DetachViewModel();
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateThemeSelection(_viewModel.IsDarkTheme);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        DetachViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = null;
        }
    }

    private void ApplyAdaptiveLayout(double width)
    {
        var stacked = double.IsFinite(width) && width > 0 && width < 340;

        Grid.SetColumn(DarkThemeButton, 0);
        Grid.SetRow(DarkThemeButton, 0);
        Grid.SetColumnSpan(DarkThemeButton, stacked ? 2 : 1);

        Grid.SetColumn(LightThemeButton, stacked ? 0 : 1);
        Grid.SetRow(LightThemeButton, stacked ? 1 : 0);
        Grid.SetColumnSpan(LightThemeButton, stacked ? 2 : 1);

        ThemeLayoutRoot.ColumnSpacing = stacked ? 0 : 12;
        ThemeLayoutRoot.RowSpacing = stacked ? 12 : 0;
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
}
