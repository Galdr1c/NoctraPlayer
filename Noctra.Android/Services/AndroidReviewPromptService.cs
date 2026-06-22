using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Noctra.Core.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Android implementation of IReviewPromptService using Google Play In-App Review API.
/// Instead of showing a custom dialog (like desktop), this launches the native
/// Play Store review flow which handles the UI natively.
/// </summary>
public sealed class AndroidReviewPromptService : IReviewPromptService
{
    private const int MinimumLaunches = 1;
    private static readonly TimeSpan PromptDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(3);
    private static readonly TimeSpan ReviewFlowTimeout = TimeSpan.FromSeconds(30);

    private readonly ISettingsService _settingsService;
    private readonly AndroidActivityProvider _activityProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _scheduledThisProcess;

    public AndroidReviewPromptService(
        ISettingsService settingsService,
        AndroidActivityProvider activityProvider)
    {
        _settingsService = settingsService;
        _activityProvider = activityProvider;
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

        await ShowIfStillEligibleAsync(cancellationToken);
    }

    private async Task ShowIfStillEligibleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = _settingsService.Settings;
            if (!IsEligible(settings, DateTime.UtcNow))
            {
                return;
            }

            var activity = _activityProvider.CurrentActivity;
            if (activity is null)
            {
                Debug.WriteLine("[ReviewPrompt] No active Android activity");
                return;
            }

            settings.ReviewPromptLastShownAtUtc = DateTime.UtcNow;
            await _settingsService.SaveAsync();

            var success = await LaunchInAppReviewAsync(activity, cancellationToken);

            if (success)
            {
                // Google considers the review flow launched successfully.
                // The native dialog was shown — Google handles deduplication.
                settings.ReviewPromptCompletedAtUtc = DateTime.UtcNow;
                settings.ReviewPromptDismissed = true;
            }
            else
            {
                // ReviewManager failed (e.g., not on Play Store build, quota exceeded).
                // Fall back to opening Play Store listing directly.
                if (TryOpenPlayStoreListing(activity))
                {
                    settings.ReviewPromptCompletedAtUtc = DateTime.UtcNow;
                    settings.ReviewPromptDismissed = true;
                }
                else
                {
                    // Could not open store either — snooze and retry later.
                    settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.Add(SnoozeDuration);
                }
            }

            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReviewPrompt] Error: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsEligible(Noctra.Models.AppSettings settings, DateTime nowUtc)
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

    /// <summary>
    /// Launches the Google Play In-App Review flow using the native ReviewManager API.
    /// The Play Store handles all UI (rating stars, review text, etc.).
    /// </summary>
    private async Task<bool> LaunchInAppReviewAsync(Activity activity, CancellationToken cancellationToken)
    {
        try
        {
            var manager = Com.Google.Android.Play.Core.Review.ReviewManagerFactory.Create(activity);
            var request = manager.RequestReviewFlow();

            var deadline = DateTime.UtcNow.Add(ReviewFlowTimeout);

            while (!request.IsComplete)
            {
                if (cancellationToken.IsCancellationRequested || DateTime.UtcNow > deadline)
                    return false;
                await Task.Delay(100);
            }

            if (!request.IsSuccessful)
            {
                Debug.WriteLine("[ReviewPrompt] Review request was not successful");
                return false;
            }

            var reviewInfo = request.Result;
            var flow = manager.LaunchReviewFlow(activity, reviewInfo);

            deadline = DateTime.UtcNow.Add(ReviewFlowTimeout);

            while (!flow.IsComplete)
            {
                if (cancellationToken.IsCancellationRequested || DateTime.UtcNow > deadline)
                    return false;
                await Task.Delay(100);
            }

            if (!flow.IsSuccessful)
            {
                Debug.WriteLine("[ReviewPrompt] Review flow was not successful");
                return false;
            }

            Debug.WriteLine("[ReviewPrompt] In-App Review flow completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReviewPrompt] Play Review API unavailable: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fallback: opens the Play Store listing page directly via Intent.
    /// </summary>
    private bool TryOpenPlayStoreListing(Activity activity)
    {
        try
        {
            var packageName = activity.PackageName;
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return false;
            }

            var intent = new Android.Content.Intent(
                Android.Content.Intent.ActionView,
                Android.Net.Uri.Parse($"market://details?id={packageName}"));
            intent.SetPackage("com.android.vending");
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            activity.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReviewPrompt] Failed to open Play Store: {ex.Message}");
            return false;
        }
    }
}
