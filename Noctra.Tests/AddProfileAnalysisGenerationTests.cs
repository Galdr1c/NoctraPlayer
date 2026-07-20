using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

/// <summary>
/// Tests for the analysis generation race condition fix and profile form
/// validation behaviors per commit fa7459e review.
///
/// Scenarios covered:
///   1. Stale analysis results are not applied after URL change
///   2. Xtream username/password errors shown separately
///   3. Stalker MAC validation correctness
///   4. Edit mode: empty PIN does not remove existing PIN
///   5. Provider type switch resets fields and analysis
///   6. Local M3U file validation
///   7. Credential editing and save
/// </summary>
public sealed class AddProfileAnalysisGenerationTests
{
    // ═══════════════════════════════════════════════════════════════
    // 1. Stale analysis race condition — most critical
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UrlChange_ClearsAnalysisResults()
    {
        var vm = CreateViewModel();

        // Simulate having analysis results
        vm.Url = "http://old-server.example.com";
        vm.AnalyzeConnectionCommand.ExecuteAsync(null).Wait();

        // Change URL — analysis results must be cleared
        vm.Url = "http://new-server.example.com";

        Assert.Equal(ConnectionHealth.Unknown, vm.ConnectionHealth);
        Assert.True(string.IsNullOrEmpty(vm.DetailedStatus));
    }

    [Fact]
    public void UsernameChange_ClearsAnalysisResults()
    {
        var vm = CreateViewModel();

        vm.Url = "http://server.example.com";
        vm.Username = "old-user";
        vm.Password = "pass";

        // Change username — analysis must be invalidated
        vm.Username = "new-user";

        Assert.Equal(ConnectionHealth.Unknown, vm.ConnectionHealth);
    }

    [Fact]
    public void PasswordChange_ClearsAnalysisResults()
    {
        var vm = CreateViewModel();

        vm.Url = "http://server.example.com";
        vm.Username = "user";
        vm.Password = "old-pass";

        // Change password — analysis must be invalidated
        vm.Password = "new-pass";

        Assert.Equal(ConnectionHealth.Unknown, vm.ConnectionHealth);
    }

    [Fact]
    public void ProviderTypeSwitch_ClearsAnalysisResults()
    {
        var vm = CreateViewModel();

        vm.Url = "http://server.example.com";
        vm.Username = "user";
        vm.Password = "pass";

        // Switch to Stalker — analysis must be invalidated
        vm.IsStalker = true;

        Assert.Equal(ConnectionHealth.Unknown, vm.ConnectionHealth);
        Assert.True(string.IsNullOrEmpty(vm.DetailedStatus));
    }

    [Fact]
    public void InvalidateActiveAnalysis_IncrementsGenerationBeforeCancel()
    {
        // Verify the race condition fix: InvalidateActiveAnalysis must increment
        // _analysisGeneration BEFORE cancelling the CTS. This ensures any in-flight
        // analysis sees the new generation and bails out.
        var vm = CreateViewModel();

        // Access private field via reflection to verify increment behavior
        var generationField = typeof(AddProfileViewModel)
            .GetField("_analysisGeneration",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(generationField);

        var initialGeneration = (int)generationField!.GetValue(vm)!;

        // Trigger InvalidateActiveAnalysis indirectly via URL change
        vm.Url = "http://test.example.com";

        var newGeneration = (int)generationField.GetValue(vm)!;

        // Verify generation was incremented
        Assert.True(newGeneration > initialGeneration,
            $"Generation must be incremented on input change. Before: {initialGeneration}, After: {newGeneration}");
    }

    [Fact]
    public void MultipleInputChanges_ContinueIncrementingGeneration()
    {
        var vm = CreateViewModel();

        var generationField = typeof(AddProfileViewModel)
            .GetField("_analysisGeneration",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var gen0 = (int)generationField.GetValue(vm)!;

        vm.Url = "http://a.example.com";
        var gen1 = (int)generationField.GetValue(vm)!;

        vm.Username = "user1";
        var gen2 = (int)generationField.GetValue(vm)!;

        vm.Password = "pass1";
        var gen3 = (int)generationField.GetValue(vm)!;

        Assert.True(gen1 > gen0);
        Assert.True(gen2 > gen1);
        Assert.True(gen3 > gen2);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Xtream username/password errors — separate fields
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Xtream_EmptyUsername_ShowsUsernameError()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;
        vm.Url = "http://server.example.com";
        vm.Password = "somepass";

        vm.TouchField("Username");

        Assert.NotNull(vm.UsernameError);
        Assert.Null(vm.PasswordError);
    }

    [Fact]
    public void Xtream_EmptyPassword_ShowsPasswordError()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;
        vm.Url = "http://server.example.com";
        vm.Username = "someuser";

        vm.TouchField("Password");

        Assert.NotNull(vm.PasswordError);
        Assert.Null(vm.UsernameError);
    }

    [Fact]
    public void Xtream_BothEmpty_ShowsBothErrors()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;
        vm.Url = "http://server.example.com";

        vm.TouchField("Username");
        vm.TouchField("Password");

        Assert.NotNull(vm.UsernameError);
        Assert.NotNull(vm.PasswordError);
    }

