using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// Mağaza (Play Billing) haklarının LicenseService abonelik durumuna
    /// yansımasını test eder:
    ///
    ///  - Kalıcı paket (lifetime) her zaman Premium verir, süresiz.
    ///  - Abonelik süresi Premium verir; süresi dolmuşsa vermez.
    ///  - Lifetime + süreli (abonelik/promosyon) → lifetime önceliklidir.
    ///  - Abonelik + promosyon → en geç bitiş kazanır.
    ///  - Store servisi yokken (masaüstü) davranış değişmez.
    ///  - StartPurchaseFlowAsync store destekleniyorsa Play Billing akışını
    ///    başlatır (abonelik ürünü öncelikli), desteklenmiyorsa URI akışını kullanır.
    /// </summary>
    public class StoreEntitlementTests
    {
        // ==========================================
        // Helpers
        // ==========================================

        private static Mock<IAppEditionService> CreateFreeEditionMock()
        {
            var editionMock = new Mock<IAppEditionService>();
            editionMock.Setup(m => m.IsFreeEdition).Returns(true);
            editionMock.Setup(m => m.IsPremiumEdition).Returns(false);
            return editionMock;
        }

        private static LicenseService CreateService(
            IAppEditionService edition,
            ISettingsService? settings = null,
            IStorePurchaseService? store = null,
            IPlatformActionService? platformActions = null)
        {
            return new LicenseService(
                edition,
                settings ?? new TestSettingsService(),
                new HttpClient(new NotFoundHttpMessageHandler()),
                localizationService: null,
                securityService: new SecurityService(),
                platformActionService: platformActions,
                storePurchaseService: store);
        }

        private static Mock<IStorePurchaseService> CreateStoreMock(StoreEntitlement entitlement, bool isSupported = true)
        {
            var storeMock = new Mock<IStorePurchaseService>();
            storeMock.Setup(m => m.IsSupported).Returns(isSupported);
            storeMock.Setup(m => m.GetEntitlementAsync(It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(entitlement));
            return storeMock;
        }

        // ==========================================
        // Lifetime package
        // ==========================================

        [Fact]
        public async Task StoreEntitlement_LifetimePackage_IsPremiumAndNoExpiry()
        {
            var store = CreateStoreMock(new StoreEntitlement { HasLifetimePremium = true });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.Null(service.PremiumExpiresAtUtc);
            Assert.Equal(SubscriptionTier.Premium, service.CurrentTier);
        }

        [Fact]
        public async Task StoreEntitlement_LifetimePackage_WinsOverExpiredPromo()
        {
            // Promosyon süresi dolmuş olsa bile kalıcı paket Premium verir.
            var settings = new TestSettingsService
            {
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddDays(-1),
                ActivePromoCode = "EXPIRED"
            };
            var store = CreateStoreMock(new StoreEntitlement { HasLifetimePremium = true });

            var service = CreateService(CreateFreeEditionMock().Object, settings: settings, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.Null(service.PremiumExpiresAtUtc);
        }

        [Fact]
        public async Task StoreEntitlement_LifetimePackage_RejectsPromoCode()
        {
            // Lifetime her zaman kazanır; eklenen süre hiç kullanılmaz, bu yüzden
            // kod reddedilmeli — config URL'ine bile gerek kalmadan (guard erken).
            var store = CreateStoreMock(new StoreEntitlement { HasLifetimePremium = true });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.Unknown, result.Kind);
            Assert.Contains("promosyon kodları gerekmez", result.Message);
        }

        // ==========================================
        // Subscription
        // ==========================================

        [Fact]
        public async Task StoreEntitlement_ActiveSubscription_IsPremiumWithExpiry()
        {
            var expires = DateTime.UtcNow.AddDays(20);
            var store = CreateStoreMock(new StoreEntitlement { SubscriptionExpiresAtUtc = expires });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.NotNull(service.PremiumExpiresAtUtc);
            Assert.Equal(expires, service.PremiumExpiresAtUtc);
        }

        [Fact]
        public async Task StoreEntitlement_ExpiredSubscription_IsFree()
        {
            var store = CreateStoreMock(new StoreEntitlement
            {
                SubscriptionExpiresAtUtc = DateTime.UtcNow.AddDays(-1)
            });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.False(service.IsPremium);
            Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
        }

        [Fact]
        public async Task StoreEntitlement_SubscriptionWinsOverEarlierPromo()
        {
            var subscriptionEnd = DateTime.UtcNow.AddDays(15);
            var promoEnd = DateTime.UtcNow.AddDays(5);
            var settings = new TestSettingsService
            {
                PromoPremiumExpiresAtUtc = promoEnd,
                ActivePromoCode = "PROMO5D"
            };
            var store = CreateStoreMock(new StoreEntitlement { SubscriptionExpiresAtUtc = subscriptionEnd });

            var service = CreateService(CreateFreeEditionMock().Object, settings: settings, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            // En geç bitiş kazanır → abonelik.
            Assert.Equal(subscriptionEnd, service.PremiumExpiresAtUtc);
        }

        [Fact]
        public async Task StoreEntitlement_PromoWinsOverEarlierSubscription()
        {
            var subscriptionEnd = DateTime.UtcNow.AddDays(5);
            var promoEnd = DateTime.UtcNow.AddDays(15);
            var settings = new TestSettingsService
            {
                PromoPremiumExpiresAtUtc = promoEnd,
                ActivePromoCode = "PROMO15D"
            };
            var store = CreateStoreMock(new StoreEntitlement { SubscriptionExpiresAtUtc = subscriptionEnd });

            var service = CreateService(CreateFreeEditionMock().Object, settings: settings, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            // En geç bitiş kazanır → promosyon.
            Assert.Equal(promoEnd, service.PremiumExpiresAtUtc);
        }

        // ==========================================
        // Premium source (kalıcı / abonelik / promosyon ayrımı)
        // ==========================================

        [Fact]
        public async Task StoreEntitlement_ActiveSubscription_SourceIsSubscriptionAndNotTrial()
        {
            var expires = DateTime.UtcNow.AddDays(20);
            var store = CreateStoreMock(new StoreEntitlement { SubscriptionExpiresAtUtc = expires });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.False(service.HasLifetimePremium);
            Assert.True(service.HasActiveStoreSubscription);
            Assert.Equal(PremiumSource.GooglePlaySubscription, service.CurrentPremiumSource);
            // Ücretli aylık abonelik "trial" gibi gösterilmemeli.
            Assert.False(service.GetCurrentSubscription().IsTrialPeriod);
        }

        [Fact]
        public async Task StoreEntitlement_LifetimePackage_SourceIsLifetime()
        {
            var store = CreateStoreMock(new StoreEntitlement { HasLifetimePremium = true });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.True(service.HasLifetimePremium);
            Assert.False(service.HasActiveStoreSubscription);
            Assert.Equal(PremiumSource.GooglePlayLifetime, service.CurrentPremiumSource);
        }

        [Fact]
        public async Task StoreEntitlement_LifetimePlusActiveSubscription_BothFlagsActive()
        {
            // Kalıcı paket alındıktan sonra aylık abonelik Google Play'de
            // yenilenmeye devam edebilir — iki hakkın varlığı da raporlanmalı
            // (upsell bu durumda iptal hatırlatması gösterir).
            var store = CreateStoreMock(new StoreEntitlement
            {
                HasLifetimePremium = true,
                SubscriptionExpiresAtUtc = DateTime.UtcNow.AddDays(10)
            });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.True(service.HasLifetimePremium);
            Assert.True(service.HasActiveStoreSubscription);
            Assert.Equal(PremiumSource.GooglePlayLifetime, service.CurrentPremiumSource);
        }

        [Fact]
        public async Task StoreEntitlement_ActiveTrialSubscription_IsMarkedAsTrial()
        {
            // Gerçek trial tespiti backend'den gelir (StoreEntitlement.IsTrialPeriod)
            // — ücretli kullanıcı trial gibi görünmez, gerçek trial görünür.
            var expires = DateTime.UtcNow.AddDays(14);
            var store = CreateStoreMock(new StoreEntitlement
            {
                SubscriptionExpiresAtUtc = expires,
                IsTrialPeriod = true
            });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.Equal(PremiumSource.GooglePlaySubscription, service.CurrentPremiumSource);
            Assert.True(service.GetCurrentSubscription().IsTrialPeriod);
        }

        [Fact]
        public async Task StoreEntitlement_PromoWinsOverTrialSubscription_NotMarkedAsTrial()
        {
            // Kazanan kaynak promosyon olduğunda trial bayrağı taşınmaz —
            // IsTrialPeriod yalnızca gerçek trial'ı açıklar.
            var settings = new TestSettingsService
            {
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddDays(20),
                ActivePromoCode = "PROMO20D"
            };
            var store = CreateStoreMock(new StoreEntitlement
            {
                SubscriptionExpiresAtUtc = DateTime.UtcNow.AddDays(5),
                IsTrialPeriod = true
            });

            var service = CreateService(CreateFreeEditionMock().Object, settings: settings, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.Equal(PremiumSource.Promo, service.CurrentPremiumSource);
            Assert.False(service.GetCurrentSubscription().IsTrialPeriod);
        }

        [Fact]
        public async Task StoreEntitlement_PromoOnly_SourceIsPromo()
        {
            var settings = new TestSettingsService
            {
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                ActivePromoCode = "PROMO7D"
            };

            var service = CreateService(CreateFreeEditionMock().Object, settings: settings);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.Equal(PremiumSource.Promo, service.CurrentPremiumSource);
            Assert.False(service.GetCurrentSubscription().IsTrialPeriod);
        }

        // ==========================================
        // Expiry timer (UI otomatik güncelleme)
        // ==========================================

        [Fact]
        public async Task ExpiryTimer_WhenPremiumExpires_TriggersSubscriptionChanged()
        {
            // Bitişe kısa süre kalan süreli Premium: timer bitiş anında
            // SubscriptionChanged tetiklemeli ve IsPremium false olmalı.
            var settings = new TestSettingsService
            {
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddSeconds(1),
                ActivePromoCode = "PROMO-1S"
            };

            var service = CreateService(CreateFreeEditionMock().Object, settings: settings, store: null);
            await service.RefreshSubscriptionStatusAsync();
            Assert.True(service.IsPremium);

            var subscriptionChanged = 0;
            service.SubscriptionChanged += () => Interlocked.Increment(ref subscriptionChanged);

            // Timer ~1 saniyede tetiklenmeli; 5 sn içinde bekle.
            await WaitUntilAsync(() => !service.IsPremium, timeoutMs: 5000);

            Assert.False(service.IsPremium);
            Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
            Assert.True(subscriptionChanged > 0, "Expiry timer SubscriptionChanged tetiklemeli.");
        }

        [Fact]
        public async Task ExpiryTimer_WhenPlaySubscriptionRenews_DoesNotPublishIntermediateFreeState()
        {
            var firstExpiry = DateTime.UtcNow.AddMilliseconds(700);
            var renewedExpiry = DateTime.UtcNow.AddMinutes(5);
            var queryCount = 0;
            var store = new Mock<IStorePurchaseService>();
            store.Setup(m => m.IsSupported).Returns(true);
            store.Setup(m => m.GetEntitlementAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new StoreEntitlement
                {
                    SubscriptionExpiresAtUtc =
                        Interlocked.Increment(ref queryCount) == 1
                            ? firstExpiry
                            : renewedExpiry
                });

            using var service = new LicenseService(
                CreateFreeEditionMock().Object,
                new TestSettingsService(),
                new HttpClient(new NotFoundHttpMessageHandler()),
                securityService: new SecurityService(),
                storePurchaseService: store.Object,
                deferInitialStoreRefresh: true);

            await service.RefreshSubscriptionStatusAsync();
            Assert.True(service.IsPremium);

            var observedFree = 0;
            service.SubscriptionChanged += () =>
            {
                if (!service.IsPremium)
                {
                    Interlocked.Exchange(ref observedFree, 1);
                }
            };

            await WaitUntilAsync(() => Volatile.Read(ref queryCount) >= 2, timeoutMs: 5000);
            await WaitUntilAsync(
                () => service.PremiumExpiresAtUtc == renewedExpiry,
                timeoutMs: 5000);

            Assert.True(service.IsPremium);
            Assert.Equal(PremiumSource.GooglePlaySubscription, service.CurrentPremiumSource);
            Assert.Equal(0, Volatile.Read(ref observedFree));
        }

        [Fact]
        public async Task ExpiryTimer_WhenPlaySubscriptionDoesNotRenew_PublishesFreeAfterRefresh()
        {
            var firstExpiry = DateTime.UtcNow.AddMilliseconds(700);
            var queryCount = 0;
            var store = new Mock<IStorePurchaseService>();
            store.Setup(m => m.IsSupported).Returns(true);
            store.Setup(m => m.GetEntitlementAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                    Interlocked.Increment(ref queryCount) == 1
                        ? new StoreEntitlement { SubscriptionExpiresAtUtc = firstExpiry }
                        : StoreEntitlement.None);

            using var service = new LicenseService(
                CreateFreeEditionMock().Object,
                new TestSettingsService(),
                new HttpClient(new NotFoundHttpMessageHandler()),
                securityService: new SecurityService(),
                storePurchaseService: store.Object,
                deferInitialStoreRefresh: true);

            await service.RefreshSubscriptionStatusAsync();
            Assert.True(service.IsPremium);

            await WaitUntilAsync(() => Volatile.Read(ref queryCount) >= 2, timeoutMs: 5000);
            await WaitUntilAsync(() => !service.IsPremium, timeoutMs: 5000);

            Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
            Assert.Equal(PremiumSource.None, service.CurrentPremiumSource);
        }

        [Fact]
        public async Task ExpiryTimer_NotScheduled_ForLifetimeOrFree()
        {
            // Kalıcı paket: süre yok → timer kurulmaz (beklemede state değişmez).
            var store = CreateStoreMock(new StoreEntitlement { HasLifetimePremium = true });
            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();
            Assert.True(service.IsPremium);

            await Task.Delay(150);
            Assert.True(service.IsPremium);
            Assert.Null(service.PremiumExpiresAtUtc);
        }

        [Fact]
        public async Task ExpiryTimer_LongExpiry_DoesNotFireEarly()
        {
            // 1 saatten uzun süre: timer 24 saat dilimine bölünür, erken ateşlenmez.
            var settings = new TestSettingsService
            {
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddHours(2),
                ActivePromoCode = "PROMO-2H"
            };

            var service = CreateService(CreateFreeEditionMock().Object, settings: settings, store: null);
            await service.RefreshSubscriptionStatusAsync();
            Assert.True(service.IsPremium);

            await Task.Delay(200);
            Assert.True(service.IsPremium);
        }

        [Fact]
        public async Task StoreEntitlement_None_IsFree()
        {
            var store = CreateStoreMock(StoreEntitlement.None);

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            Assert.False(service.IsPremium);
            Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
        }

        [Fact]
        public async Task StoreEntitlement_StoreNull_BehavesAsDesktop()
        {
            // Store servisi yok (masaüstü): abonelik durumu promo üzerinden belirlenir.
            var settings = new TestSettingsService
            {
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                ActivePromoCode = "PROMO7D"
            };

            var service = CreateService(CreateFreeEditionMock().Object, settings: settings, store: null);
            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.NotNull(service.PremiumExpiresAtUtc);
        }

        [Fact]
        public async Task RefreshSubscriptionStatusAsync_ReQueriesStoreOnEachCall()
        {
            // İnceleme #8: Resume/focus'da çağrılan RefreshSubscriptionStatusAsync
            // yalnızca settings senkronlamamalı; gerçek mağaza sorgusu yapmalı.
            // Arka planda tamamlanan satın alma / refund / iptal bu çağrıda
            // yakalanır (constructor'daki ilk sorguya güvenilmez).
            var storeMock = CreateStoreMock(StoreEntitlement.None);
            var service = CreateService(CreateFreeEditionMock().Object, store: storeMock.Object);
            await service.RefreshSubscriptionStatusAsync();
            Assert.False(service.IsPremium);

            // Store sonucu değişti (ör. başka cihazda satın alma tamamlandı).
            storeMock.Setup(m => m.GetEntitlementAsync(It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(new StoreEntitlement { HasLifetimePremium = true }));

            await service.RefreshSubscriptionStatusAsync();

            Assert.True(service.IsPremium);
            Assert.Null(service.PremiumExpiresAtUtc);
            // İkinci çağrı da mağazayı yeniden sorguladı (settings sync değil).
            // Sayım: constructor fire-and-forget (1) + ilk çağrı (1) + ikinci çağrı (1).
            storeMock.Verify(
                m => m.GetEntitlementAsync(It.IsAny<System.Threading.CancellationToken>()),
                Times.AtLeast(3));
        }

        [Fact]
        public async Task StoreEntitlement_EntitlementChangedEvent_RefreshesSubscription()
        {
            var storeMock = CreateStoreMock(StoreEntitlement.None);
            var service = CreateService(CreateFreeEditionMock().Object, store: storeMock.Object);
            await service.RefreshSubscriptionStatusAsync();
            Assert.False(service.IsPremium);

            // EntitlementChanged → servis yeniden sorgular ve Premium olur.
            storeMock.Setup(m => m.GetEntitlementAsync(It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(new StoreEntitlement { HasLifetimePremium = true }));
            storeMock.Raise(m => m.EntitlementChanged += null, null, EventArgs.Empty);

            await WaitUntilAsync(() => service.IsPremium);
            Assert.True(service.IsPremium);
            Assert.Null(service.PremiumExpiresAtUtc);
        }

        // ==========================================
        // StartPurchaseFlowAsync (store-aware)
        // ==========================================

        [Fact]
        public async Task StartPurchaseFlowAsync_StoreSupported_LaunchesSubscriptionProduct()
        {
            var storeMock = new Mock<IStorePurchaseService>();
            storeMock.Setup(m => m.IsSupported).Returns(true);
            storeMock.Setup(m => m.GetEntitlementAsync(It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(StoreEntitlement.None));
            storeMock.Setup(m => m.GetProductsAsync(It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<StoreProduct>>(new[]
                {
                    new StoreProduct
                    {
                        ProductId = "noctra_premium_lifetime",
                        Kind = StoreProductKind.Lifetime,
                        Title = "Lifetime",
                        Price = "$49.99"
                    },
                    new StoreProduct
                    {
                        ProductId = "noctra_premium_monthly",
                        Kind = StoreProductKind.Subscription,
                        Title = "Monthly",
                        Price = "$4.99",
                        BillingPeriod = "P1M",
                        OfferToken = "offer-token-1"
                    }
                }));
            storeMock.Setup(m => m.LaunchPurchaseAsync(It.IsAny<StoreProduct>(), It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(StorePurchaseResult.Ok()));

            var service = CreateService(CreateFreeEditionMock().Object, store: storeMock.Object);

            var result = await service.StartPurchaseFlowAsync(SubscriptionTier.Premium);

            Assert.True(result);
            // Abonelik ürünü önceliklidir (lifetime değil).
            storeMock.Verify(m => m.LaunchPurchaseAsync(
                It.Is<StoreProduct>(p => p.Kind == StoreProductKind.Subscription),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task StartPurchaseFlowAsync_StoreSupportedButNoProducts_ReturnsFalse()
        {
            var storeMock = new Mock<IStorePurchaseService>();
            storeMock.Setup(m => m.IsSupported).Returns(true);
            storeMock.Setup(m => m.GetEntitlementAsync(It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(StoreEntitlement.None));
            storeMock.Setup(m => m.GetProductsAsync(It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<StoreProduct>>(Array.Empty<StoreProduct>()));

            var service = CreateService(CreateFreeEditionMock().Object, store: storeMock.Object);

            var result = await service.StartPurchaseFlowAsync(SubscriptionTier.Premium);

            Assert.False(result);
            storeMock.Verify(m => m.LaunchPurchaseAsync(
                It.IsAny<StoreProduct>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task StartPurchaseFlowAsync_StoreNotSupported_UsesPlatformUris()
        {
            var storeMock = CreateStoreMock(StoreEntitlement.None, isSupported: false);

            var platformMock = new Mock<IPlatformActionService>();
            platformMock.Setup(m => m.OpenUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var editionMock = CreateFreeEditionMock();
            editionMock.Setup(m => m.PremiumStoreLaunchUri).Returns("https://example.com/premium");

            var service = CreateService(editionMock.Object, store: storeMock.Object, platformActions: platformMock.Object);

            var result = await service.StartPurchaseFlowAsync(SubscriptionTier.Premium);

            Assert.True(result);
            platformMock.Verify(m => m.OpenUrlAsync("https://example.com/premium", It.IsAny<CancellationToken>()), Times.Once);
            // Store desteklenmediği için satın alma akışı çağrılmaz.
            storeMock.Verify(m => m.LaunchPurchaseAsync(
                It.IsAny<StoreProduct>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task StartPurchaseFlowAsync_EditionLockedPremium_ReturnsFalse()
        {
            var editionMock = new Mock<IAppEditionService>();
            editionMock.Setup(m => m.IsFreeEdition).Returns(false);
            editionMock.Setup(m => m.IsPremiumEdition).Returns(true);

            var storeMock = CreateStoreMock(StoreEntitlement.None);
            var service = CreateService(editionMock.Object, store: storeMock.Object);

            var result = await service.StartPurchaseFlowAsync(SubscriptionTier.Premium);

            Assert.False(result);
            storeMock.Verify(m => m.LaunchPurchaseAsync(
                It.IsAny<StoreProduct>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        // ==========================================
        // Pending purchase (onay bekleyen ödeme)
        // ==========================================

        [Fact]
        public async Task StoreEntitlement_PendingPurchase_DoesNotGrantPremium_AndExposesPendingFlag()
        {
            var store = CreateStoreMock(new StoreEntitlement { HasPendingPurchase = true });

            var service = CreateService(CreateFreeEditionMock().Object, store: store.Object);
            await service.RefreshSubscriptionStatusAsync();

            // Pending satın alma PREMIUM VERMEZ — hak yalnızca PURCHASED +
            // backend doğrulamasıyla açılır; bayrak yalnızca UI bildirimi içindir.
            Assert.False(service.IsPremium);
            Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
            Assert.Null(service.PremiumExpiresAtUtc);
            Assert.True(service.HasPendingStorePurchase);
        }

        [Fact]
        public async Task StoreEntitlement_PendingFlagClears_WhenPurchaseResolves()
        {
            var isPending = true;
            var storeMock = new Mock<IStorePurchaseService>();
            storeMock.Setup(m => m.IsSupported).Returns(true);
            storeMock.Setup(m => m.GetEntitlementAsync(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(new StoreEntitlement
                {
                    HasPendingPurchase = Volatile.Read(ref isPending)
                }));

            var service = CreateService(CreateFreeEditionMock().Object, store: storeMock.Object);

            // Constructor'ın fire-and-forget refresh'i + ilk açık refresh.
            await WaitUntilAsync(() => service.HasPendingStorePurchase);

            isPending = false;
            await service.RefreshSubscriptionStatusAsync();

            // Ödeme çözülünce bayrak temizlenir; hak yine açılmaz.
            await WaitUntilAsync(() => !service.HasPendingStorePurchase);
            Assert.False(service.IsPremium);
        }

        // ==========================================
        // Helpers
        // ==========================================

        /// <summary>Promosyon uç noktasına ulaşmayan 404 dönen basit handler.</summary>
        private sealed class NotFoundHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}")
                });
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(20);
            }

            Assert.True(condition(), "Condition was not met within the timeout.");
        }

        private sealed class TestSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public event Action? SettingsChanged;

            public DateTime? PromoPremiumExpiresAtUtc
            {
                get => Settings.PromoPremiumExpiresAtUtc;
                set => Settings.PromoPremiumExpiresAtUtc = value;
            }

            public string? ActivePromoCode
            {
                get => Settings.ActivePromoCode;
                set => Settings.ActivePromoCode = value;
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
}
