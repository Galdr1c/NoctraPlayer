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
        viewModel.OnProfileSelected += (selected, _) => selectedProfile = selected;
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
        viewModel.OnProfileSelected += (selected, _) => selectedProfile = selected;

        await viewModel.SelectProfileCommand.ExecuteAsync(profile);

        Assert.Same(profile, selectedProfile);
    }

    [Fact]
    public async Task SelectProfile_PendingDeletion_RecoversOnlyAfterPinGate()
    {
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(service => service.CancelProfileDeletionAsync(42))
            .Returns(Task.CompletedTask)
            .Verifiable();
        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(service => service.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var viewModel = CreateViewModel(profileService.Object, dialogService.Object);
        var profile = new Profile { Id = 42, Name = "Doomed", PendingDeletionAt = DateTime.UtcNow };
        Profile? selectedProfile = null;
        viewModel.OnProfileSelected += (selected, _) => selectedProfile = selected;

        await viewModel.SelectProfileCommand.ExecuteAsync(profile);

        profileService.Verify(service => service.CancelProfileDeletionAsync(42), Times.Once);
        Assert.Same(profile, selectedProfile);
    }

    [Fact]
    public async Task SelectProfile_PinProtectedPendingDeletion_WrongPin_DoesNotRecover()
    {
        var profileService = new Mock<IProfileService>();
        var dialogService = new Mock<IDialogService>();

        var viewModel = CreateViewModel(profileService.Object, dialogService.Object);
        viewModel.PinPrompt = (_, _) => Task.FromResult(false);

        var profile = new Profile
        {
            Id = 9,
            Name = "Doomed",
            PinHash = "hash",
            PendingDeletionAt = DateTime.UtcNow
        };
        Profile? selectedProfile = null;
        viewModel.OnProfileSelected += (selected, _) => selectedProfile = selected;

        await viewModel.SelectProfileCommand.ExecuteAsync(profile);

        profileService.Verify(service => service.CancelProfileDeletionAsync(It.IsAny<int>()), Times.Never);
        Assert.Null(selectedProfile);
    }

    private static ProfilesViewModel CreateViewModel(IProfileService profileService)
    {
        return CreateViewModel(profileService, Mock.Of<IDialogService>());
    }

    private static ProfilesViewModel CreateViewModel(IProfileService profileService, IDialogService dialogService)
    {
        return new ProfilesViewModel(
            profileService,
            new ProfileAccessService(),
            dialogService,
            Mock.Of<IDispatcherService>(),
            Mock.Of<ILicenseService>(),
            Mock.Of<ILocalizationService>());
    }
}
