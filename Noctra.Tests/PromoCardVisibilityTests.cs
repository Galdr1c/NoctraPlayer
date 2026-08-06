using Moq;
using Noctra.Core.Services;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// Kalıcı Premium edition'da promo kartı gizlenmeli (kod zaten anlamsız);
/// süreli promo Premium aktifken kart kalmalı ve buton \"Süre Ekle\" demeli.
/// </summary>
public class PromoCardVisibilityTests
{
    // ==========================================
    // XAML bağlama taramaları (proje konvansiyonu)
    // ==========================================

    [Fact]
    public void DesktopGlobalSettings_PromoCard_HiddenWhenLockedPremium_AndButtonBindsText()
    {
        var view = ReadProjectFile("Noctra.Avalonia", "Views", "GlobalSettingsWindow.axaml");
        var vm = ReadProjectFile("Noctra.Core", "ViewModels", "GlobalSettingsViewModel.cs");

        Assert.Contains("IsVisible=\"{Binding CanUsePromoCodes}\"", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PromoApplyButtonText}\"", view, StringComparison.Ordinal);

        Assert.Contains("public bool CanUsePromoCodes => !_licenseService.IsEditionLockedPremium;", vm, StringComparison.Ordinal);
        Assert.Contains("_licenseService.IsPremium && _licenseService.PromoPremiumExpiresAtUtc.HasValue", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSettings_PromoCard_HiddenWhenLockedPremium_AndButtonBindsText()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var vm = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");

        Assert.Contains("IsVisible=\"{Binding CanUsePromoCodes}\"", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PromoApplyButtonText}\"", view, StringComparison.Ordinal);

        Assert.Contains("public bool CanUsePromoCodes => !_licenseService.IsEditionLockedPremium;", vm, StringComparison.Ordinal);
        Assert.Contains("_licenseService.IsPremium && _licenseService.PromoPremiumExpiresAtUtc.HasValue", vm, StringComparison.Ordinal);
    }

    // ==========================================
    // Davranış testleri (GlobalSettingsViewModel)
    // ==========================================

    [Fact]
    public void CanUsePromoCodes_WhenEditionLockedPremium_ShouldBeFalse()
    {
        var vm = CreateViewModel(lockedPremium: true);

        Assert.False(vm.CanUsePromoCodes);
    }

    [Fact]
    public void CanUsePromoCodes_WhenFreeEdition_ShouldBeTrue_EvenIfTimedPromoActive()
    {
        var vm = CreateViewModel(lockedPremium: false, isPremium: true, hasExpiry: true);

        Assert.True(vm.CanUsePromoCodes);
    }

    [Fact]
    public void PromoApplyButtonText_WhenTimedPromoPremiumActive_ShouldReturnExtendKey()
    {
        var localization = CreateKeyPassthroughLocalization();
        var vm = CreateViewModel(
            lockedPremium: false,
            isPremium: true,
            hasExpiry: true,
            localization);

        Assert.Equal("GlobalSettings.Promo.ApplyExtend", vm.PromoApplyButtonText);
    }

    [Fact]
    public void PromoApplyButtonText_WhenFree_ShouldReturnApplyKey()
    {
        var localization = CreateKeyPassthroughLocalization();
        var vm = CreateViewModel(
            lockedPremium: false,
            isPremium: false,
            hasExpiry: false,
            localization);

        Assert.Equal("GlobalSettings.Promo.Apply", vm.PromoApplyButtonText);
    }

    private static Mock<ILocalizationService> CreateKeyPassthroughLocalization()
    {
        var localization = new Mock<ILocalizationService>();
        localization
            .Setup(l => l.GetString(It.IsAny<string>()))
            .Returns<string>(key => key);
        return localization;
    }

    private static GlobalSettingsViewModel CreateViewModel(
        bool lockedPremium,
        bool isPremium = false,
        bool hasExpiry = false,
        Mock<ILocalizationService>? localization = null)
    {
        var license = new Mock<ILicenseService>();
        license.SetupGet(l => l.IsEditionLockedPremium).Returns(lockedPremium);
        license.SetupGet(l => l.IsPremium).Returns(isPremium);
        license.SetupGet(l => l.PromoPremiumExpiresAtUtc)
            .Returns(hasExpiry ? DateTime.UtcNow.AddDays(7) : null);

        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings());

        var appVersion = new Mock<IAppVersionService>();
        appVersion.SetupGet(a => a.DisplayVersion).Returns("1.0.0");

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetCacheSizeStringAsync()).ReturnsAsync("0 B");

        var update = new Mock<IAppUpdateService>();
        update.Setup(u => u.CheckPendingUpdateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate });

        return new GlobalSettingsViewModel(
            Mock.Of<IThemeService>(),
            Mock.Of<IDialogService>(),
            settings.Object,
            cache.Object,
            appVersion.Object,
            Mock.Of<IDispatcherService>(),
            Mock.Of<IDiagnosticReportService>(),
            license.Object,
            Mock.Of<IProfileService>(),
            Mock.Of<IEpgService>(),
            localization?.Object ?? Mock.Of<ILocalizationService>(),
            update.Object);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
