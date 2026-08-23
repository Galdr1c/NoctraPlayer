using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.Session;
using Microsoft.Extensions.DependencyInjection;

namespace Noctra.Android.Services;

[Service(
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter([AndroidX.Media3.Session.MediaSessionService.ServiceInterface])]
public sealed class NoctraPlaybackService : MediaSessionService
{
    private static readonly object LifecycleGate = new();
    private static TaskCompletionSource _readySource = CreateReadySource();
    private static TaskCompletionSource _stoppedSource = CreateCompletedSource();
    private static bool _isReady;

    private AndroidVideoPlayerService? _videoPlayerService;
    private MediaSession? _mediaSession;

    public static async Task EnsureStartedAsync(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Task readyTask;
        lock (LifecycleGate)
        {
            if (_isReady)
            {
                return;
            }

            readyTask = _readySource.Task;
        }

        var intent = new Intent(context, typeof(NoctraPlaybackService));
        if (context.StartService(intent) is null)
        {
            throw new InvalidOperationException("Android could not start the playback service.");
        }

        await readyTask
            .WaitAsync(TimeSpan.FromSeconds(8))
            .ConfigureAwait(false);
    }

    public static async Task StopPlaybackServiceAsync(
        Context context,
        CancellationToken cancellationToken = default)
    {
        Task stoppedTask;

        lock (LifecycleGate)
        {
            if (!_isReady)
                return;

            _stoppedSource = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            stoppedTask = _stoppedSource.Task;
        }

        context.StopService(
            new Intent(context, typeof(NoctraPlaybackService)));

        await stoppedTask
            .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
            .ConfigureAwait(false);
    }

    public override void OnCreate()
    {
        base.OnCreate();

        try
        {
            var nativeApplication = Application as global::Noctra.Android.Application
                ?? throw new InvalidOperationException("Noctra Android application is unavailable.");

            _videoPlayerService = nativeApplication.Services
                .GetRequiredService<AndroidVideoPlayerService>();
            _videoPlayerService.PlayerChanged += OnPlayerChanged;

            var player = _videoPlayerService.AttachPlaybackHost();
            _mediaSession = new MediaSession.Builder(this, player).Build();

            var notificationProvider =
                new DefaultMediaNotificationProvider.Builder(this).Build()
                ?? throw new InvalidOperationException("Media notification provider could not be created.");
            notificationProvider.SetSmallIcon(Resource.Drawable.ic_notification_noctra);
            SetMediaNotificationProvider(notificationProvider);

            lock (LifecycleGate)
            {
                _isReady = true;
                _readySource.TrySetResult();
            }
        }
        catch (Exception ex)
        {
            lock (LifecycleGate)
            {
                _readySource.TrySetException(ex);
            }

            throw;
        }
    }

    public override MediaSession? OnGetSession(MediaSession.ControllerInfo? controllerInfo)
        => _mediaSession;

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // Arka plan oynatma yok: task kalkınca oynatmayı durdur.
        PauseAllPlayersAndStopSelf();
    }

    public override void OnDestroy()
    {
        lock (LifecycleGate)
        {
            _isReady = false;
            _readySource = CreateReadySource();
            _stoppedSource.TrySetResult();
        }

        if (_videoPlayerService is not null)
        {
            _videoPlayerService.PlayerChanged -= OnPlayerChanged;
        }

        _mediaSession?.Release();
        _mediaSession?.Dispose();
        _mediaSession = null;

        _videoPlayerService?.DetachPlaybackHost();
        _videoPlayerService = null;

        base.OnDestroy();
    }

    private void OnPlayerChanged(IExoPlayer player)
    {
        if (_mediaSession is not null)
        {
            _mediaSession.Player = player;
        }
    }

    private static TaskCompletionSource CreateReadySource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource CreateCompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult();
        return source;
    }
}
