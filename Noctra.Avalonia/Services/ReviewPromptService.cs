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
    private static readonly TimeSpan OwnerRetryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OwnerWaitTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EventOwnerWaitTimeout = TimeSpan.FromSeconds(10);

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
        ILocalizationService localizationService,
        ReviewPromptTracker? reviewPromptTracker = null)
    {
        _settingsService = settingsService;
        _appEditionService = appEditionService;
        _packageIdentityService = packageIdentityService;
        _localizationService = localizationService;

        if (reviewPromptTracker is not null)
        {
            reviewPromptTracker.PromptRequested += OnPromptRequested;
        }
    }

    private void OnPromptRequested()
    {
        _ = TryShowPromptAsync();
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
            if (settings.ReviewPromptLaunchCount < ReviewPromptPolicy.MinimumLaunches)
            {
                settings.ReviewPromptLaunchCount++;
                await _settingsService.SaveAsyncBestEffort();
            }

            if (!ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow))
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
            await Task.Delay(ReviewPromptPolicy.LaunchSettleDelay, cancellationToken);
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

    public async Task TryShowPromptAsync(CancellationToken cancellationToken = default)
    {
        // Event-driven entry: no launch counter bump, no settle delay.
        if (!ReviewPromptPolicy.IsEligible(_settingsService.Settings, DateTime.UtcNow))
        {
            return;
        }

        // Event-driven calls (e.g. right after a playback session) should not
        // hold the flow open for the long launch-path owner timeout; bail out
        // quickly if the window is not ready right now.
        var ownerReady = await WaitForOwnerAsync(cancellationToken, EventOwnerWaitTimeout);
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
            if (!ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow) || !TryGetOwner(out var owner))
            {
                return;
            }

            var reviewUri = BuildReviewUri();
            settings.ReviewPromptLastShownAtUtc = DateTime.UtcNow;
            await _settingsService.SaveAsyncBestEffort();

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
                        settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.Add(ReviewPromptPolicy.SnoozeDuration);
                    }

                    await _settingsService.SaveAsyncBestEffort();
                    break;

                case ReviewPromptResult.Never:
                    settings.ReviewPromptDismissed = true;
                    await _settingsService.SaveAsyncBestEffort();
                    break;

                default:
                    settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.Add(ReviewPromptPolicy.SnoozeDuration);
                    await _settingsService.SaveAsyncBestEffort();
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> WaitForOwnerAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? OwnerWaitTimeout);

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
