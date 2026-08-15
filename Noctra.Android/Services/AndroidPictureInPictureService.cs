using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Noctra.Android;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidPictureInPictureService : IPictureInPictureService
{
    // Fixed request codes for PiP action PendingIntents.
    // String hashes may not be stable across processes; constants are deterministic.
    private const int RequestPlayPause = 1001;
    private const int RequestPreviousLive = 1002;
    private const int RequestNextLive = 1003;
    private const int RequestNextEpisode = 1004;

    private readonly AndroidActivityProvider _activityProvider;
    private readonly ILocalizationService _localizationService;
    private PictureInPicturePlaybackState _state = new();

    public AndroidPictureInPictureService(
        AndroidActivityProvider activityProvider,
        ILocalizationService localizationService)
    {
        _activityProvider = activityProvider;
        _localizationService = localizationService;
    }

    public event EventHandler<PictureInPictureModeChangedEventArgs>? PictureInPictureModeChanged;

    public bool IsSupported => Build.VERSION.SdkInt >= BuildVersionCodes.O;

    public bool IsInPictureInPictureMode => _activityProvider.CurrentActivity?.IsInPictureInPictureMode == true;

    public async Task<bool> EnterPictureInPictureAsync()
    {
        var activity = _activityProvider.CurrentActivity;
        if (!IsSupported || activity is null || !_state.CanEnterPictureInPicture)
        {
            return false;
        }

        // Android 12+ may complete auto-enter before the explicit OEM fallback
        // resumes. Treat that race as success instead of entering a second time.
        if (IsInPictureInPictureMode)
        {
            return true;
        }

        var parameters = BuildParams(autoEnterEnabled: false);
        if (parameters is null)
        {
            return false;
        }

        var mainActivity = activity as MainActivity;
        if (mainActivity is not null)
        {
            await mainActivity.PrepareVideoSurfaceForPictureInPictureAsync();
        }

        try
        {
            var entered = activity.EnterPictureInPictureMode(parameters);
            if (!entered)
            {
                mainActivity?.RestoreAvaloniaSurfaceAfterFailedPictureInPictureEntry();
            }

            return entered;
        }
        catch
        {
            mainActivity?.RestoreAvaloniaSurfaceAfterFailedPictureInPictureEntry();
            return false;
        }
    }

    public Task<bool> TryEnterAutoPictureInPictureAsync()
    {
        if (!_state.CanEnterPictureInPicture || !_state.IsPlaying)
        {
            return Task.FromResult(false);
        }

        // SetAutoEnterEnabled provides the smooth Android 12+ transition, but
        // some OEM/navigation combinations still reach OnUserLeaveHint without
        // entering PiP. Keep this idempotent explicit fallback on every version.
        if (IsInPictureInPictureMode)
        {
            return Task.FromResult(true);
        }

        return EnterPictureInPictureAsync();
    }

    public void UpdatePictureInPictureState(PictureInPicturePlaybackState state)
    {
        _state = state ?? new PictureInPicturePlaybackState();

        var activity = _activityProvider.CurrentActivity;
        if (!IsSupported || activity is null)
        {
            return;
        }

        // Auto-enter yalnızca premium kullanıcılarda aktiftir; CanEnterPictureInPicture
        // MainView tarafında premium kontrolüyle gated edilir (ücretsiz tier'da
        // false gelir, SetAutoEnterEnabled(false) kalır → video arka planda durur).
        var parameters = BuildParams(autoEnterEnabled: _state.CanEnterPictureInPicture && _state.IsPlaying);
        if (parameters is null)
        {
            return;
        }

        try
        {
            activity.SetPictureInPictureParams(parameters);
        }
        catch
        {
            // PiP params are best-effort. Some OEM builds can throw while activity is finishing.
        }
    }

    public void NotifyPictureInPictureModeChanged(bool isInPictureInPictureMode)
    {
        PictureInPictureModeChanged?.Invoke(
            this,
            new PictureInPictureModeChangedEventArgs(isInPictureInPictureMode));
    }

    private PictureInPictureParams? BuildParams(bool autoEnterEnabled)
    {
        if (!IsSupported)
        {
            return null;
        }

        var builder = new PictureInPictureParams.Builder()
            .SetAspectRatio(new Rational(16, 9));

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            builder.SetAutoEnterEnabled(autoEnterEnabled);
            builder.SetSeamlessResizeEnabled(true);
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (!string.IsNullOrWhiteSpace(_state.Title))
            {
                builder.SetTitle(new Java.Lang.String(_state.Title));
            }

            if (!string.IsNullOrWhiteSpace(_state.Subtitle))
            {
                builder.SetSubtitle(new Java.Lang.String(_state.Subtitle));
            }
        }

        var actions = BuildRemoteActions();
        if (actions.Count > 0)
        {
            builder.SetActions(actions);
        }

        return builder.Build();
    }

    private IList<RemoteAction> BuildRemoteActions()
    {
        var actions = new List<RemoteAction>(3);

        if (_state.IsLiveContent)
        {
            actions.Add(CreateAction(
                AndroidPictureInPictureActionReceiver.ControlPreviousLive,
                global::Android.Resource.Drawable.IcMediaPrevious,
                Localize("Player.Mobile.Previous", "Previous"),
                Localize("Player.Overlay.PreviousChannel", "Previous channel")));
        }

        actions.Add(CreateAction(
            AndroidPictureInPictureActionReceiver.ControlPlayPause,
            _state.IsPlaying ? global::Android.Resource.Drawable.IcMediaPause : global::Android.Resource.Drawable.IcMediaPlay,
            _state.IsPlaying ? Localize("Player.Overlay.Pause", "Pause") : Localize("Player.Overlay.Play", "Play"),
            Localize("Player.Overlay.PlayPause.Tooltip", "Play / pause")));

        if (_state.IsLiveContent)
        {
            actions.Add(CreateAction(
                AndroidPictureInPictureActionReceiver.ControlNextLive,
                global::Android.Resource.Drawable.IcMediaNext,
                Localize("Player.Mobile.Next", "Next"),
                Localize("Player.Overlay.NextChannel", "Next channel")));
        }
        else if (_state.IsSeriesContent)
        {
            actions.Add(CreateAction(
                AndroidPictureInPictureActionReceiver.ControlNextEpisode,
                global::Android.Resource.Drawable.IcMediaNext,
                Localize("Player.Mobile.Next", "Next"),
                Localize("Player.Overlay.NextEpisode", "Next episode")));
        }

        return actions;
    }

    private string Localize(string key, string fallback)
    {
        var value = _localizationService.GetString(key);
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private RemoteAction CreateAction(string control, int drawableResource, string title, string description)
    {
        var activity = _activityProvider.CurrentActivity;
        var context = activity?.ApplicationContext ?? activity ?? throw new InvalidOperationException("Android activity is unavailable.");
        var intent = new Intent(context, typeof(AndroidPictureInPictureActionReceiver));
        intent.SetAction(AndroidPictureInPictureActionReceiver.ActionControl);
        intent.PutExtra(AndroidPictureInPictureActionReceiver.ExtraControl, control);

        var requestCode = control switch
        {
            AndroidPictureInPictureActionReceiver.ControlPlayPause => RequestPlayPause,
            AndroidPictureInPictureActionReceiver.ControlPreviousLive => RequestPreviousLive,
            AndroidPictureInPictureActionReceiver.ControlNextLive => RequestNextLive,
            AndroidPictureInPictureActionReceiver.ControlNextEpisode => RequestNextEpisode,
            _ => control.GetHashCode(StringComparison.Ordinal)
        };
        var pendingIntent = PendingIntent.GetBroadcast(
            context,
            requestCode,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new RemoteAction(
            Icon.CreateWithResource(context, drawableResource),
            new Java.Lang.String(title),
            new Java.Lang.String(description),
            pendingIntent);
    }
}
