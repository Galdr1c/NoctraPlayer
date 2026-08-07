using System.Text.Json;
using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// Backend doğrulama (Noctra.Billing.Api) sonrası LicenseService davranışı:
///  - Doğrulama başarılıysa hak hem kullanılır hem önbelleğe yazılır.
///  - Doğrulama hizmetine ulaşılamıyorsa (IsVerified=false) son bilinen
///    doğrulanmış önbellek kullanılır — hak asla erken düşmez.
///  - Önbellek yoksa Free'e düşülür (doğrulanamayan hak = hak yok).
/// </summary>
public sealed class StoreEntitlementFallbackTests
{
    private static Mock<IAppEditionService> CreateFreeEditionMock()
    {
        var editionMock = new Mock<IAppEditionService>();
        editionMock.Setup(m => m.IsFreeEdition).Returns(true);
        editionMock.Setup(m => m.IsPremiumEdition).Returns(false);
        return editionMock;
    }

    private static LicenseService CreateService(
        ISettingsService settings,
        IStorePurchaseService store) =>
        new(
            CreateFreeEditionMock().Object,
            settings,
            new HttpClient(),
            localizationService: null,
            securityService: new SecurityService(),
            storePurchaseService: store);

    private static Mock<IStorePurchaseService> CreateStoreMock(StoreEntitlement entitlement)
    {
        var storeMock = new Mock<IStorePurchaseService>();
        storeMock.Setup(m => m.IsSupported).Returns(true);
        storeMock.Setup(m => m.GetEntitlementAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(entitlement));
        return storeMock;
    }

    private static string Serialize(StoreEntitlement entitlement) =>
        JsonSerializer.Serialize(entitlement);

    [Fact]
    public async Task UnverifiedEntitlement_UsesCachedVerifiedLifetime()
    {
        var settings = new TestSettingsService
        {
            StoreVerifiedEntitlementJson = Serialize(new StoreEntitlement { HasLifetimePremium = true })
        };
        // Doğrulama hizmetine ulaşılamadı (ağ/sunucu) — ama hak düşürülmez.
        var store = CreateStoreMock(new StoreEntitlement { IsVerified = false });

        var service = CreateService(settings, store.Object);
        await service.RefreshSubscriptionStatusAsync();

        Assert.True(service.IsPremium);
        Assert.Null(service.PremiumExpiresAtUtc);
    }

    [Fact]
    public async Task UnverifiedEntitlement_UsesCachedVerifiedSubscriptionExpiry()
    {
        var cachedExpiry = DateTime.UtcNow.AddDays(20);
        var settings = new TestSettingsService
        {
            StoreVerifiedEntitlementJson = Serialize(new StoreEntitlement
            {
                SubscriptionExpiresAtUtc = cachedExpiry
            })
        };
        var store = CreateStoreMock(new StoreEntitlement { IsVerified = false });

        var service = CreateService(settings, store.Object);
        await service.RefreshSubscriptionStatusAsync();

        Assert.True(service.IsPremium);
        Assert.Equal(cachedExpiry, service.PremiumExpiresAtUtc);
    }

    [Fact]
    public async Task UnverifiedEntitlement_ExpiredCache_IsFree()
    {
        var settings = new TestSettingsService
        {
            StoreVerifiedEntitlementJson = Serialize(new StoreEntitlement
            {
                SubscriptionExpiresAtUtc = DateTime.UtcNow.AddDays(-1)
            })
        };
        var store = CreateStoreMock(new StoreEntitlement { IsVerified = false });

        var service = CreateService(settings, store.Object);
        await service.RefreshSubscriptionStatusAsync();

        Assert.False(service.IsPremium);
        Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
    }

    [Fact]
    public async Task UnverifiedEntitlement_NoCache_IsFree()
    {
        var settings = new TestSettingsService();
        var store = CreateStoreMock(new StoreEntitlement { IsVerified = false });

        var service = CreateService(settings, store.Object);
        await service.RefreshSubscriptionStatusAsync();

        Assert.False(service.IsPremium);
        Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
    }

    [Fact]
    public async Task VerifiedEntitlement_UpdatesCacheAndPremium()
    {
        var settings = new TestSettingsService();
        var expiry = DateTime.UtcNow.AddDays(15);
        var store = CreateStoreMock(new StoreEntitlement
        {
            SubscriptionExpiresAtUtc = expiry,
            IsVerified = true
        });

        var service = CreateService(settings, store.Object);
        await service.RefreshSubscriptionStatusAsync();

        Assert.True(service.IsPremium);
        Assert.Equal(expiry, service.PremiumExpiresAtUtc);

        // Önbelleğe yazıldı — sonraki çevrimdışı açılışta kullanılır.
        Assert.False(string.IsNullOrWhiteSpace(settings.StoreVerifiedEntitlementJson));
        var cached = JsonSerializer.Deserialize<StoreEntitlement>(settings.StoreVerifiedEntitlementJson!);
        Assert.NotNull(cached);
        Assert.Equal(expiry, cached!.SubscriptionExpiresAtUtc);
    }

    [Fact]
    public async Task VerifiedNoEntitlement_OverwritesCacheWithNone()
    {
        var settings = new TestSettingsService
        {
            StoreVerifiedEntitlementJson = Serialize(new StoreEntitlement { HasLifetimePremium = true })
        };
        // Backend doğrulaması: hiçbir aktif hak yok (ör. iptal edilmiş).
        var store = CreateStoreMock(new StoreEntitlement { IsVerified = true });

        var service = CreateService(settings, store.Object);
        await service.RefreshSubscriptionStatusAsync();

        Assert.False(service.IsPremium);
        var cached = JsonSerializer.Deserialize<StoreEntitlement>(settings.StoreVerifiedEntitlementJson!);
        Assert.NotNull(cached);
        Assert.False(cached!.HasLifetimePremium);
        Assert.Null(cached.SubscriptionExpiresAtUtc);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public event Action? SettingsChanged;

        public string? StoreVerifiedEntitlementJson
        {
            get => Settings.StoreVerifiedEntitlementJson;
            set => Settings.StoreVerifiedEntitlementJson = value;
        }

        public Task LoadAsync() => Task.CompletedTask;
        public Task LoadProfileSettingsAsync(int profileId) => Task.CompletedTask;
        public Task<AppSettings?> PeekProfileSettingsAsync(int profileId) => Task.FromResult<AppSettings?>(Settings);
        public Task SaveAsync()
        {
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }
        public void NotifySettingsChanged() => SettingsChanged?.Invoke();
        public void ResetToDefaults() => SettingsChanged?.Invoke();
        public Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds) => Task.FromResult(0);
    }
}
