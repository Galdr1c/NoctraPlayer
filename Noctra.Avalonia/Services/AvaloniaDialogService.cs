using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Data;
using Noctra.Avalonia.Views;
using Noctra.Services.Interfaces;

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
        var fullMessage = ex == null ? message : $"{message}\n{ex.Message}";
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

    public async Task<bool> ShowEditProfileAsync(int profileId)
    {
        var owner = GetMainWindow();
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.Profiles
            .Include(p => p.ProviderAccount)
            .FirstOrDefaultAsync(p => p.Id == profileId);
        if (profile == null)
        {
            return false;
        }

        var window = scope.ServiceProvider.GetRequiredService<AddProfileWindow>();
        if (window.DataContext is Noctra.ViewModels.AddProfileViewModel vm)
        {
            vm.InitializeForEdit(profile);
        }

        var result = await window.ShowDialog<bool?>(owner);
        return result == true;
    }

    private static Window GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            return desktop.MainWindow;
        }

        throw new InvalidOperationException("Main window not found.");
    }
}
