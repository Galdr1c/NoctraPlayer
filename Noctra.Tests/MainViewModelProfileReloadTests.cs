using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Tests;

/// <summary>
/// Oturum içi profil yeniden yükleme testleri. Kısa ömürlü ProfileAccessGrant
/// yalnızca GİRİŞTE doğrulanır; kullanıcı profildeyken teknik yeniden yükleme
/// (RefreshSelectedPlaylistAsync) grant'in süresi dolmuş olsa bile çalışmalıdır
/// — oturum içi yol tekrar PIN sormaz.
/// </summary>
public class MainViewModelProfileReloadTests
{
    private readonly AppSettings _settings = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IDialogService> _dialogService = new();
    private readonly Mock<ILicenseService> _licenseService = new();

    public MainViewModelProfileReloadTests()
    {
        _settingsService.SetupGet(service => service.Settings).Returns(_settings);
        _settingsService.Setup(service => service.SaveAsync()).Returns(Task.CompletedTask);
        _licenseService.SetupGet(service => service.IsPremium).Returns(true);
        _licenseService.Setup(service => service.IsWithinLimit(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
    }

    [Fact]
    public async Task LoadProfileAsync_PinProfile_WithoutValidGrant_Throws()
    {
        // PIN korumalı profil — giriş kapısı (grant kontrolü) split sonrası da korunmalı.
        var profile = new Profile
        {
            Id = 2,
            Name = "PIN Profile",
            PinHash = ProfilePinVerifier.Create("1234"),
            ProviderAccount = new ProviderAccount
            {
                Id = 2,
                Url = "http://test.com",
                Type = ProfileType.M3U
            }
        };

        var viewModel = CreateViewModel();
        var expiredGrant = new ProfileAccessGrant(
            profile.Id,
            ProfileAccessPurpose.Load,
            DateTime.UtcNow.AddSeconds(-1));

        // Eksik grant ve süresi dolmuş grant — ikisi de girişi reddetmeli.
        await Assert.ThrowsAsync<ProfileAccessDeniedException>(() => viewModel.LoadProfileAsync(profile));
        await Assert.ThrowsAsync<ProfileAccessDeniedException>(() => viewModel.LoadProfileAsync(profile, expiredGrant));

        // Oturum kurulmadı.
        Assert.Null(viewModel.CurrentProfileId);
    }

    [Fact]
    public async Task RefreshSelectedPlaylist_AfterGrantExpiry_ReloadsWithoutAccessDenied()
    {
        // PIN korumalı profil — giriş yetkisi 1 saniyede süresi dolar.
        var profile = new Profile
        {
            Id = 1,
            Name = "PIN Profile",
            PinHash = ProfilePinVerifier.Create("1234"),
            ProviderAccount = new ProviderAccount
            {
                Id = 1,
                Url = "http://test.com",
                Type = ProfileType.M3U
            }
        };

        var viewModel = CreateViewModel();
        var shortLivedGrant = new ProfileAccessGrant(
            profile.Id,
            ProfileAccessPurpose.Load,
            DateTime.UtcNow.AddSeconds(1));

        // Geçerliyken giriş başarılı — oturum kurulur.
        await viewModel.LoadProfileAsync(profile, shortLivedGrant);
        Assert.Equal(profile.Id, viewModel.CurrentProfileId);

        // Kısa ömürlü giriş yetkisinin süresi dolar.
        await Task.Delay(1200);

        // Oturum içi teknik yeniden yükleme (SelectedPlaylist == null) — artık
        // giriş grant'ine bağlı değil. Eski davranış burada ProfileAccessDeniedException
        // fırlatır ve kullanıcıyı profildeyken hata ekranına düşürürdü.
        await viewModel.RefreshSelectedPlaylistAsync();

        // Oturum hâlâ aktif — kullanıcı profilden atılmadı.
        Assert.Equal(profile.Id, viewModel.CurrentProfileId);
    }

    private MainViewModel CreateViewModel()
    {
        var dispatcher = new Mock<IDispatcherService>();
        dispatcher.Setup(service => service.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        dispatcher.Setup(service => service.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        dispatcher.Setup(service => service.InvokeAsync(It.IsAny<Func<Task>>()))
            .Returns((Func<Task> action) => action());

        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service.GetString(It.IsAny<string>())).Returns("Test");

        return new MainViewModel(
            _settingsService.Object,
            new Mock<IContentDownloadService>().Object,
            new Mock<IMetadataService>().Object,
            dispatcher.Object,
            _dialogService.Object,
            null!,
            new Mock<IChannelService>().Object,
            new Mock<IMediaService>().Object,
            new Mock<IEpgService>().Object,
            new Mock<IPlaylistService>().Object,
            new Mock<IWatchHistoryService>().Object,
            new Mock<IXtreamCodesService>().Object,
            new Mock<IStalkerPortalService>().Object,
            null!,
            null!,
            new Mock<IDbContextFactory<AppDbContext>>().Object,
            new Mock<ISecurityService>().Object,
            new Mock<ITmdbSyncService>().Object,
            _licenseService.Object,
            new Mock<IAppVersionService>().Object,
            localization.Object,
            new Mock<ILogger<MainViewModel>>().Object);
    }
}
