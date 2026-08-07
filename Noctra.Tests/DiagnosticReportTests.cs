using System.IO;
using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// Diagnostic raporlarının GÜNCEL ve DOĞRU bilgi ürettiğini doğrular: yalnızca
/// "Premium/Free" değil — kaynak (kalıcı/abonelik/promosyon/edisyon), bitiş,
/// gerçek trial ve pending satın alma durumu rapora yansır; sürüm bilgisi
/// Core assembly'sinden değil doğru sürüm servisinden gelir.
/// </summary>
public class DiagnosticReportTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static Mock<ILicenseService> CreateLicenseMock()
    {
        var mock = new Mock<ILicenseService>();
        mock.Setup(m => m.IsPremium).Returns(false);
        mock.Setup(m => m.CurrentPremiumSource).Returns(PremiumSource.None);
        mock.Setup(m => m.PremiumExpiresAtUtc).Returns((DateTime?)null);
        mock.Setup(m => m.HasPendingStorePurchase).Returns(false);
        mock.Setup(m => m.GetCurrentSubscription()).Returns(new SubscriptionInfo());
        return mock;
    }

    // ==========================================
    // DiagnosticLicenseReport.BuildStatus
    // ==========================================

    [Fact]
    public void BuildStatus_FreeUser_ReportsFreeOnly()
    {
        var license = CreateLicenseMock();

        var status = DiagnosticLicenseReport.BuildStatus(license.Object);

        Assert.Contains("Status: Free", status);
        Assert.DoesNotContain("Premium Source", status);
        Assert.DoesNotContain("Pending Purchase", status);
    }

    [Fact]
    public void BuildStatus_LifetimePremium_ReportsLifetimeSource()
    {
        var license = CreateLicenseMock();
        license.Setup(m => m.IsPremium).Returns(true);
        license.Setup(m => m.CurrentPremiumSource).Returns(PremiumSource.GooglePlayLifetime);
        license.Setup(m => m.GetCurrentSubscription())
            .Returns(new SubscriptionInfo { Tier = SubscriptionTier.Premium });

        var status = DiagnosticLicenseReport.BuildStatus(license.Object);

        Assert.Contains("Status: Premium", status);
        Assert.Contains("Google Play Lifetime", status);
    }

    [Fact]
    public void BuildStatus_SubscriptionWithExpiry_ReportsExpiryUtc()
    {
        var expires = DateTime.UtcNow.AddDays(20);
        var license = CreateLicenseMock();
        license.Setup(m => m.IsPremium).Returns(true);
        license.Setup(m => m.CurrentPremiumSource).Returns(PremiumSource.GooglePlaySubscription);
        license.Setup(m => m.PremiumExpiresAtUtc).Returns(expires);
        license.Setup(m => m.GetCurrentSubscription())
            .Returns(new SubscriptionInfo { Tier = SubscriptionTier.Premium, ExpiresAt = expires });

        var status = DiagnosticLicenseReport.BuildStatus(license.Object);

        Assert.Contains("Status: Premium", status);
        Assert.Contains("Google Play Subscription", status);
        Assert.Contains(expires.ToString("O"), status);
        // Ücretli (trial'sız) abonelik asla "Trial: Yes" görünmemeli.
        Assert.DoesNotContain("Trial: Yes", status);
    }

    [Fact]
    public void BuildStatus_TrialSubscription_ReportsTrial()
    {
        var license = CreateLicenseMock();
        license.Setup(m => m.IsPremium).Returns(true);
        license.Setup(m => m.CurrentPremiumSource).Returns(PremiumSource.GooglePlaySubscription);
        license.Setup(m => m.GetCurrentSubscription())
            .Returns(new SubscriptionInfo
            {
                Tier = SubscriptionTier.Premium,
                ExpiresAt = DateTime.UtcNow.AddDays(14),
                IsTrialPeriod = true
            });

        var status = DiagnosticLicenseReport.BuildStatus(license.Object);

        Assert.Contains("Trial: Yes", status);
    }

    [Fact]
    public void BuildStatus_PendingPurchase_ReportsPending()
    {
        var license = CreateLicenseMock();
        license.Setup(m => m.HasPendingStorePurchase).Returns(true);

        var status = DiagnosticLicenseReport.BuildStatus(license.Object);

        Assert.Contains("Pending Purchase: Yes", status);
    }

    // ==========================================
    // Platform servisleri ortak doğru bilgiyi kullanır
    // ==========================================

    [Fact]
    public void AndroidDiagnosticService_ReportsCurrentLicenseAndAccurateVersion()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Noctra.Android", "Services", "AndroidDiagnosticReportService.cs"));

        // Yalnızca "Premium/Free" değil — ortak güncel lisans durumu üretilir.
        Assert.Contains("DiagnosticLicenseReport.BuildStatus(_licenseService)", source);
        Assert.DoesNotContain("Status: {(_licenseService.IsPremium", source);
        // Sürüm gerçek paket sürümünden + build numarasından gelir.
        Assert.Contains("_appVersionService.DisplayVersion", source);
        Assert.Contains("_appVersionService.BuildNumber", source);
        // Raporun üretildiği an kaydedilir.
        Assert.Contains("Report Generated (UTC)", source);
    }

    [Fact]
    public void DesktopDiagnosticService_ReportsCurrentLicenseAndAccurateVersion()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Noctra.Core", "Services", "DiagnosticReportService.cs"));

        // Ortak güncel lisans durumu kullanılır; tek "Premium/Free" satırı yok.
        Assert.Contains("DiagnosticLicenseReport.BuildStatus(_licenseService)", source);
        Assert.DoesNotContain("_licenseService.IsPremium ? \"Premium\" : \"Free\"", source);
        // Sürüm Core assembly'sinden DEĞİL, sürüm servisinden okunur — ve yalnız
        // ctor'da değil, rapor gövdesinde KULLANILIR.
        Assert.Contains("IAppVersionService appVersionService", source);
        Assert.Contains("App Version: {_appVersionService.DisplayVersion}", source);
        Assert.DoesNotContain("GetType().Assembly.GetName().Version", source);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NoctraPlayer.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
