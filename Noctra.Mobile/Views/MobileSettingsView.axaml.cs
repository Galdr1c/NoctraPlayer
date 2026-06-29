using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Noctra.Mobile.Localization;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileSettingsView : UserControl
{
    private SettingsViewModel? _viewModel;

    public event EventHandler? BackToProfilesRequested;

    public MobileSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        base.OnDataContextChanged(e);

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is not null)
        {
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

    private void BackToProfiles_Click(object? sender, RoutedEventArgs e)
    {
        BackToProfilesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PrivacyPolicy_Click(object? sender, RoutedEventArgs e)
    {
        LegalDocumentHost.ShowDocument(
            LocalizationSource.Instance["GlobalSettings.Privacy.Title"],
            LocalizationSource.Instance["GlobalSettings.Privacy.Message.Current"]);
    }

    private void Terms_Click(object? sender, RoutedEventArgs e)
    {
        LegalDocumentHost.ShowDocument(
            LocalizationSource.Instance["GlobalSettings.Terms.Title"],
            LocalizationSource.Instance["GlobalSettings.Terms.Message.Current"]);
    }

    private void DarkTheme_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.IsDarkTheme = true;
        UpdateThemeSelection(true);
    }

    private void LightTheme_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.IsDarkTheme = false;
        UpdateThemeSelection(false);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsDarkTheme) && _viewModel is not null)
        {
            UpdateThemeSelection(_viewModel.IsDarkTheme);
        }
    }

    private void UpdateThemeSelection(bool isDark)
    {
        if (DarkThemeButton is null || LightThemeButton is null)
        {
            return;
        }

        var accentBrush = (IBrush?)Application.Current?.FindResource("AccentBrush") ?? Brushes.Transparent;

        DarkThemeButton.BorderBrush = isDark ? accentBrush : Brushes.Transparent;
        LightThemeButton.BorderBrush = isDark ? Brushes.Transparent : accentBrush;
        DarkCheckmark.IsVisible = isDark;
        LightCheckmark.IsVisible = !isDark;
    }
}
