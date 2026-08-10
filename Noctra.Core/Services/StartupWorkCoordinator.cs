using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Core.Services;

/// <summary>
/// Startup'ta ağır fire-and-forget işlerin zamanlamasını yönetir.
/// Yeni bir profil açıldığında (ilk kare, kanal yükleme, EPG, history rayları,
/// enrichment...) onlarca paralel SQLite/HTTP görevi aynı anda yarışabilir.
/// Bu koordinatör işleri aşamalara ayırır ve arka plan aşamasını küçük bir
/// concurrency penceresiyle sınırlar:
///   Critical      → hemen çalışır (şema fixup, profil restore).
///   AfterFirstFrame → ilk kare çizilene kadar bekler (EPG sync gibi).
///   Background    → ilk kareyi bekler + dar concurrency penceresi (varsayılan 2).
/// İlk kare sinyali gelmezse bile işler <see cref="FirstFrameTimeout"/> sonrası
/// asla takılmaz.
/// </summary>
public enum StartupWorkStage
{
    /// <summary>
    /// İlk kareyi beklemeden hemen çalışır (şema fixup, profil restore).
    /// </summary>
    Critical,

    /// <summary>
    /// İlk kare çizilene kadar ertelenir; çalıştığı thread çağıranın thread'idir.
    /// </summary>
    AfterFirstFrame,

    /// <summary>
    /// İlk kare çizilene kadar ertelenir ve küçük bir arka plan concurrency
    /// penceresiyle (varsayılan 2) sınırlanır.
    /// </summary>
    Background
}

public sealed class StartupWorkCoordinator
{
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _backgroundGate;
    private readonly object _sync = new();
    private readonly Queue<Func<Task>> _pendingBackground = new();
    private readonly TaskCompletionSource<bool> _firstFrameRendered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _drainerActive;

    /// <summary>
    /// İlk kare zaten çizilmiş sayılan örnek. DI dışında (testler, desktop
    /// fallback) kullanılır; Background işlerini hemen serbest bırakır.
    /// </summary>
    public static StartupWorkCoordinator Immediate { get; } = CreateImmediate();

    public StartupWorkCoordinator(int backgroundConcurrency = 2)
    {
        _backgroundGate = new SemaphoreSlim(
            Math.Max(1, backgroundConcurrency),
            Math.Max(1, backgroundConcurrency));
    }

    /// <summary>
    /// İlk kare çizildiğinde MainActivity tarafından çağrılır.
    /// </summary>
    public void MarkFirstFrameRendered()
    {
        _firstFrameRendered.TrySetResult(true);
    }

    /// <summary>
    /// Verilen işi aşamasına göre zamanlar. Dönen Task iş gerçekten
    /// tamamlandığında tamamlanır; fire-and-forget çağrılarda await'lemeyin.
    /// </summary>
    public Task RunAsync(
        string name,
        Func<Task> work,
        StartupWorkStage stage = StartupWorkStage.Background)
    {
        switch (stage)
        {
            case StartupWorkStage.AfterFirstFrame:
                return RunAfterFirstFrameAsync(name, work);
            case StartupWorkStage.Background:
                return RunBackgroundAsync(name, work);
            default:
                return ExecuteSafelyAsync(name, work);
        }
    }

    private async Task RunAfterFirstFrameAsync(string name, Func<Task> work)
    {
        await WaitForFirstFrameAsync().ConfigureAwait(false);
        await ExecuteSafelyAsync(name, work).ConfigureAwait(false);
    }

    private Task RunBackgroundAsync(string name, Func<Task> work)
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_sync)
        {
            _pendingBackground.Enqueue(async () =>
            {
                try
                {
                    await WaitForFirstFrameAsync().ConfigureAwait(false);
                    await _backgroundGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await work().ConfigureAwait(false);
                    }
                    finally
                    {
                        _backgroundGate.Release();
                    }

                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

            if (_drainerActive == 0)
            {
                _drainerActive = 1;
                Task.Run(DrainBackgroundQueueAsync);
            }
        }

        return completion.Task;
    }

    private async Task DrainBackgroundQueueAsync()
    {
        while (true)
        {
            Func<Task>? next;
            lock (_sync)
            {
                if (_pendingBackground.Count == 0)
                {
                    _drainerActive = 0;
                    return;
                }

                next = _pendingBackground.Dequeue();
            }

            await ExecuteSafelyAsync("background-queue", next).ConfigureAwait(false);
        }
    }

    private async Task WaitForFirstFrameAsync()
    {
        if (_firstFrameRendered.Task.IsCompleted)
        {
            return;
        }

        // İlk kare sinyali gelmezse (ör. signal yolu başarısız oldu) işler
        // sonsuza dek beklemesin.
        await Task.WhenAny(
            _firstFrameRendered.Task,
            Task.Delay(FirstFrameTimeout)).ConfigureAwait(false);
    }

    private static async Task ExecuteSafelyAsync(string name, Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Arka plan işleri asla sessizce ölmesin; yine de kuyruk drain'ini
            // bozmasın. Hata zaten çağıran tarafın içinde loglanmış olmalı.
        }
    }

    private static StartupWorkCoordinator CreateImmediate()
    {
        var instance = new StartupWorkCoordinator();
        instance.MarkFirstFrameRendered();
        return instance;
    }
}
