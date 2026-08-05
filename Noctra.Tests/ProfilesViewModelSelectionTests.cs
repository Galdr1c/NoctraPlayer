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

    [Fact]
    public async Task SelectProfile_ConcurrentCall_IgnoredWhileFirstPending()
    {
        var profileService = new Mock<IProfileService>();
        var viewModel = CreateViewModel(profileService.Object);

        // PinPrompt bekleyen bir TCS döndürür — PIN akışı asılı kalır.
        var pinGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pinPromptCalls = 0;
        viewModel.PinPrompt = (_, _) =>
        {
            pinPromptCalls++;
            return pinGate.Task;
        };

        var profile = new Profile { Id = 1, Name = "Locked", PinHash = "hash" };
        Profile? selectedProfile = null;
        viewModel.OnProfileSelected += (selected, _) => selectedProfile = selected;

        // İlk çağrı PIN kapısında beklerken gelen ikinci çağrı yutulmalı:
        // yeni PIN akışı başlamamalı, seçim yayınlanmamalı.
        var first = viewModel.SelectProfileCommand.ExecuteAsync(profile);
        Assert.Equal(1, pinPromptCalls);

        var second = viewModel.SelectProfileCommand.ExecuteAsync(profile);
        await second;

        Assert.Equal(1, pinPromptCalls);
        Assert.Null(selectedProfile);

        // Kapı açılınca ilk akış tek grant ile tamamlanır.
        pinGate.TrySetResult(true);
        await first;

        Assert.Equal(1, pinPromptCalls);
        Assert.Same(profile, selectedProfile);
    }

    [Fact]
    public async Task SelectProfile_GuardResetsAfterFlowCompletes()
    {
        var profileService = new Mock<IProfileService>();
        var viewModel = CreateViewModel(profileService.Object);

        var pinPromptCalls = 0;
        viewModel.PinPrompt = (_, _) =>
        {
            pinPromptCalls++;
            return Task.FromResult(true);
        };

        var profile = new Profile { Id = 1, Name = "Locked", PinHash = "hash" };
        var selectionCount = 0;
        viewModel.OnProfileSelected += (_, _) => selectionCount++;

        // İlk akış tamamlandıktan sonra koruma sıfırlanır — ardışık seçim çalışır.
        await viewModel.SelectProfileCommand.ExecuteAsync(profile);
        await viewModel.SelectProfileCommand.ExecuteAsync(profile);

        Assert.Equal(2, pinPromptCalls);
        Assert.Equal(2, selectionCount);
    }

    [Fact]
    public async Task DeleteProfile_ConcurrentCall_IgnoredWhileFirstPending()
    {
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(service => service.DeleteProfileAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ProfileAccessGrant>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var dialogService = new Mock<IDialogService>();
        var confirmGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialogService
            .Setup(service => service.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(confirmGate.Task);

        var localizationService = new Mock<ILocalizationService>();
        localizationService
            .Setup(service => service.GetString(It.IsAny<string>()))
            .Returns("");

        var viewModel = CreateViewModel(profileService.Object, dialogService.Object, localizationService.Object);
        var profile = new Profile { Id = 3, Name = "Doomed" };

        var first = viewModel.DeleteProfileCommand.ExecuteAsync(profile);
        var second = viewModel.DeleteProfileCommand.ExecuteAsync(profile);
        await second;

        // İlk akış konfirmasyonda beklerken ikinci çağrı yeni konfirmasyon açamaz.
        dialogService.Verify(
            service => service.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);

        confirmGate.TrySetResult(true);
        await first;

        profileService.Verify(
            service => service.DeleteProfileAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ProfileAccessGrant>()),
            Times.Once);
    }

    [Fact]
    public async Task EditProfile_ConcurrentCall_IgnoredWhileFirstPending()
    {
        var profileService = new Mock<IProfileService>();
        var viewModel = CreateViewModel(profileService.Object);

        var pinGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pinPromptCalls = 0;
        viewModel.PinPrompt = (_, _) =>
        {
            pinPromptCalls++;
            return pinGate.Task;
        };

        var profile = new Profile { Id = 4, Name = "Locked", PinHash = "hash" };
        ProfileAccessGrant? lastGrant = null;
        viewModel.OnProfileEditRequested += (_, grant) => lastGrant = grant;

        var first = viewModel.EditProfileCommand.ExecuteAsync(profile);
        Assert.Equal(1, pinPromptCalls);

        var second = viewModel.EditProfileCommand.ExecuteAsync(profile);
        await second;

        Assert.Equal(1, pinPromptCalls);
        Assert.Null(lastGrant);

        pinGate.TrySetResult(true);
        await first;

        Assert.Equal(1, pinPromptCalls);
        Assert.NotNull(lastGrant);
    }

    private static ProfilesViewModel CreateViewModel(IProfileService profileService)
    {
        return CreateViewModel(profileService, Mock.Of<IDialogService>());
    }

    private static ProfilesViewModel CreateViewModel(IProfileService profileService, IDialogService dialogService)
    {
        return CreateViewModel(profileService, dialogService, Mock.Of<ILocalizationService>());
    }

    private static ProfilesViewModel CreateViewModel(
        IProfileService profileService,
        IDialogService dialogService,
        ILocalizationService localizationService)
    {
        return new ProfilesViewModel(
            profileService,
            new ProfileAccessService(),
            dialogService,
            Mock.Of<IDispatcherService>(),
            Mock.Of<ILicenseService>(),
            localizationService);
    }
}
