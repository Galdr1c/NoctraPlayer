using IPTVPlayer.Services.Interfaces;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using System;
using Microsoft.UI.Xaml;

namespace IPTVPlayer.WinUI.Services;

public class WinUIDialogService : IDialogService
{
    private static global::IPTVPlayer.WinUI.App AppInstance => (global::IPTVPlayer.WinUI.App)global::Microsoft.UI.Xaml.Application.Current;

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Tamam",
            XamlRoot = AppInstance.MainWindow.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Evet",
            CloseButtonText = "Hayır",
            XamlRoot = AppInstance.MainWindow.Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowErrorAsync(string title, string message, Exception? ex = null)
    {
        string fullMessage = message;
        if (ex != null) fullMessage += $"\n\nDetay: {ex.Message}";
        await ShowMessageAsync(title, fullMessage);
    }

    public Task ShowUpsellAsync()
    {
        // Placeholder until UpsellWindow is migrated to WinUI 3
        return ShowMessageAsync("Premium Özellik", "Bu özellik için premium abonelik gereklidir.");
    }

    public Task<bool> ShowAddProfileAsync()
    {
        // Placeholder until AddProfileWindow is migrated
        return Task.FromResult(false);
    }

    public Task<bool> ShowEditProfileAsync(int profileId)
    {
        // Placeholder until AddProfileWindow is migrated
        return Task.FromResult(false);
    }
}
