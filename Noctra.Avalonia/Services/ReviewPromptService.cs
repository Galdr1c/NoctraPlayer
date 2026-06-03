using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Noctra.Avalonia.Views;
using Noctra.Core.Services;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

public sealed class ReviewPromptService : IReviewPromptService
{
    private const int MinimumLaunches = 1;
    private static readonly TimeSpan PromptDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan OwnerRetryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OwnerWaitTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(3);

    private readonly ISettingsService _settingsService;
    private readonly IAppEditionService _appEditionService;
    private readonly IPackageIdentityService _packageIdentityService;
    private readonly ILocalizationService _localizationService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _scheduledThisProcess;

    public ReviewPromptService(
        ISettingsService settingsService,
        IAppEditionService appEditionService,
        IPackageIdentityService packageIdentityService,
        ILocalizationService localizationService)
    {
        _settingsService = settingsService;
        _appEditionService = appEditionService;
        _packageIdentityService = packageIdentityService;
        _localizationService = localizationService;
    }

    public async Task TryShowMainWindowPromptAsync(CancellationToken cancellationToken = default)
    {
        if (_scheduledThisProcess)
        {
            return;
        }

        _scheduledThisProcess = true;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = _settingsService.Settings;
            settings.ReviewPromptLaunchCount++;
            await _settingsService.SaveAsync();

            if (!IsEligible(settings, DateTime.UtcNow))
            {
                return;
            }
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await Task.Delay(PromptDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var ownerReady = await WaitForOwnerAsync(cancellationToken);
        if (!ownerReady)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ShowIfStillEligibleAsync();
        });
    }

    private async Task ShowIfStillEligibleAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var settings = _settingsService.Settings;
            if (!IsEligible(settings, DateTime.UtcNow) || !TryGetOwner(out var owner))
            {
                return;
            }

            var reviewUri = BuildReviewUri();
            settings.ReviewPromptLastShownAtUtc = DateTime.UtcNow;
            await _settingsService.SaveAsync();

            var dialog = new ReviewPromptWindow();
            var result = await dialog.ShowDialog<ReviewPromptResult?>(owner) ?? ReviewPromptResult.Later;

            switch (result)
            {
                case ReviewPromptResult.RateNow:
                    if (OpenStoreReview(reviewUri))
                    {
                        settings.ReviewPromptCompletedAtUtc = DateTime.UtcNow;
                        settings.ReviewPromptDismissed = true;
                    }
                    else
                    {
                        settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.Add(SnoozeDuration);
                    }

                    await _settingsService.SaveAsync();
                    break;

                case ReviewPromptResult.Never:
                    settings.ReviewPromptDismissed = true;
                    await _settingsService.SaveAsync();
                    break;

                default:
                    settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.Add(SnoozeDuration);
                    await _settingsService.SaveAsync();
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsEligible(Noctra.Models.AppSettings settings, DateTime nowUtc)
    {
        if (settings.ReviewPromptDismissed || settings.ReviewPromptCompletedAtUtc.HasValue)
        {
            return false;
        }

        if (settings.ReviewPromptLaunchCount < MinimumLaunches)
        {
            return false;
        }

        if (settings.ReviewPromptSnoozedUntilUtc.HasValue && settings.ReviewPromptSnoozedUntilUtc.Value > nowUtc)
        {
            return false;
        }

        if (settings.ReviewPromptLastShownAtUtc.HasValue &&
            nowUtc - settings.ReviewPromptLastShownAtUtc.Value < SnoozeDuration)
        {
            return false;
        }

        return true;
    }

    private async Task<bool> WaitForOwnerAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(OwnerWaitTimeout);

        while (DateTime.UtcNow < deadline)
        {
            var ownerReady = await Dispatcher.UIThread.InvokeAsync(() => TryGetOwner(out _));
            if (ownerReady)
            {
                return true;
            }

            try
            {
                await Task.Delay(OwnerRetryInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private bool TryGetOwner(out Window owner)
    {
        owner = null!;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is not { IsVisible: true } mainWindow ||
            mainWindow is not MainWindow noctraMainWindow)
        {
            return false;
        }

        if (!noctraMainWindow.IsReviewPromptAllowedSurface)
        {
            return false;
        }

        if (HasBlockingWindow(mainWindow))
        {
            return false;
        }

        owner = mainWindow;
        return true;
    }

    private static bool HasBlockingWindow(Window owner)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return true;
        }

        return desktop.Windows.Any(window =>
            window.IsVisible &&
            !ReferenceEquals(window, owner) &&
            window is not ReviewPromptWindow);
    }

    private string BuildReviewUri()
    {
        if (IsUsableStoreUri(_appEditionService.StoreReviewLaunchUri))
        {
            return _appEditionService.StoreReviewLaunchUri;
        }

        if (IsUsableStoreProductId(_appEditionService.StoreProductId))
        {
            return $"ms-windows-store://review/?productid={Uri.EscapeDataString(_appEditionService.StoreProductId)}";
        }

        if (_packageIdentityService.IsPackaged && !string.IsNullOrWhiteSpace(_packageIdentityService.PackageFamilyName))
        {
            return $"ms-windows-store://review/?PFN={Uri.EscapeDataString(_packageIdentityService.PackageFamilyName)}";
        }

        return string.Empty;
    }

    private static bool IsUsableStoreUri(string? uri)
    {
        return !string.IsNullOrWhiteSpace(uri) &&
               uri.Contains("ms-windows-store://review/", StringComparison.OrdinalIgnoreCase) &&
               !uri.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsableStoreProductId(string? productId)
    {
        return !string.IsNullOrWhiteSpace(productId) &&
               !productId.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
    }

    private bool OpenStoreReview(string reviewUri)
    {
        if (string.IsNullOrWhiteSpace(reviewUri))
        {
            ShowStoreOpenError();
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = reviewUri,
                UseShellExecute = true
            });

            return true;
        }
        catch (Exception ex)
        {
            ShowStoreOpenError();
            StartupDiagnostics.LogException("Failed to open Microsoft Store review URI.", ex);
            return false;
        }
    }

    private void ShowStoreOpenError()
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var ownerAvailable = TryGetOwner(out var owner);
            if (!ownerAvailable)
            {
                return;
            }

            var dialog = new DialogWindow(
                _localizationService.GetString("ReviewPrompt.Error.Title"),
                _localizationService.GetString("ReviewPrompt.Error.Message"),
                DialogMode.Error);
            await dialog.ShowDialog(owner);
        });
    }
}