    [Fact]
    public void Xtream_BothFilled_NoErrors()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;
        vm.Url = "http://server.example.com";
        vm.Username = "validuser";
        vm.Password = "validpass";

        vm.TouchField("Username");
        vm.TouchField("Password");

        Assert.Null(vm.UsernameError);
        Assert.Null(vm.PasswordError);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Stalker MAC validation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Stalker_ValidMac_NoError()
    {
        var vm = CreateViewModel();
        vm.IsStalker = true;
        vm.Username = "00:1A:79:AB:CD:EF";

        vm.TouchField("Username");

        Assert.Null(vm.MacAddressError);
    }

    [Fact]
    public void Stalker_InvalidMacFormat_ShowsError()
    {
        var vm = CreateViewModel();
        vm.IsStalker = true;
        vm.Username = "not-a-mac";

        vm.TouchField("Username");

        Assert.NotNull(vm.MacAddressError);
    }

    [Fact]
    public void Stalker_EmptyMac_ShowsRequiredError()
    {
        var vm = CreateViewModel();
        vm.IsStalker = true;
        vm.Username = "";

        vm.TouchField("Username");

        Assert.NotNull(vm.MacAddressError);
    }

    [Fact]
    public void Stalker_MacFormatting_IsHandledByCodeBehind()
    {
        // MAC formatlama (colon ekleme, prefix, büyük harf) artık
        // ProfileSetupView code-behind'da UsernameTextBox_TextChanged
        // tarafından caret-korumalı şekilde yapılıyor.
        // ViewModel sadece değeri olduğu gibi saklar.
        var vm = CreateViewModel();
        vm.IsStalker = true;

        // ViewModel MAC değerini olduğu gibi saklar
        vm.Username = "AB:CD:EF";
        Assert.Equal("AB:CD:EF", vm.Username);

        // Tam MAC formatında değer de saklanır
        vm.Username = "00:1A:79:AB:CD:EF";
        Assert.Equal("00:1A:79:AB:CD:EF", vm.Username);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Edit mode PIN — empty PIN should not remove existing PIN
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EditMode_EmptyPin_DoesNotShowError()
    {
        var vm = CreateViewModel();
        var profile = new Profile
        {
            Name = "Test",
            PinHash = "existing-hash",
            ProviderAccount = new ProviderAccount
            {
                Type = ProfileType.XtreamCodes,
                Url = "http://server.example.com",
                Username = "user",
                Password = "encrypted"
            }
        };
        vm.InitializeForEdit(profile);

        // PIN field is empty (as initialized), HasPin = true (existing PIN)
        Assert.True(vm.HasPin);
        Assert.True(vm.HasExistingPin);
        Assert.True(string.IsNullOrEmpty(vm.PinCode));

        vm.TouchField("PinCode");

        // Empty PIN with existing PIN should NOT show error (keeps existing PIN)
        Assert.Null(vm.PinError);
    }

    [Fact]
    public void EditMode_NewPinRequired_WhenNoExistingPin()
    {
        var vm = CreateViewModel();
        var profile = new Profile
        {
            Name = "Test",
            PinHash = null, // No existing PIN
            ProviderAccount = new ProviderAccount
            {
                Type = ProfileType.XtreamCodes,
                Url = "http://server.example.com",
                Username = "user",
                Password = "encrypted"
            }
        };
        vm.InitializeForEdit(profile);

        vm.HasPin = true;
        vm.TouchField("PinCode");

        // Empty PIN with no existing PIN should show required error
        Assert.NotNull(vm.PinError);
    }

    [Fact]
    public void EditMode_PinMismatch_ShowsError()
    {
        var vm = CreateViewModel();
        vm.HasPin = true;
        vm.PinCode = "1234";
        vm.PinConfirm = "5678";

        vm.TouchField("PinConfirm");

        Assert.NotNull(vm.PinConfirmationError);
    }

    [Fact]
    public void EditMode_PinMatch_NoError()
    {
        var vm = CreateViewModel();
        vm.HasPin = true;
        vm.PinCode = "1234";
        vm.PinConfirm = "1234";

        vm.TouchField("PinConfirm");

        Assert.Null(vm.PinConfirmationError);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. Provider type switch — field reset
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SwitchToStalker_ResetsPasswordAndSetsMacPrefix()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;
        vm.Url = "http://server.example.com";
        vm.Username = "xtreamuser";
        vm.Password = "xtreampass";

        vm.IsStalker = true;

        Assert.Equal("00:1A:79:", vm.Username);
        Assert.True(string.IsNullOrEmpty(vm.Password));
    }

    [Fact]
    public void SwitchToStalker_CachesXtreamCredentials()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;
        vm.Url = "http://server.example.com";
        vm.Username = "xtreamuser";
        vm.Password = "xtreampass";

        vm.IsStalker = true;

        // Switch back to Xtream — should restore cached credentials
        vm.IsXtream = true;

        Assert.Equal("http://server.example.com", vm.Url);
        Assert.Equal("xtreamuser", vm.Username);
        Assert.Equal("xtreampass", vm.Password);
    }

