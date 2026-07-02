using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Material.Icons;

namespace Noctra.Mobile.Controls;

/// <summary>
/// Shared empty state component used across all mobile views for consistent styling.
/// Supports an icon, title, description, and optional primary/secondary actions.
/// </summary>
public partial class MobileEmptyStateView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<MobileEmptyStateView, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<MobileEmptyStateView, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<MaterialIconKind> IconKindProperty =
        AvaloniaProperty.Register<MobileEmptyStateView, MaterialIconKind>(nameof(IconKind), MaterialIconKind.Home);

    public static readonly StyledProperty<string> PrimaryActionTextProperty =
        AvaloniaProperty.Register<MobileEmptyStateView, string>(nameof(PrimaryActionText), string.Empty);

    public static readonly StyledProperty<string> SecondaryActionTextProperty =
        AvaloniaProperty.Register<MobileEmptyStateView, string>(nameof(SecondaryActionText), string.Empty);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public MaterialIconKind IconKind
    {
        get => GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public string PrimaryActionText
    {
        get => GetValue(PrimaryActionTextProperty);
        set => SetValue(PrimaryActionTextProperty, value);
    }

    public string SecondaryActionText
    {
        get => GetValue(SecondaryActionTextProperty);
        set => SetValue(SecondaryActionTextProperty, value);
    }

    /// <summary>
    /// Raised when the primary action button is clicked.
    /// </summary>
    public event EventHandler? PrimaryActionClicked;

    /// <summary>
    /// Raised when the secondary action button is clicked.
    /// </summary>
    public event EventHandler? SecondaryActionClicked;

    public MobileEmptyStateView()
    {
        InitializeComponent();

        PrimaryActionButton.Click += OnPrimaryActionClick;
        SecondaryActionButton.Click += OnSecondaryActionClick;

        AffectsRender<MobileEmptyStateView>(TitleProperty, DescriptionProperty, IconKindProperty);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateUI();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty ||
            change.Property == DescriptionProperty ||
            change.Property == IconKindProperty ||
            change.Property == PrimaryActionTextProperty ||
            change.Property == SecondaryActionTextProperty)
        {
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        TitleBlock.Text = Title;
        DescriptionBlock.Text = Description;
        EmptyIcon.Kind = IconKind;

        PrimaryActionTextBlock.Text = PrimaryActionText;
        PrimaryActionButton.IsVisible = !string.IsNullOrEmpty(PrimaryActionText);

        SecondaryActionTextBlock.Text = SecondaryActionText;
        SecondaryActionButton.IsVisible = !string.IsNullOrEmpty(SecondaryActionText);
    }

    private void OnPrimaryActionClick(object? sender, RoutedEventArgs e)
        => PrimaryActionClicked?.Invoke(this, EventArgs.Empty);

    private void OnSecondaryActionClick(object? sender, RoutedEventArgs e)
        => SecondaryActionClicked?.Invoke(this, EventArgs.Empty);
}
