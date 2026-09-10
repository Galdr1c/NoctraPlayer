using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    public AdaptiveSettingsOverviewView()
    {
        InitializeComponent();
        CommonSections.BackToProfilesRequested += CommonSections_BackToProfilesRequested;
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
        base.OnDataContextChanged(e);

        if (DataContext is SettingsViewModel viewModel)
        {
            // Match mobile Settings behavior: edits persist immediately.
            viewModel.EnableAutoSave();
        }
    }

    private void ApplyAdaptiveLayout(double width)
    {
        var metrics = AdaptiveLayoutMetrics.ForWidth(width);
        var vertical = metrics.LayoutClass == AdaptiveLayoutClass.Compact ? 16 : 20;
        LayoutRoot.Margin = new Thickness(metrics.PagePadding, vertical, metrics.PagePadding, 28);
        LayoutRoot.MaxWidth = metrics.LayoutClass == AdaptiveLayoutClass.Expanded
            ? 920
            : double.PositiveInfinity;
    }

    private void CommonSections_BackToProfilesRequested(object? sender, RoutedEventArgs e)
        => BackToProfilesRequested?.Invoke(this, e);

    private void AdvancedSettings_Click(object? sender, RoutedEventArgs e)
        => AdvancedSettingsRequested?.Invoke(this, e);
}