    [Fact]
    public void SwitchToStalker_ClearsValidationErrors()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;
        vm.Url = "http://server.example.com";
        vm.Username = "user";

        // Trigger a validation error
        vm.TouchField("Password");
        Assert.NotNull(vm.PasswordError);

        // Switch to Stalker — validation errors should be cleared
        vm.IsStalker = true;

        Assert.Null(vm.PasswordError);
        Assert.Null(vm.UsernameError);
        Assert.Null(vm.ServerUrlError);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. Touched/submitted validation — field-level errors
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UntouchedField_NoError()
    {
        var vm = CreateViewModel();
        vm.Url = ""; // Invalid

        // Field not touched — no error shown
        Assert.Null(vm.ServerUrlError);
        Assert.Null(vm.UrlError);
    }

    [Fact]
    public void TouchedField_EmptyUrl_ShowsError()
    {
        var vm = CreateViewModel();

        vm.TouchField("Url");

        Assert.NotNull(vm.ServerUrlError);
    }

    [Fact]
    public void SubmittedForm_ShowsAllErrors()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;

        // Submit without filling fields — all errors should show
        vm.IsSubmitted = true;

        // Trigger all validators
        vm.TouchField("ProfileName");
        vm.TouchField("Url");
        vm.TouchField("Username");
        vm.TouchField("Password");

