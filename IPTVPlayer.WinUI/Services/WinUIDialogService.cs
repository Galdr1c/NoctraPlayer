using IPTVPlayer.Services.Interfaces;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace IPTVPlayer.WinUI.Services;

public class WinUIDialogService : IDialogService
{
    public async Task ShowMessageAsync(string title, string message)
    {
        if (App.Current.MainWindow?.Content?.XamlRoot == null) return;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Tamam",
            XamlRoot = App.Current.MainWindow.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        if (App.Current.MainWindow?.Content?.XamlRoot == null) return false;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Evet",
            CloseButtonText = "Hayır",
            XamlRoot = App.Current.MainWindow.Content.XamlRoot
        };
        
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowUpsellAsync()
    {
        await ShowMessageAsync("Premium", "Upsell ekranı henüz eklenmedi (WinUI)");
    }

    public async Task<bool> ShowAddProfileAsync()
    {
        await ShowMessageAsync("Profil Ekle", "Profil ekleme ekranı henüz eklenmedi (WinUI)");
        return false;
    }

    public async Task<bool> ShowEditProfileAsync(int profileId)
    {
         await ShowMessageAsync("Profil Düzenle", "Profil düzenleme ekranı henüz eklenmedi (WinUI)");
         return false;
    }
}
