using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Mobile.Localization;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Mobile.Views;

public partial class MobileLegalConsentView : UserControl
{
    private ISettingsService? _settingsService;
    private ILocalizationService? _localizationService;

    public MobileLegalConsentView()
    {
        InitializeComponent();
    }

    public bool DiagnosticDataConsent => DiagnosticDataCheckBox.IsChecked == true;

    public bool HasAccepted { get; private set; }

    /// <summary>
    /// Shows the legal consent flow. Returns true if the user accepted.
    /// </summary>
    public async Task<bool> ShowConsentFlowAsync()
    {
        ResolveServices();
        if (_settingsService == null)
            return true;

        if (!RequiresLegalConsent(_settingsService.Settings))
            return true;

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe to completion
        void OnConsentCompleted(object? s, EventArgs e)
        {
            Consented -= OnConsentCompleted;
            completion.TrySetResult(HasAccepted);
        }

        Consented += OnConsentCompleted;

        IsVisible = true;

        return await completion.Task;
    }

    private event EventHandler? Consented;

    private void ResolveServices()
    {
        if (_settingsService != null)
            return;

        if (Application.Current is not App { Services: not null } app)
            return;

        _settingsService = app.Services.GetService<ISettingsService>();
        _localizationService = app.Services.GetService<ILocalizationService>();
    }

    private static bool RequiresLegalConsent(AppSettings settings)
    {
        return !settings.LegalConsentAccepted ||
               !string.Equals(settings.LegalConsentVersion, AppSettings.CurrentLegalConsentVersion, StringComparison.Ordinal) ||
               !string.Equals(settings.PrivacyNoticeVersion, AppSettings.CurrentPrivacyNoticeVersion, StringComparison.Ordinal);
    }

    private void RequiredCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        UpdateContinueButtonState();
    }

    private void UpdateContinueButtonState()
    {
        var allRequiredAccepted =
            TermsAcceptedCheckBox.IsChecked == true &&
            NoContentCheckBox.IsChecked == true &&
            LawfulSourcesCheckBox.IsChecked == true &&
            PrivacyAcceptedCheckBox.IsChecked == true;

        ContinueButton.IsEnabled = allRequiredAccepted;
    }

    private async void Continue_Click(object? sender, RoutedEventArgs e)
    {
        if (!ContinueButton.IsEnabled)
            return;

        ResolveServices();
        if (_settingsService != null)
        {
            var settings = _settingsService.Settings;
            settings.LegalConsentAccepted = true;
            settings.LegalConsentVersion = AppSettings.CurrentLegalConsentVersion;
            settings.PrivacyNoticeVersion = AppSettings.CurrentPrivacyNoticeVersion;
            settings.LegalConsentAcceptedAtUtc = DateTime.UtcNow;
            settings.DiagnosticDataConsent = DiagnosticDataConsent;
            await _settingsService.SaveAsync();
        }

        HasAccepted = true;
        IsVisible = false;

        Consented?.Invoke(this, EventArgs.Empty);
    }

    private void Privacy_Click(object? sender, RoutedEventArgs e)
    {
        ResolveServices();
        if (_localizationService != null)
        {
            LegalDocumentHost.ShowDocument(
                _localizationService.GetString("GlobalSettings.Privacy.Title"),
                _localizationService.GetString("GlobalSettings.Privacy.Message.Current"));
        }
    }

    private void Terms_Click(object? sender, RoutedEventArgs e)
    {
        ResolveServices();
        if (_localizationService != null)
        {
            LegalDocumentHost.ShowDocument(
                _localizationService.GetString("GlobalSettings.Terms.Title"),
                _localizationService.GetString("GlobalSettings.Terms.Message.Current"));
        }
    }
}
