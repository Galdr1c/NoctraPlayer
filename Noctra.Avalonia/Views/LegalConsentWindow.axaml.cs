using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.Avalonia.Localization;

namespace Noctra.Avalonia.Views;

public partial class LegalConsentWindow : Window
{
    public LegalConsentWindow()
    {
        InitializeComponent();
    }

    public bool DiagnosticDataConsent => DiagnosticDataCheckBox.IsChecked == true;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateContinueButtonState();
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

    private void Continue_Click(object? sender, RoutedEventArgs e)
    {
        if (!ContinueButton.IsEnabled)
        {
            return;
        }

        Close(LegalConsentResult.Accepted(DiagnosticDataConsent));
    }

    private void Decline_Click(object? sender, RoutedEventArgs e)
    {
        Close(LegalConsentResult.Declined);
    }

    private async void Terms_Click(object? sender, RoutedEventArgs e)
    {
        await ShowLegalDocumentAsync(
            LocalizationSource.Instance["GlobalSettings.Terms.Title"],
            LocalizationSource.Instance["GlobalSettings.Terms.Message.Current"]);
    }

    private async void Privacy_Click(object? sender, RoutedEventArgs e)
    {
        await ShowLegalDocumentAsync(
            LocalizationSource.Instance["GlobalSettings.Privacy.Title"],
            LocalizationSource.Instance["GlobalSettings.Privacy.Message.Current"]);
    }

    private async Task ShowLegalDocumentAsync(string title, string message)
    {
        var dialog = new LegalDocumentWindow(title, message)
        {
            Topmost = false
        };

        await dialog.ShowDialog(this);
    }

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}

public sealed record LegalConsentResult(bool IsAccepted, bool DiagnosticDataConsent)
{
    public static LegalConsentResult Accepted(bool diagnosticDataConsent) => new(true, diagnosticDataConsent);
    public static LegalConsentResult Declined { get; } = new(false, false);
}
