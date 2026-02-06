using IPTVPlayer.Services.Interfaces;
using System.Windows;
using System.Threading.Tasks;

namespace IPTVPlayer.Services;

public class WpfDialogService : IDialogService
{
    public Task ShowMessageAsync(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string title, string message, Exception? ex = null)
    {
        string fullMessage = message;
        if (ex != null)
        {
            fullMessage += $"\n\nDetay: {ex.Message}";
            if (ex.InnerException != null)
            {
                fullMessage += $"\nİç Hata: {ex.InnerException.Message}";
            }
        }

        MessageBox.Show(fullMessage, title, MessageBoxButton.OK, MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public Task ShowUpsellAsync()
    {
        // UpsellWindow logic could go here, but for now strict implementation
        MessageBox.Show("Premium Upgrade Required (Mock)", "Premium", MessageBoxButton.OK);
        return Task.CompletedTask;
    }

    public Task<bool> ShowAddProfileAsync()
    {
        return Task.FromResult(false); // Mock
    }

    public Task<bool> ShowEditProfileAsync(int profileId)
    {
        return Task.FromResult(false); // Mock
    }
}