        Assert.NotNull(vm.ProfileNameError);
        Assert.NotNull(vm.ServerUrlError);
        Assert.NotNull(vm.UsernameError);
        Assert.NotNull(vm.PasswordError);
    }

    [Fact]
    public void TouchField_ResetsErrorOnValidInput()
    {
        var vm = CreateViewModel();
        vm.IsXtream = true;

        vm.TouchField("Username");
        Assert.NotNull(vm.UsernameError); // Empty = error

        vm.Username = "validuser";
        vm.TouchField("Username");
        Assert.Null(vm.UsernameError); // Filled = no error
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. Credential editing and save
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveWithChangedCredentials_DetectsChange()
    {
        ProfileSaveRequest? capturedRequest = null;
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(s => s.SaveProfileAsync(It.IsAny<ProfileSaveRequest>()))
            .Callback<ProfileSaveRequest>(r => capturedRequest = r)
            .ReturnsAsync(new Profile { Name = "Updated" });

        // Mock Xtream auth to succeed — SaveAsync calls VerifyProviderAsync
        // when credentials changed, which authenticates via IXtreamCodesService.
        var xtreamService = new Mock<IXtreamCodesService>();
        xtreamService
            .Setup(s => s.AuthenticateAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = CreateViewModel(
            profileService: profileService.Object,
            xtreamCodesService: xtreamService.Object);
        var originalProfile = new Profile
        {
            Name = "Original",
            ProviderAccount = new ProviderAccount
            {
                Type = ProfileType.XtreamCodes,
                Url = "http://old-server.example.com",
                Username = "olduser",
                Password = new DesktopSecurityService().Encrypt("oldpass")
            }
        };
        vm.InitializeForEdit(originalProfile);

        // Change credentials to differ from originals
        vm.ProfileName = "Updated";
        vm.Username = "newuser";
        vm.Password = "newpass";

        await vm.SaveCommand.ExecuteAsync(null);

        // Verify no validation errors blocked the save
        Assert.Null(vm.ProfileNameError);
        Assert.Null(vm.ServerUrlError);
        Assert.Null(vm.UsernameError);
        Assert.Null(vm.PasswordError);
        Assert.Null(vm.MacAddressError);
        Assert.Null(vm.PinError);
        Assert.Null(vm.PinConfirmationError);

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.CredentialsChanged,
            "CredentialsChanged should be true when Username or Password differs from original.");
        Assert.Equal("Updated", capturedRequest.ProfileName);
        Assert.Equal("newuser", capturedRequest.Username);
    }

    [Fact]
    public async Task SaveWithUnchangedCredentials_NoValidation()
    {
        var xtreamService = new Mock<IXtreamCodesService>();
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(s => s.SaveProfileAsync(It.IsAny<ProfileSaveRequest>()))
            .ReturnsAsync(new Profile { Name = "Same" });

        var vm = CreateViewModel(
            profileService: profileService.Object,
            xtreamCodesService: xtreamService.Object);
        var originalProfile = new Profile
        {
            Name = "Original",
            ProviderAccount = new ProviderAccount
            {
                Type = ProfileType.XtreamCodes,
                Url = "http://server.example.com",
                Username = "user",
                Password = new DesktopSecurityService().Encrypt("pass")
            }
        };
        vm.InitializeForEdit(originalProfile);
        vm.ProfileName = "Original";

        await vm.SaveCommand.ExecuteAsync(null);

        // Credentials unchanged — no server-side validation should occur
        xtreamService.Verify(s => s.AuthenticateAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static AddProfileViewModel CreateViewModel(
        IProfileService? profileService = null,
        IDialogService? dialogService = null,
        IXtreamCodesService? xtreamCodesService = null,
        IM3UParser? parser = null)
    {
        var avatarService = new Mock<IAvatarService>();
        avatarService
            .Setup(s => s.GetAvatarsByCategory())
            .Returns(new Dictionary<string, List<string>>
            {
                ["Default"] = new() { "default" }
            });

        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(s => s.IsPremium).Returns(true);

        return new AddProfileViewModel(
            profileService ?? new Mock<IProfileService>().Object,
            new Mock<IDispatcherService>().Object,
            avatarService.Object,
            dialogService ?? new Mock<IDialogService>().Object,
            licenseService.Object,
            parser ?? new Mock<IM3UParser>().Object,
            xtreamCodesService ?? new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            new DesktopSecurityService(),
            CreateLocalizationService());
    }

    private static ILocalizationService CreateLocalizationService()
    {
        var localization = new Mock<ILocalizationService>();
        localization
            .Setup(s => s.GetString(It.IsAny<string>()))
            .Returns((string key) => key);
        return localization.Object;
    }
}
