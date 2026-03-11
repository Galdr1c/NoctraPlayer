using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.Notifications;
using Noctra.Avalonia.Views;
using System.Runtime.InteropServices;
using System.IO;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private readonly IServiceProvider _services;

    public AvaloniaDialogService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var owner = GetMainWindow();
        var dialog = new DialogWindow(title, message, DialogMode.Information);
        await dialog.ShowDialog(owner);
    }

    public async Task ShowErrorAsync(string title, string message, Exception? ex = null)
    {
        var owner = GetMainWindow();
        var safeDetail = ex == null
            ? string.Empty
            : UserFriendlyErrorMessage.FromException(ex);
        var fullMessage = string.IsNullOrWhiteSpace(safeDetail)
            ? message
            : $"{message}\n{safeDetail}";
        var dialog = new DialogWindow(title, fullMessage, DialogMode.Error);
        await dialog.ShowDialog(owner);
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var owner = GetMainWindow();
        var dialog = new DialogWindow(title, message, DialogMode.Confirmation);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }

    public async Task ShowUpsellAsync()
    {
        var owner = GetMainWindow();
        var window = _services.GetRequiredService<UpsellWindow>();
        await window.ShowDialog(owner);
    }

    public async Task<bool> ShowAddProfileAsync()
    {
        var owner = GetMainWindow();
        using var scope = _services.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<AddProfileWindow>();
        var result = await window.ShowDialog<bool?>(owner);
        return result == true;
    }

    public async Task<bool> ShowEditProfileAsync(Profile profile)
    {
        var owner = GetMainWindow();
        using var scope = _services.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<AddProfileWindow>();
        if (window.DataContext is Noctra.ViewModels.AddProfileViewModel vm)
        {
            vm.InitializeForEdit(profile);
        }

        var result = await window.ShowDialog<bool?>(owner);
        return result == true;
    }

    public async Task ShowGlobalSettingsAsync()
    {
        var owner = GetMainWindow();
        using var scope = _services.CreateScope();
        var vm = scope.ServiceProvider.GetRequiredService<GlobalSettingsViewModel>();
        var window = scope.ServiceProvider.GetRequiredService<GlobalSettingsWindow>();
        window.DataContext = vm;
        await window.ShowDialog(owner);
    }

    public async Task<string?> ShowAvatarPickerAsync(string? currentAvatar)
    {
        var owner = GetMainWindow();
        using var scope = _services.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<AvatarPickerWindow>();
        var vm = window.DataContext as AvatarPickerViewModel
            ?? scope.ServiceProvider.GetRequiredService<AvatarPickerViewModel>();

        if (!ReferenceEquals(window.DataContext, vm))
        {
            window.DataContext = vm;
        }

        vm.SelectedAvatar = currentAvatar;
        return await window.ShowDialog<string?>(owner);
    }

    public async Task ShowNotificationAsync(string title, string message)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                // Native Windows Toast
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .AddAppLogoOverride(new Uri("file:///" + Path.GetFullPath("Assets/Logo.png")))
                    .Show();
                
                return;
            }
            catch
            {
                // Fallback to custom window if native fails
            }
        }

        var window = new DialogWindow(title, message, DialogMode.Notification);

        // Non-blocking for notifications
        window.Show();
        await Task.CompletedTask;
    }

    private Window GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            return desktop.MainWindow;
        }

        throw new InvalidOperationException("Main window not found.");
    }
}
