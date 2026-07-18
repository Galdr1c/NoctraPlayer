using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class ProfilesViewModelSelectionTests
{
    [Fact]
    public async Task SelectProfile_PublishesSelectionBeforeLastUsedPersistenceCompletes()
    {
        var updateStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(service => service.UpdateLastUsedAsync(42))
            .Returns(async () =>
            {
                updateStarted.TrySetResult(true);
                await releaseUpdate.Task;
            });

        var viewModel = CreateViewModel(profileService.Object);
        var profile = new Profile { Id = 42, Name = "Large M3U" };
        Profile? selectedProfile = null;
        var closeRequested = false;
        viewModel.OnProfileSelected += selected => selectedProfile = selected;
        viewModel.RequestClose += () => closeRequested = true;

        var selectionTask = viewModel.SelectProfileCommand.ExecuteAsync(profile);

        try
        {
            await updateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Yield();

            Assert.Same(profile, selectedProfile);
            Assert.True(closeRequested);
            Assert.True(selectionTask.IsCompleted);
        }
        finally
        {
            releaseUpdate.TrySetResult(true);
            await selectionTask;
        }
    }

    [Fact]
    public async Task SelectProfile_LastUsedPersistenceFailureDoesNotPreventSelection()
    {
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(service => service.UpdateLastUsedAsync(7))
            .Returns(Task.FromException(new InvalidOperationException("database busy")));

        var viewModel = CreateViewModel(profileService.Object);
        var profile = new Profile { Id = 7, Name = "M3U" };
        Profile? selectedProfile = null;
        viewModel.OnProfileSelected += selected => selectedProfile = selected;

        await viewModel.SelectProfileCommand.ExecuteAsync(profile);

        Assert.Same(profile, selectedProfile);
    }

    private static ProfilesViewModel CreateViewModel(IProfileService profileService)
    {
        return new ProfilesViewModel(
            profileService,
            Mock.Of<IDialogService>(),
            Mock.Of<IDispatcherService>(),
            Mock.Of<ILicenseService>(),
            Mock.Of<ILocalizationService>());
    }
}
