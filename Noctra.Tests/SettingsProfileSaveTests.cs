using Noctra.Models;
using Noctra.Services;

namespace Noctra.Tests;

public class SettingsProfileSaveTests
{
    [Fact]
    public async Task SaveForActiveProfileAsync_WhenServiceTargetsAnotherProfile_LoadsActiveProfileBeforeApplyingChanges()
    {
        var service = new RecordingSettingsService(profileId: 1);

        var saved = await Noctra.ViewModels.SettingsViewModel.SaveForActiveProfileAsync(
            service,
            activeProfileId: 2,
            settings =>
            {
                settings.SaveWatchHistory = false;
                settings.ClearHistoryOnExit = true;
                settings.WatchHistoryRetentionDays = 14;
            });

        Assert.True(saved);
        Assert.Equal(new[] { 2 }, service.LoadedProfileIds);
        Assert.Equal(2, service.Settings.ProfileId);
        Assert.False(service.Settings.SaveWatchHistory);
        Assert.True(service.Settings.ClearHistoryOnExit);
        Assert.Equal(14, service.Settings.WatchHistoryRetentionDays);
        Assert.Equal(1, service.SaveCount);
    }

    private sealed class RecordingSettingsService : ISettingsService
    {
        public RecordingSettingsService(int profileId)
        {
            Settings = new AppSettings { ProfileId = profileId };
        }

        public AppSettings Settings { get; private set; }
        public List<int> LoadedProfileIds { get; } = [];
        public int SaveCount { get; private set; }
        public event Action? SettingsChanged;

        public Task LoadAsync() => LoadProfileSettingsAsync(0);

        public Task LoadProfileSettingsAsync(int profileId)
        {
            LoadedProfileIds.Add(profileId);
            Settings = new AppSettings { ProfileId = profileId };
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task<AppSettings?> PeekProfileSettingsAsync(int profileId) =>
            Task.FromResult<AppSettings?>(new AppSettings { ProfileId = profileId });

        public Task SaveAsync()
        {
            SaveCount++;
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }
        public void NotifySettingsChanged() => SettingsChanged?.Invoke();

        public void ResetToDefaults()
        {
        }

        public Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds) =>
            Task.FromResult(0);
    }
}
