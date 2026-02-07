using IPTVPlayer.Services.Interfaces;
using IPTVPlayer.Views;
using System.Windows;
using System.Threading.Tasks;

namespace IPTVPlayer.Services;

public class WpfDialogService : IDialogService
{
    private readonly IDispatcherService _dispatcherService;

    public WpfDialogService(IDispatcherService dispatcherService)
    {
        _dispatcherService = dispatcherService;
    }

    public Task ShowMessageAsync(string title, string message)
    {
        return _dispatcherService.InvokeAsync(() =>
        {
            var dialog = new DialogWindow(title, message, DialogMode.Information)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
            return Task.CompletedTask;
        });
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

        return _dispatcherService.InvokeAsync(() =>
        {
            var dialog = new DialogWindow(title, fullMessage, DialogMode.Error)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
            return Task.CompletedTask;
        });
    }

    public Task<bool> ShowConfirmationAsync(string title, string message)
    {
        return _dispatcherService.InvokeAsync<bool>(() =>
        {
            var dialog = new DialogWindow(title, message, DialogMode.Question)
            {
                Owner = Application.Current.MainWindow
            };
            var result = dialog.ShowDialog();
            return result == true && dialog.DialogResultValue;
        });
    }

    public Task ShowUpsellAsync()
    {
        return ShowMessageAsync("Premium", "Bu özellik için Premium üyelik gereklidir.");
    }

    public Task<bool> ShowAddProfileAsync()
    {
        return Task.FromResult(false);
    }

    public Task<bool> ShowEditProfileAsync(int profileId)
    {
        return Task.FromResult(false);
    }
}
