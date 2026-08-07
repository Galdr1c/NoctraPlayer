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

        // Kalıcı Premium'da gizleme: edition-locked VEYA Play lifetime paket.
        Assert.Contains("public bool CanUsePromoCodes =>", vm, StringComparison.Ordinal);
        Assert.Contains("!_licenseService.IsEditionLockedPremium", vm, StringComparison.Ordinal);
        Assert.Contains("!_licenseService.HasLifetimePremium", vm, StringComparison.Ordinal);
        Assert.Contains("_licenseService.IsPremium && _licenseService.PromoPremiumExpiresAtUtc.HasValue", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSettings_PromoCard_HiddenWhenLockedPremium_AndButtonBindsText()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var vm = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");

        Assert.Contains("IsVisible=\"{Binding CanUsePromoCodes}\"", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PromoApplyButtonText}\"", view, StringComparison.Ordinal);

        // Kalıcı Premium'da gizleme: edition-locked VEYA Play lifetime paket.
        Assert.Contains("public bool CanUsePromoCodes =>", vm, StringComparison.Ordinal);
        Assert.Contains("!_licenseService.IsEditionLockedPremium", vm, StringComparison.Ordinal);
        Assert.Contains("!_licenseService.HasLifetimePremium", vm, StringComparison.Ordinal);
        Assert.Contains("_licenseService.IsPremium && _licenseService.PromoPremiumExpiresAtUtc.HasValue", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePromoInput_ConfiguredForMobileUsage_AndDoneKeyAppliesCode()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml.cs");

        // Madde 20: mobil klavye yapılandırması (max uzunluk, erişilebilirlik adı)
        // + paste sonrası normalize davranışı zaten VM'de.
        // NOT: IsTextPredictionEnabled bu projenin Avalonia 12.1.0 sürümünde
        // TextBox üzerinde YOKTUR (AVLN2000 XAML derleme hatası — mobil uygulama
        // bu yüzden derlenemiyordu); 32a1758 ile eklenmişti ve kaldırıldı.
        Assert.Contains("x:Name=\"PromoCodeTextBox\"", view, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"24\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("IsTextPredictionEnabled", view, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding !IsApplyingPromoCode}\"", view, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"PromoCodeTextBox_KeyDown\"", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{loc:Translate GlobalSettings.Promo.Title}\"", view, StringComparison.Ordinal);

        // Madde 17: IME Done/Enter tuşu komutu doğrudan tetikler.
        Assert.Contains("PromoCodeTextBox_KeyDown", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyPromoCodeCommand.Execute", codeBehind, StringComparison.Ordinal);

        // Madde 18: dönen loading ikonu — Material.Icons.Avalonia'nın yerleşik
        // Animation="Spin" özelliği kullanılır; özel global Rotating stili yoktur
        // (o stil App.axaml'de başlangıçta yükleniyordu ve açılışı kırabilirdi).
        Assert.Contains("Animation=\"Spin\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Classes=\"Rotating\"", view, StringComparison.Ordinal);

        // Madde 19: sonuç yalnız renkle değil, ikonlarla da ifade ediliyor.
        Assert.Contains("Kind=\"CheckCircle\"", view, StringComparison.Ordinal);
        Assert.Contains("Kind=\"AlertCircle\"", view, StringComparison.Ordinal);

        // Ekran okuyucu canlı duyurusu (LiveSetting).
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileApp_DoesNotContainGlobalRotatingStyle()
    {
        // App.axaml başlangıçta (splash öncesi) yüklenir; buraya giren global
        // animasyon stili runtime XAML çözümlemesini kırabilir (uygulama açılmaz).
        // Stil kaldırıldı — dosyada MaterialIcon.Rotating / RotateTransform.Angle
        // animasyonu OLMAMALI. Dönen ikon yerleşik Animation="Spin" kullanır.
        var app = ReadProjectFile("Noctra.Mobile", "App.axaml");

        Assert.DoesNotContain("materialIcons|MaterialIcon.Rotating", app, StringComparison.Ordinal);
        Assert.DoesNotContain("RotateTransform.Angle", app, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopPromo_InputLockedWhileApplying_AndResultShowsIcons()
    {
        var view = ReadProjectFile("Noctra.Avalonia", "Views", "GlobalSettingsWindow.axaml");

        // Madde 16: istek sürerken giriş düzenlenemez.
        Assert.Contains("IsEnabled=\"{Binding !IsApplyingPromoCode}\"", view, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"24\"", view, StringComparison.Ordinal);

        // Madde 19: renk yalnız destekleyici; sonuç ikonlarla da bildiriliyor.
        Assert.Contains("Kind=\"CheckCircle\"", view, StringComparison.Ordinal);
        Assert.Contains("Kind=\"AlertCircle\"", view, StringComparison.Ordinal);

        // Ekran okuyucu canlı duyurusu (LiveSetting).
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", view, StringComparison.Ordinal);
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
    public void CanUsePromoCodes_WhenLifetimePremium_ShouldBeFalse()
    {
        // Play lifetime paket sahibi: kalıcı Premium, kod girilemez (mobil + masaüstü).
        var vm = CreateViewModel(lockedPremium: false, hasLifetime: true);

        Assert.False(vm.CanUsePromoCodes);
    }

    [Fact]
    public void CanUsePromoCodes_WhenFreeEdition_ShouldBeTrue_EvenIfTimedPromoActive()
    {
        var vm = CreateViewModel(lockedPremium: false, isPremium: true, hasExpiry: true);

        Assert.True(vm.CanUsePromoCodes);
    }

    [Fact]
    public void CanUsePromoCodes_WhenTimedStoreSubscriptionActive_ShouldBeTrue()
    {
        // Süreli (abonelik) Premium: kart kalmalı — sistem süre eklemeyi destekler.
        var vm = CreateViewModel(lockedPremium: false, isPremium: true);

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

    [Fact]
    public async Task ApplyPromoCode_Success_ShowsSingleResultCardWithAddedAndExpiryLines()
    {
        var license = new Mock<ILicenseService>();
        license.SetupGet(l => l.IsEditionLockedPremium).Returns(false);
        license.SetupGet(l => l.IsPremium).Returns(true);
        license.SetupGet(l => l.PromoPremiumExpiresAtUtc).Returns(DateTime.UtcNow.AddDays(30));
        license.Setup(l => l.ApplyPromoCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(PromoCodeRedemptionResult.Ok("legacy service message", DateTime.UtcNow.AddDays(30), 30));

        var vm = CreateViewModel(
            lockedPremium: false,
            isPremium: true,
            hasExpiry: true,
            CreateKeyPassthroughLocalization(),
            license);

        vm.PromoCodeInput = "PROMO30";
        await vm.ApplyPromoCodeCommand.ExecuteAsync(null);

        // Tek sonuç kartı: "✓ N gün Premium eklendi" + "Yeni bitiş tarihi: T"
        Assert.True(vm.IsPromoCodeStatusSuccess);
        Assert.Contains("GlobalSettings.Promo.Success.DaysAddedFormat", vm.PromoCodeStatus);
        Assert.Contains("GlobalSettings.Promo.Success.NewExpiryFormat", vm.PromoCodeStatus);
        Assert.Contains(Environment.NewLine, vm.PromoCodeStatus);
        // Servisin eski tek cümlelik başarı mesajı artık kullanılmaz
        Assert.DoesNotContain("legacy service message", vm.PromoCodeStatus);
        Assert.Equal(string.Empty, vm.PromoCodeInput);
    }

    [Fact]
    public async Task ApplyPromoCode_Failure_ShowsServiceMessageOnly()
    {
        var license = new Mock<ILicenseService>();
        license.SetupGet(l => l.IsEditionLockedPremium).Returns(false);
        license.Setup(l => l.ApplyPromoCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(PromoCodeRedemptionResult.Fail("servis hata mesajı", PromoCodeResultKind.CodeInvalid));

        var vm = CreateViewModel(
            lockedPremium: false,
            localization: CreateKeyPassthroughLocalization(),
            license: license);

        vm.PromoCodeInput = "PROMO30";
        await vm.ApplyPromoCodeCommand.ExecuteAsync(null);

        Assert.False(vm.IsPromoCodeStatusSuccess);
        Assert.Equal("servis hata mesajı", vm.PromoCodeStatus);
    }

    // ==========================================
    // Madde 14: Aktif Premium bilgisi promo formunun dışında, Premium kartında
    // ==========================================

    [Fact]
    public void DesktopGlobalSettings_PremiumStatusText_BoundOnlyInPremiumCard()
    {
        var view = ReadProjectFile("Noctra.Avalonia", "Views", "GlobalSettingsWindow.axaml");

        // PremiumStatusText promo kartından çıkarıldı: dosyada tek bağlantı
        // kalmalı ve o bağlantı PREMIUM CARD bölümünde olmalı.
        var firstIndex = view.IndexOf("Text=\"{Binding PremiumStatusText}\"", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, "PremiumStatusText Premium kartında bağlı olmalı.");
        Assert.Equal(firstIndex, view.LastIndexOf("Text=\"{Binding PremiumStatusText}\"", StringComparison.Ordinal));
        Assert.True(view.IndexOf("PREMIUM FOOTER", StringComparison.Ordinal) < firstIndex,
            "PremiumStatusText bağlantısı PREMIUM FOOTER bölümünde olmalı.");
    }

    [Fact]
    public void MobileSettings_PremiumStatusText_BoundOnlyInPremiumCard()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        var firstIndex = view.IndexOf("Text=\"{Binding PremiumStatusText}\"", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, "PremiumStatusText Premium kartında bağlı olmalı.");
        Assert.Equal(firstIndex, view.LastIndexOf("Text=\"{Binding PremiumStatusText}\"", StringComparison.Ordinal));
        Assert.True(view.IndexOf("About", StringComparison.Ordinal) < firstIndex,
            "PremiumStatusText bağlantısı About (Premium) kartında olmalı.");
    }

    [Fact]
    public void ViewModels_DoNotOverwritePromoCodeStatusOnSubscriptionChange()
    {
        var globalVm = ReadProjectFile("Noctra.Core", "ViewModels", "GlobalSettingsViewModel.cs");
        var settingsVm = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");

        // Başarı mesajı abonelik event'i tarafından ezilmemeli:
        // OnLicenseSubscriptionChanged PromoCodeStatus'a yazmamalı.
        Assert.DoesNotContain("PromoCodeStatus = PremiumStatusText", globalVm, StringComparison.Ordinal);
        Assert.DoesNotContain("PromoCodeStatus = PremiumStatusText", settingsVm, StringComparison.Ordinal);
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
        Mock<ILocalizationService>? localization = null,
        Mock<ILicenseService>? license = null,
        bool hasLifetime = false)
    {
        license ??= new Mock<ILicenseService>();
        license.SetupGet(l => l.IsEditionLockedPremium).Returns(lockedPremium);
        license.SetupGet(l => l.HasLifetimePremium).Returns(hasLifetime);
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
            update.Object);    }

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
