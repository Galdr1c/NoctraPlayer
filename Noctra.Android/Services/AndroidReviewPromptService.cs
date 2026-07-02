using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Noctra.Core.Services;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Debug = System.Diagnostics.Debug;

namespace Noctra.Android.Services;

/// <summary>
/// Android implementation of IReviewPromptService using Google Play In-App Review API.
/// Falls back to an Avalonia in-app bottom sheet (via <see cref="ReviewPromptFallbackHandler"/>)
/// and, as a last resort, a native Android dialog.
/// </summary>
public sealed class AndroidReviewPromptService : IReviewPromptService
{
    private const int MinimumLaunches = 1;
    private static readonly TimeSpan PromptDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(3);
    private static readonly TimeSpan ReviewFlowTimeout = TimeSpan.FromSeconds(30);

    private readonly ISettingsService _settingsService;
    private readonly AndroidActivityProvider _activityProvider;
    private readonly ILocalizationService _localizationService;
    private readonly ReviewPromptFallbackHandler _fallbackHandler;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _scheduledThisProcess;

    public AndroidReviewPromptService(
        ISettingsService settingsService,
        AndroidActivityProvider activityProvider,
        ILocalizationService localizationService,
        ReviewPromptFallbackHandler fallbackHandler)
    {
        _settingsService = settingsService;
        _activityProvider = activityProvider;
        _localizationService = localizationService;
        _fallbackHandler = fallbackHandler;
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
                // Google handles deduplication and may decide whether to show UI.
                settings.ReviewPromptCompletedAtUtc = DateTime.UtcNow;
                settings.ReviewPromptDismissed = true;
            }
            else
            {
                // 1st fallback: Avalonia bottom sheet (registered by MainView).
                // 2nd fallback: native Android dialog.
                ReviewPromptFallbackResult fallbackResult;
                if (_fallbackHandler.HasHandler)
                {
                    var sharedResult = await _fallbackHandler.ShowAsync(cancellationToken);
                    fallbackResult = sharedResult switch
                    {
                        ReviewPromptResult.RateNow => ReviewPromptFallbackResult.RateNow,
                        ReviewPromptResult.Never => ReviewPromptFallbackResult.Never,
                        _ => ReviewPromptFallbackResult.Later
                    };
                }
                else
                {
                    fallbackResult = await ShowFallbackReviewPromptAsync(activity, cancellationToken);
                }

                switch (fallbackResult)
                {
                    case ReviewPromptFallbackResult.RateNow:
                        if (TryOpenPlayStoreListing(activity))
                        {
                            settings.ReviewPromptCompletedAtUtc = DateTime.UtcNow;
                            settings.ReviewPromptDismissed = true;
                        }
                        else
                        {
                            settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.Add(SnoozeDuration);
                        }

                        break;
                    case ReviewPromptFallbackResult.Never:
                        settings.ReviewPromptDismissed = true;
                        break;
                    case ReviewPromptFallbackResult.Later:
                    default:
                        settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.Add(SnoozeDuration);
                        break;
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

    private async Task<bool> LaunchInAppReviewAsync(Activity activity, CancellationToken cancellationToken)
    {
        try
        {
            var manager = global::Google.Android.Play.Core.Review.ReviewManagerFactory.Create(activity);
            var request = manager.RequestReviewFlow();

            var deadline = DateTime.UtcNow.Add(ReviewFlowTimeout);
            while (!request.IsComplete)
            {
                if (cancellationToken.IsCancellationRequested || DateTime.UtcNow > deadline)
                {
                    return false;
                }

                await Task.Delay(100, cancellationToken);
            }

            if (!request.IsSuccessful)
            {
                Debug.WriteLine("[ReviewPrompt] Review request was not successful");
                return false;
            }

            if (request.Result is not global::Google.Android.Play.Core.Review.ReviewInfo reviewInfo)
            {
                Debug.WriteLine("[ReviewPrompt] Review request returned an unexpected result type");
                return false;
            }

            var flow = manager.LaunchReviewFlow(activity, reviewInfo);
            deadline = DateTime.UtcNow.Add(ReviewFlowTimeout);

            while (!flow.IsComplete)
            {
                if (cancellationToken.IsCancellationRequested || DateTime.UtcNow > deadline)
                {
                    return false;
                }

                await Task.Delay(100, cancellationToken);
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

    private Task<ReviewPromptFallbackResult> ShowFallbackReviewPromptAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ReviewPromptFallbackResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetResult(ReviewPromptFallbackResult.Later);
            return completion.Task;
        }

        activity.RunOnUiThread(() =>
        {
            try
            {
                var dialog = new Dialog(activity);
                dialog.Window?.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
                dialog.SetCanceledOnTouchOutside(true);

                dialog.SetContentView(BuildFallbackDialogView(activity, dialog, completion));
                dialog.CancelEvent += (_, _) => completion.TrySetResult(ReviewPromptFallbackResult.Later);
                dialog.DismissEvent += (_, _) => completion.TrySetResult(ReviewPromptFallbackResult.Later);
                dialog.Show();

                var window = dialog.Window;
                if (window is not null)
                {
                    window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
                    window.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                    window.SetGravity(GravityFlags.Bottom | GravityFlags.CenterHorizontal);
                    window.SetDimAmount(0.55f);
                    window.AddFlags(WindowManagerFlags.DimBehind);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ReviewPrompt] Failed to show fallback prompt: {ex.Message}");
                completion.TrySetResult(ReviewPromptFallbackResult.Later);
            }
        });

        cancellationToken.Register(() => completion.TrySetResult(ReviewPromptFallbackResult.Later));
        return completion.Task;
    }

    private LinearLayout BuildFallbackDialogView(
        Context context,
        Dialog dialog,
        TaskCompletionSource<ReviewPromptFallbackResult> completion)
    {
        var root = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical
        };
        root.SetPadding(Dp(context, 22), Dp(context, 22), Dp(context, 22), Dp(context, 18));
        root.Background = CreateRoundedBackground(
            Color.ParseColor("#151515"),
            Dp(context, 24),
            Color.ParseColor("#333333"),
            Dp(context, 1));

        var rootParams = new ViewGroup.MarginLayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        rootParams.SetMargins(Dp(context, 14), 0, Dp(context, 14), Dp(context, 14));
        root.LayoutParameters = rootParams;

        var header = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        root.AddView(header);

        var iconFrame = new FrameLayout(context);
        iconFrame.Background = CreateRoundedBackground(Color.ParseColor("#268B5CF6"), Dp(context, 18));
        header.AddView(iconFrame, new LinearLayout.LayoutParams(Dp(context, 48), Dp(context, 48)));

        var icon = new TextView(context)
        {
            Text = "☆",
            TextSize = 30,
            Gravity = GravityFlags.Center
        };
        icon.SetTextColor(Color.ParseColor("#B7A6F6"));
        iconFrame.AddView(icon, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        var title = new TextView(context)
        {
            Text = _localizationService.GetString("ReviewPrompt.Title"),
            TextSize = 20,
            Typeface = Typeface.DefaultBold
        };
        title.SetTextColor(Color.White);
        var titleParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        titleParams.SetMargins(Dp(context, 14), 0, 0, 0);
        header.AddView(title, titleParams);

        var message = new TextView(context)
        {
            Text = _localizationService.GetString("ReviewPrompt.Message.Android"),
            TextSize = 14
        };
        message.SetTextColor(Color.ParseColor("#E5E5E5"));
        message.SetLineSpacing(Dp(context, 2), 1.0f);
        var messageParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        messageParams.SetMargins(0, Dp(context, 16), 0, Dp(context, 18));
        root.AddView(message, messageParams);

        root.AddView(CreateActionButton(
            context,
            _localizationService.GetString("ReviewPrompt.Action.Rate"),
            primary: true,
            () =>
            {
                completion.TrySetResult(ReviewPromptFallbackResult.RateNow);
                dialog.Dismiss();
            }));

        root.AddView(CreateActionButton(
            context,
            _localizationService.GetString("ReviewPrompt.Action.Later"),
            primary: false,
            () =>
            {
                completion.TrySetResult(ReviewPromptFallbackResult.Later);
                dialog.Dismiss();
            }));

        root.AddView(CreateQuietButton(
            context,
            _localizationService.GetString("ReviewPrompt.Action.Never"),
            () =>
            {
                completion.TrySetResult(ReviewPromptFallbackResult.Never);
                dialog.Dismiss();
            }));

        return root;
    }

    private static TextView CreateActionButton(Context context, string text, bool primary, Action action)
    {
        var button = new TextView(context)
        {
            Text = text,
            TextSize = 15,
            Typeface = Typeface.DefaultBold,
            Gravity = GravityFlags.Center
        };
        button.SetTextColor(Color.White);
        button.SetPadding(Dp(context, 16), Dp(context, 12), Dp(context, 16), Dp(context, 12));
        button.Background = CreateRoundedBackground(
            primary ? Color.ParseColor("#8B5CF6") : Color.ParseColor("#242424"),
            Dp(context, 12),
            primary ? Color.ParseColor("#8B5CF6") : Color.ParseColor("#333333"),
            Dp(context, 1));
        button.Click += (_, _) => action();

        var parameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        parameters.SetMargins(0, 0, 0, Dp(context, 10));
        button.LayoutParameters = parameters;
        return button;
    }

    private static TextView CreateQuietButton(Context context, string text, Action action)
    {
        var button = new TextView(context)
        {
            Text = text,
            TextSize = 14,
            Gravity = GravityFlags.Center
        };
        button.SetTextColor(Color.ParseColor("#A7A7CC"));
        button.SetPadding(Dp(context, 16), Dp(context, 10), Dp(context, 16), Dp(context, 10));
        button.Click += (_, _) => action();
        return button;
    }

    private bool TryOpenPlayStoreListing(Activity activity)
    {
        var packageName = activity.PackageName;
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return false;
        }

        return TryStartReviewIntent(activity, $"market://details?id={packageName}", "com.android.vending") ||
               TryStartReviewIntent(activity, $"market://details?id={packageName}", packageName: null) ||
               TryStartReviewIntent(activity, $"https://play.google.com/store/apps/details?id={packageName}", packageName: null);
    }

    private static bool TryStartReviewIntent(Activity activity, string uri, string? packageName)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri));
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                intent.SetPackage(packageName);
            }

            intent.AddFlags(ActivityFlags.NewTask);
            activity.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReviewPrompt] Failed to open review target: {ex.Message}");
            return false;
        }
    }

    private static GradientDrawable CreateRoundedBackground(Color color, int radius)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(radius);
        return drawable;
    }

    private static GradientDrawable CreateRoundedBackground(Color color, int radius, Color strokeColor, int strokeWidth)
    {
        var drawable = CreateRoundedBackground(color, radius);
        drawable.SetStroke(strokeWidth, strokeColor);
        return drawable;
    }

    private static int Dp(Context context, int value)
        => (int)Math.Round(value * context.Resources!.DisplayMetrics!.Density);

    private enum ReviewPromptFallbackResult
    {
        Later,
        Never,
        RateNow
    }
}
