using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Noctra.Mobile.Localization;
using Noctra.Mobile.Navigation;
using Noctra.Mobile.Services;
using Noctra.Core.Services;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileSettingsView : UserControl, IMobileNavigationStateParticipant
{
    private SettingsViewModel? _viewModel;
    private bool _isFormattingPromoCode;
    private bool _privacyChoicesSubscribed;

    private static readonly (string Value, string Key)[] AppLanguageOptions =
    {
        ("en", "Language.English"),
        ("tr", "Language.Turkish"),
        ("de", "Language.German"),
        ("fr", "Language.French"),
        ("es", "Language.Spanish")
    };

    private static readonly (string Value, string Key)[] MediaLanguageOptions =
    {
        ("tr", "Language.Turkish"),
        ("en", "Language.English"),
        ("de", "Language.German"),
        ("fr", "Language.French"),
        ("es", "Language.Spanish"),
        ("it", "Language.Italian"),
        ("pt", "Language.Portuguese"),
        ("ru", "Language.Russian"),
        ("ar", "Language.Arabic"),
        ("nl", "Language.Dutch")
    };

    private static readonly string[] DataUsageKeys =
    {
        "Settings.Playback.Quality.Low",
        "Settings.Playback.Quality.Medium",
        "Settings.Playback.Quality.High",
        "Settings.Playback.Quality.Auto"
    };

    private static readonly string[] RefreshFrequencyKeys =
    {
        "Settings.Channels.Frequency.Off",
        "Settings.Channels.Frequency.1h",
        "Settings.Channels.Frequency.3h",
        "Settings.Channels.Frequency.12h",
        "Settings.Channels.Frequency.24h",
        "Settings.Channels.Frequency.2d",
        "Settings.Channels.Frequency.3d",
        "Settings.Channels.Frequency.7d"
    };

    private static readonly string[] WatchHistoryRetentionKeys =
    {
        "Settings.Privacy.Retention.Forever",
        "Settings.Privacy.Retention.3d",
        "Settings.Privacy.Retention.7d",
        "Settings.Privacy.Retention.14d",
        "Settings.Privacy.Retention.30d"
    };

    public event EventHandler? BackToProfilesRequested;

    public MobileSettingsView()
    {
        InitializeComponent();
    }

    bool IMobileNavigationStateParticipant.TryCaptureNavigationState(out MobilePageScrollState state)
        => MobileNavigationScrollState.TryCapture(SettingsScrollViewer, out state);

    bool IMobileNavigationStateParticipant.TryRestoreNavigationState(
        MobilePageScrollState state,
        bool allowClamping)
        => MobileNavigationScrollState.TryRestore(SettingsScrollViewer, state, allowClamping);

    protected override void OnDataContextChanged(EventArgs e)
    {
        SelectionSheetHost.TryClose();

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        base.OnDataContextChanged(e);

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is not null)
        {
            ResetHiddenCategoryExpanders();
            _viewModel.EnableAutoSave();
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateThemeSelection(_viewModel.IsDarkTheme);
            UpdateSelectionLabels();
            SubscribePrivacyChoices();
        }
    }

    private void SubscribePrivacyChoices()
    {
        if (_privacyChoicesSubscribed)
            return;

        var ads = MobileAdvertisingServices.TryGet();
        if (ads is not null)
        {
            ads.ConsentStatusChanged += AdService_ConsentStatusChanged;
        }

        _privacyChoicesSubscribed = true;
        UpdatePrivacyChoicesVisibility();
    }

    private void UnsubscribePrivacyChoices()
    {
        if (!_privacyChoicesSubscribed)
            return;

        var ads = MobileAdvertisingServices.TryGet();
        if (ads is not null)
        {
            ads.ConsentStatusChanged -= AdService_ConsentStatusChanged;
        }

        _privacyChoicesSubscribed = false;
    }

    private void AdService_ConsentStatusChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(UpdatePrivacyChoicesVisibility);

    private void UpdatePrivacyChoicesVisibility()
    {
        var ads = MobileAdvertisingServices.TryGet();
        PrivacyChoicesButton.IsVisible = ads is { CanShowPrivacyOptions: true };
    }

    private async void PrivacyChoices_Click(object? sender, RoutedEventArgs e)
    {
        var ads = MobileAdvertisingServices.TryGet();
        if (ads is null)
        {
            return;
        }

        try
        {
            // Opens the UMP privacy-options form (GDPR / US-state choices).
            await ads.ShowPrivacyOptionsAsync();
        }
        catch
        {
            // Best-effort: a failed UMP form must never break Settings.
        }
    }

    private void ResetHiddenCategoryExpanders()
    {
        HiddenLiveCategoriesExpander.IsExpanded = false;
        HiddenMovieCategoriesExpander.IsExpanded = false;
        HiddenSeriesCategoriesExpander.IsExpanded = false;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = null;
        }

        UnsubscribePrivacyChoices();
        SelectionSheetHost.TryClose();

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
            LocalizationSource.Instance["GlobalSettings.Privacy.Message.Mobile"]);
    }

    private void Terms_Click(object? sender, RoutedEventArgs e)
    {
        LegalDocumentHost.ShowDocument(
            LocalizationSource.Instance["GlobalSettings.Terms.Title"],
            LocalizationSource.Instance["GlobalSettings.Terms.Message.Mobile"]);
    }

    private void ShowUpsell_Click(object? sender, RoutedEventArgs e)
    {
        UpsellHost.Show();
    }

    private void PromoCodeTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        // Klavyedeki "Bitti"/Enter tuşu kodu doğrudan uygular (madde 17).
        if (e.Key != Key.Enter || _viewModel is null)
        {
            return;
        }

        _viewModel.ApplyPromoCodeCommand.Execute(null);
        e.Handled = true;
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

    private void DarkTheme_Tapped(object? sender, TappedEventArgs e)
    {
        // Tapped (PointerPressed değil) kullanıyoruz: parmak ekrana değdiği an
        // değil, gerçek bir "tap" (kısa dokunma) olduğunda tetiklenir. Böylece
        // kullanıcı scroll için parmağını kaydırırken tema butonunun üstünden
        // geçince yanlışlıkla tema değişmez.
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.IsDarkTheme = true;
        UpdateThemeSelection(true);
    }

    private void LightTheme_Tapped(object? sender, TappedEventArgs e)
    {
        // Tapped: scroll sırasında yanlışlıkla tetiklenmeyi önler (yukarıya bakın).
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

        if (e.PropertyName is nameof(SettingsViewModel.AppLanguage)
            or nameof(SettingsViewModel.SelectedDataUsage)
            or nameof(SettingsViewModel.SubtitleLanguage)
            or nameof(SettingsViewModel.PreferredAudioLanguage)
            or nameof(SettingsViewModel.ChannelListRefreshFrequencyIndex)
            or nameof(SettingsViewModel.EpgRefreshFrequencyIndex)
            or nameof(SettingsViewModel.EpgTimeOffsetIndex)
            or nameof(SettingsViewModel.WatchHistoryRetentionIndex))
        {
            UpdateSelectionLabels();
        }
    }

    internal bool TryHandleBack()
    {
        if (SelectionSheetHost.TryClose())
        {
            return true;
        }

        if (UpsellHost.TryClose())
        {
            return true;
        }

        if (LegalDocumentHost.IsVisible)
        {
            LegalDocumentHost.IsVisible = false;
            return true;
        }

        return false;
    }

    private void OpenSelectionSheet_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null ||
            sender is not Control { Tag: string tag } ||
            !Enum.TryParse<SettingsSelectionKind>(tag, out var kind))
        {
            return;
        }

        SelectionSheetHost.Show(
            GetSelectionTitle(kind),
            BuildOptions(kind),
            option =>
            {
                ApplySelection(kind, option);
                UpdateSelectionLabels();
            },
            _ => UpsellHost.Show());
    }

    private IReadOnlyList<MobileSelectionOption> BuildOptions(SettingsSelectionKind kind)
    {
        var viewModel = _viewModel!;
        return kind switch
        {
            SettingsSelectionKind.AppLanguage => CreateStringOptions(AppLanguageOptions, viewModel.AppLanguage),
            SettingsSelectionKind.DataUsage => CreateIndexOptions(DataUsageKeys, viewModel.SelectedDataUsage),
            SettingsSelectionKind.SubtitleLanguage => CreateStringOptions(MediaLanguageOptions, viewModel.SubtitleLanguage),
            SettingsSelectionKind.PreferredAudioLanguage => CreateStringOptions(MediaLanguageOptions, viewModel.PreferredAudioLanguage),
            SettingsSelectionKind.ChannelRefreshFrequency => CreateIndexOptions(
                RefreshFrequencyKeys,
                viewModel.ChannelListRefreshFrequencyIndex,
                index => index > 0 && !viewModel.IsPremium),
            SettingsSelectionKind.EpgRefreshFrequency => CreateIndexOptions(
                RefreshFrequencyKeys,
                viewModel.EpgRefreshFrequencyIndex,
                index => index > 0 && !viewModel.IsPremium),
            SettingsSelectionKind.EpgTimeOffset => CreateEpgOffsetOptions(viewModel.EpgTimeOffsetIndex),
            SettingsSelectionKind.WatchHistoryRetention => CreateIndexOptions(
                WatchHistoryRetentionKeys,
                viewModel.WatchHistoryRetentionIndex),
            _ => Array.Empty<MobileSelectionOption>()
        };
    }

    private void ApplySelection(SettingsSelectionKind kind, MobileSelectionOption option)
    {
        var viewModel = _viewModel!;
        switch (kind)
        {
            case SettingsSelectionKind.AppLanguage:
                viewModel.AppLanguage = (string)option.Value;
                break;
            case SettingsSelectionKind.DataUsage:
                viewModel.SelectedDataUsage = (int)option.Value;
                break;
            case SettingsSelectionKind.SubtitleLanguage:
                viewModel.SubtitleLanguage = (string)option.Value;
                break;
            case SettingsSelectionKind.PreferredAudioLanguage:
                viewModel.PreferredAudioLanguage = (string)option.Value;
                break;
            case SettingsSelectionKind.ChannelRefreshFrequency:
                viewModel.ChannelListRefreshFrequencyIndex = (int)option.Value;
                break;
            case SettingsSelectionKind.EpgRefreshFrequency:
                viewModel.EpgRefreshFrequencyIndex = (int)option.Value;
                break;
            case SettingsSelectionKind.EpgTimeOffset:
                viewModel.EpgTimeOffsetIndex = (int)option.Value;
                break;
            case SettingsSelectionKind.WatchHistoryRetention:
                viewModel.WatchHistoryRetentionIndex = (int)option.Value;
                break;
        }
    }

    private void UpdateSelectionLabels()
    {
        if (_viewModel is null)
        {
            return;
        }

        AppLanguageSelectionValue.Text = GetLanguageLabel(_viewModel.AppLanguage);
        DataUsageSelectionValue.Text = GetIndexedLabel(DataUsageKeys, _viewModel.SelectedDataUsage);
        SubtitleLanguageSelectionValue.Text = GetLanguageLabel(_viewModel.SubtitleLanguage);
        PreferredAudioLanguageSelectionValue.Text = GetLanguageLabel(_viewModel.PreferredAudioLanguage);
        ChannelRefreshFrequencySelectionValue.Text = GetIndexedLabel(RefreshFrequencyKeys, _viewModel.ChannelListRefreshFrequencyIndex);
        EpgRefreshFrequencySelectionValue.Text = GetIndexedLabel(RefreshFrequencyKeys, _viewModel.EpgRefreshFrequencyIndex);
        EpgTimeOffsetSelectionValue.Text = GetEpgOffsetLabel(_viewModel.EpgTimeOffsetIndex);
        WatchHistoryRetentionSelectionValue.Text = GetIndexedLabel(WatchHistoryRetentionKeys, _viewModel.WatchHistoryRetentionIndex);
    }

    private static List<MobileSelectionOption> CreateStringOptions(
        IEnumerable<(string Value, string Key)> definitions,
        string selectedValue)
    {
        var options = new List<MobileSelectionOption>();
        foreach (var (value, key) in definitions)
        {
            options.Add(new MobileSelectionOption(
                value,
                Translate(key),
                string.Equals(value, selectedValue, StringComparison.OrdinalIgnoreCase)));
        }

        return options;
    }

    private static List<MobileSelectionOption> CreateIndexOptions(
        IReadOnlyList<string> keys,
        int selectedIndex,
        Func<int, bool>? isLocked = null)
    {
        var options = new List<MobileSelectionOption>(keys.Count);
        for (var index = 0; index < keys.Count; index++)
        {
            options.Add(new MobileSelectionOption(
                index,
                Translate(keys[index]),
                index == selectedIndex,
                isLocked?.Invoke(index) == true));
        }

        return options;
    }

    private static List<MobileSelectionOption> CreateEpgOffsetOptions(int selectedIndex)
    {
        var options = new List<MobileSelectionOption>(25);
        for (var index = 0; index <= 24; index++)
        {
            options.Add(new MobileSelectionOption(index, GetEpgOffsetLabel(index), index == selectedIndex));
        }

        return options;
    }

    private static string GetSelectionTitle(SettingsSelectionKind kind) => kind switch
    {
        SettingsSelectionKind.AppLanguage => Translate("Settings.Language.Title"),
        SettingsSelectionKind.DataUsage => Translate("Settings.Playback.Quality"),
        SettingsSelectionKind.SubtitleLanguage => Translate("Settings.Playback.PreferredSubtitle"),
        SettingsSelectionKind.PreferredAudioLanguage => Translate("Settings.Playback.PreferredAudio"),
        SettingsSelectionKind.ChannelRefreshFrequency => Translate("Settings.Channels.Frequency"),
        SettingsSelectionKind.EpgRefreshFrequency => Translate("Settings.Epg.Refresh"),
        SettingsSelectionKind.EpgTimeOffset => Translate("Settings.Epg.Timezone"),
        SettingsSelectionKind.WatchHistoryRetention => Translate("Settings.Privacy.Retention"),
        _ => string.Empty
    };

    private static string GetLanguageLabel(string? languageCode)
    {
        foreach (var (value, key) in MediaLanguageOptions)
        {
            if (string.Equals(value, languageCode, StringComparison.OrdinalIgnoreCase))
            {
                return Translate(key);
            }
        }

        return string.IsNullOrWhiteSpace(languageCode) ? Translate("Language.English") : languageCode;
    }

    private static string GetIndexedLabel(IReadOnlyList<string> keys, int index)
    {
        if (keys.Count == 0)
        {
            return string.Empty;
        }

        return Translate(keys[Math.Clamp(index, 0, keys.Count - 1)]);
    }

    private static string GetEpgOffsetLabel(int index)
    {
        var offset = Math.Clamp(index, 0, 24) - 12;
        if (offset == 0)
        {
            return Translate("Settings.Epg.Timezone.Auto");
        }

        var key = offset > 0
            ? "Settings.Epg.Timezone.Format.Positive"
            : "Settings.Epg.Timezone.Format.Negative";
        return string.Format(Translate(key), offset);
    }

    private static string Translate(string key) => LocalizationSource.Instance[key];

    private enum SettingsSelectionKind
    {
        AppLanguage,
        DataUsage,
        SubtitleLanguage,
        PreferredAudioLanguage,
        ChannelRefreshFrequency,
        EpgRefreshFrequency,
        EpgTimeOffset,
        WatchHistoryRetention
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
