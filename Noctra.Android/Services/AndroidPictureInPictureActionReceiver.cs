using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Noctra.ViewModels;

namespace Noctra.Android.Services;

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class AndroidPictureInPictureActionReceiver : BroadcastReceiver
{
    public const string ActionControl = "studio.kynora.noctra.PICTURE_IN_PICTURE_CONTROL";
    public const string ExtraControl = "control";
    public const string ControlPlayPause = "play_pause";
    public const string ControlPreviousLive = "previous_live";
    public const string ControlNextLive = "next_live";
    public const string ControlNextEpisode = "next_episode";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != ActionControl)
        {
            return;
        }

        if (Avalonia.Application.Current is not Noctra.Mobile.App app || app.Services is null)
        {
            return;
        }

        var player = app.Services.GetService<PlayerViewModel>();
        if (player is null)
        {
            return;
        }

        switch (intent.GetStringExtra(ExtraControl))
        {
            case ControlPlayPause:
                player.PlayPauseCommand.Execute(null);
                break;
            case ControlPreviousLive:
                player.PlayPreviousLiveChannelCommand.Execute(null);
                break;
            case ControlNextLive:
                player.PlayNextLiveChannelCommand.Execute(null);
                break;
            case ControlNextEpisode:
                player.PlayNextEpisodeCommand.Execute(null);
                break;
        }
    }
}
