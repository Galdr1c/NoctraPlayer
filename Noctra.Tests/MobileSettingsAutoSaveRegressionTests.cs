using System.Reflection;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class MobileSettingsAutoSaveRegressionTests
{
    [Fact]
    public void MobileSettings_UsesViewModelAutoSaveAndDoesNotExposeManualSaveButton()
    {
        var viewModel = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        Assert.Contains("QueueAutoSave", viewModel, StringComparison.Ordinal);
        Assert.Contains("partial void OnAppLanguageChanged", viewModel, StringComparison.Ordinal);
        Assert.Contains("_localizationService.SetLanguage", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding SaveSettingsCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ResetToDefaultsCommand}\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoSaveCoordinator_CoalescesRapidRequestsIntoOneSave()
    {
        var coordinatorType = typeof(SettingsViewModel).Assembly.GetType(
            "Noctra.ViewModels.SettingsAutoSaveCoordinator");
        Assert.NotNull(coordinatorType);

        var saveCount = 0;
        Func<Task> save = () =>
        {
            Interlocked.Increment(ref saveCount);
            return Task.CompletedTask;
        };

        using var coordinator = (IDisposable?)Activator.CreateInstance(
            coordinatorType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [save],
            culture: null);
        Assert.NotNull(coordinator);

        var requestSave = coordinatorType!.GetMethod(
            "RequestSave",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(requestSave);

        requestSave!.Invoke(coordinator, [TimeSpan.FromMilliseconds(40)]);
        requestSave.Invoke(coordinator, [TimeSpan.FromMilliseconds(40)]);
        requestSave.Invoke(coordinator, [TimeSpan.FromMilliseconds(40)]);

        await Task.Delay(150);

        Assert.Equal(1, Volatile.Read(ref saveCount));
    }

    [Fact]
    public async Task AutoSaveCoordinator_SerializesSavesAndRunsTrailingLatestRequest()
    {
        var coordinatorType = typeof(SettingsViewModel).Assembly.GetType(
            "Noctra.ViewModels.SettingsAutoSaveCoordinator");
        Assert.NotNull(coordinatorType);

        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCount = 0;
        var concurrentSaves = 0;
        var peakConcurrentSaves = 0;

        async Task SaveAsync()
        {
            var current = Interlocked.Increment(ref concurrentSaves);
            UpdateMaximum(ref peakConcurrentSaves, current);
            var sequence = Interlocked.Increment(ref saveCount);
            if (sequence == 1)
            {
                firstSaveStarted.TrySetResult();
                await releaseFirstSave.Task;
            }

            Interlocked.Decrement(ref concurrentSaves);
        }

        using var coordinator = (IDisposable?)Activator.CreateInstance(
            coordinatorType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [(Func<Task>)SaveAsync],
            culture: null);
        Assert.NotNull(coordinator);

        var requestSave = coordinatorType!.GetMethod(
            "RequestSave",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(requestSave);

        requestSave!.Invoke(coordinator, [TimeSpan.Zero]);
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        requestSave.Invoke(coordinator, [TimeSpan.Zero]);
        requestSave.Invoke(coordinator, [TimeSpan.Zero]);
        releaseFirstSave.TrySetResult();

        await WaitUntilAsync(() => Volatile.Read(ref saveCount) == 2);

        Assert.Equal(2, Volatile.Read(ref saveCount));
        Assert.Equal(1, Volatile.Read(ref peakConcurrentSaves));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("The expected auto-save did not complete.");
            }

            await Task.Delay(10);
        }
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (candidate <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, candidate, current) != current);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
