using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Avalonia.Localization;
using Noctra.Core.Services;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class GlobalSettingsWindow : Window
{
    private GlobalSettingsViewModel? _viewModel;
    private GlobalSettings? _subscribedSettings;
    private bool _isFormattingPromoCode;

    public GlobalSettingsWindow()
        : this(((App)Application.Current!).Services.GetRequiredService<GlobalSettingsViewModel>())
    {
    }

    public GlobalSettingsWindow(GlobalSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        AttachSettings(viewModel.Settings);
        UpdateThemeSelection(viewModel.Settings.IsDarkTheme);
        RefreshSelectionLabels();

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        LocalizationSource.Instance.PropertyChanged += LocalizationSource_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GlobalSettingsViewModel.Settings) || _viewModel is null)
        {
            return;
        }

        AttachSettings(_viewModel.Settings);
        Dispatcher.UIThread.Post(() =>
        {
            UpdateThemeSelection(_viewModel.Settings.IsDarkTheme);
            RefreshSelectionLabels();
        });
    }

    private void Settings_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(GlobalSettings.IsDarkTheme))
        {
            Dispatcher.UIThread.Post(
                () => UpdateThemeSelection(_viewModel.Settings.IsDarkTheme));
        }

        if (e.PropertyName is null or nameof(GlobalSettings.Language))
        {
            Dispatcher.UIThread.Post(RefreshSelectionLabels);
        }
    }

    private void LocalizationSource_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(RefreshSelectionLabels);

    private void AttachSettings(GlobalSettings settings)
    {
        if (ReferenceEquals(_subscribedSettings, settings))
        {
            return;
        }

        if (_subscribedSettings is not null)
        {
            _subscribedSettings.PropertyChanged -= Settings_PropertyChanged;
        }

        _subscribedSettings = settings;
        _subscribedSettings.PropertyChanged += Settings_PropertyChanged;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && GlobalSettingsSelectionSheet.TryClose())
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void RefreshSelectionLabels()
    {
        if (_viewModel is null)
        {
            return;
        }

        GlobalLanguageSelectionValue.Text =
            DesktopSettingsSelectionCatalog.GetAppLanguageLabel(
                _viewModel.Settings.Language);
    }

    private void OpenGlobalLanguageSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        GlobalSettingsSelectionSheet.Show(
            DesktopSettingsSelectionCatalog.Text("GlobalSettings.Language.Title"),
            DesktopSettingsSelectionCatalog.BuildAppLanguages(_viewModel.Settings.Language),
            option => _viewModel.Settings.Language = (string)option.Value);
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

    /// <summary>
    /// Promo kodunu yazıldıkça biçimlendirir: büyük harf + her 4 karakterde
    /// bir '-' (PromoCodeFormatter). Caret konumunu korur; paste edilen düz
    /// değerler de otomatik biçimlenir.
    /// </summary>
    private void PromoCodeTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isFormattingPromoCode || sender is not TextBox textBox)
        {
            return;
        }

        _isFormattingPromoCode = true;
        try
        {
            var caretIndex = textBox.CaretIndex;
            var raw = textBox.Text ?? string.Empty;

            // Caret öncesindeki alfanümerik karakter sayısını hesapla
            var rawAlnumBeforeCaret = raw[..Math.Min(caretIndex, raw.Length)]
                .Count(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));
            var alnumCount = Math.Min(rawAlnumBeforeCaret, PromoCodeFormatter.MaxCharacters);

            // Ortak formatter'ı kullanarak kodu biçimlendir
            var formatted = PromoCodeFormatter.Normalize(raw);
            var newCaretIndex = PromoCodeFormatter.CalculateCaretPosition(alnumCount, formatted.Length);

            if (textBox.Text != formatted)
            {
                textBox.Text = formatted;
                textBox.CaretIndex = newCaretIndex;
            }
        }
        finally
        {
            _isFormattingPromoCode = false;
        }
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
        GlobalSettingsSelectionSheet.TryClose();
        LocalizationSource.Instance.PropertyChanged -= LocalizationSource_PropertyChanged;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (_subscribedSettings is not null)
        {
            _subscribedSettings.PropertyChanged -= Settings_PropertyChanged;
            _subscribedSettings = null;
        }

        base.OnClosed(e);
    }
}
