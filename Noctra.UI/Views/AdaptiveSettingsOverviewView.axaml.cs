using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Noctra.UI.Layout;
using Noctra.ViewModels;

namespace Noctra.UI.Views;

public partial class AdaptiveSettingsOverviewView : UserControl
{
    public static readonly StyledProperty<Control?> PlatformContentProperty =
        AvaloniaProperty.Register<AdaptiveSettingsOverviewView, Control?>(nameof(PlatformContent));

    public static readonly StyledProperty<bool> HasPlatformContentProperty =
        AvaloniaProperty.Register<AdaptiveSettingsOverviewView, bool>(nameof(HasPlatformContent));

    public static readonly StyledProperty<bool> ShowAdvancedSettingsActionProperty =
        AvaloniaProperty.Register<AdaptiveSettingsOverviewView, bool>(nameof(ShowAdvancedSettingsAction));

    private SettingsViewModel? _viewModel;

    public AdaptiveSettingsOverviewView()
    {
        InitializeComponent();
        ApplyAdaptiveLayout(Bounds.Width);
        SizeChanged += (_, args) => ApplyAdaptiveLayout(args.NewSize.Width);
    }

    public event EventHandler<RoutedEventArgs>? BackToProfilesRequested;
    public event EventHandler<RoutedEventArgs>? AdvancedSettingsRequested;

    public Control? PlatformContent
    {
        get => GetValue(PlatformContentProperty);
        set => SetValue(PlatformContentProperty, value);
    }

    public bool HasPlatformContent
    {
        get => GetValue(HasPlatformContentProperty);
        private set => SetValue(HasPlatformContentProperty, value);
    }

    public bool ShowAdvancedSettingsAction
    {
        get => GetValue(ShowAdvancedSettingsActionProperty);
        set => SetValue(ShowAdvancedSettingsActionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlatformContentProperty)
            HasPlatformContent = change.NewValue is Control;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        base.OnDataContextChanged(e);

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is not null)
        {
            // Mobile Settings is the behavioral source of truth: edits persist
            // immediately instead of requiring a desktop-only Save action.
            _viewModel.EnableAutoSave();
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateThemeSelection(_viewModel.IsDarkTheme);
        }
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

    private void ApplyAdaptiveLayout(double width)
    {
        var metrics = AdaptiveLayoutMetrics.ForWidth(width);
        var vertical = metrics.LayoutClass == AdaptiveLayoutClass.Compact ? 16 : 20;
        LayoutRoot.Margin = new Thickness(metrics.PagePadding, vertical, metrics.PagePadding, 28);
        LayoutRoot.MaxWidth = metrics.LayoutClass == AdaptiveLayoutClass.Expanded ? 920 : double.PositiveInfinity;
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

    private void AdvancedSettings_Click(object? sender, RoutedEventArgs e)
        => AdvancedSettingsRequested?.Invoke(this, e);
}
