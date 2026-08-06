using Xunit;
using Noctra.Services;
using Noctra.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using Noctra.Services.Interfaces;

namespace Noctra.Tests
{
    public class LicenseServiceTests
    {
        private readonly LicenseService _licenseService;

        public LicenseServiceTests()
        {
            var editionMock = new Moq.Mock<Noctra.Services.Interfaces.IAppEditionService>();
            editionMock.Setup(m => m.IsFreeEdition).Returns(true);
            _licenseService = new LicenseService(editionMock.Object);
        }

        [Fact]
        public void DefaultTier_ShouldBeFree()
        {
            Assert.Equal(SubscriptionTier.Free, _licenseService.CurrentTier);
            Assert.False(_licenseService.IsPremium);
        }

        [Theory]
        [InlineData(LicenseService.Features.AdFree, false)]
        [InlineData(LicenseService.Features.SleepTimer, false)]
        public void FreeTier_ShouldNotHavePremiumFeatures(string feature, bool expected)
        {
            _licenseService.SetTierForTesting(SubscriptionTier.Free);
            Assert.Equal(expected, _licenseService.IsFeatureAvailable(feature));
        }

        [Fact]
        public void PremiumTier_ShouldHaveAllFeatures()
        {
            _licenseService.SetTierForTesting(SubscriptionTier.Premium);
            
            Assert.True(_licenseService.IsFeatureAvailable(LicenseService.Features.AdFree));
            Assert.True(_licenseService.IsFeatureAvailable(LicenseService.Features.SleepTimer));
            Assert.True(_licenseService.IsFeatureAvailable(LicenseService.Features.EpgAutoRefresh));
        }

        [Theory]
        [InlineData(LicenseService.Limits.Profiles, 4, true)]  // Limit is 5
        [InlineData(LicenseService.Limits.Profiles, 5, false)]
        public void FreeTier_ShouldRespectLimits(string limit, int currentCount, bool expected)
        {
            _licenseService.SetTierForTesting(SubscriptionTier.Free);
            Assert.Equal(expected, _licenseService.IsWithinLimit(limit, currentCount));
        }

        [Fact]
        public void ActivatePremium_ShouldChangeTierAndNotify()
        {
            bool notified = false;
            _licenseService.SubscriptionChanged += () => notified = true;

            _licenseService.ActivatePremium();

            Assert.Equal(SubscriptionTier.Premium, _licenseService.CurrentTier);
            Assert.True(_licenseService.IsPremium);
            Assert.True(notified);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigUrlMissing_ShouldReturnConfigurationError()
        {
            using var _ = TemporarilyClearPromoCodesUrl();
            var service = CreateLicenseService();

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Contains("yapılandırması bulunamadı", result.Message);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenRemoteConfigFails_ShouldReturnLoadError()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            using var httpClient = CreateHttpClient(HttpStatusCode.InternalServerError, "{}");
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Contains("yüklenemedi", result.Message);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenEnvConfigUrlHasCode_ShouldActivatePromoPremium()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition
                    {
                        Code = "PROMO-EXAMPLE-7D",
                        DurationDays = 7,
                        IsActive = true
                    }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.True(result.Success);
            Assert.True(service.IsPremium);
            Assert.Equal("PROMO-EXAMPLE-7D", service.ActivePromoCode);
            Assert.NotNull(service.PromoPremiumExpiresAtUtc);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenOnlySettingsConfigUrlExists_ShouldNotUseUntrustedUrl()
        {
            // Güvenlik: settings.json kullanıcı tarafından düzenlenebilir —
            // oradaki PromoCodeConfigUrl asla promosyon kaynağı olarak
            // kullanılmamalı; aksi halde kullanıcı kendi JSON'unu işaret
            // edip kendine Premium açabilirdi.
            using var _ = TemporarilyClearPromoCodesUrl();
            var settings = new TestSettingsService
            {
                Settings = { PromoCodeConfigUrl = "https://example.com/noctra-promo-codes.json" }
            };
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition
                    {
                        Code = "PROMO-EXAMPLE-7D",
                        DurationDays = 36500,
                        IsActive = true,
                        AllowReuse = true
                    }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Contains("yapılandırması bulunamadı", result.Message);
            Assert.False(service.IsPremium);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenCodeIsAccepted_ShouldPersistEncryptedPromoGrantOnly()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition
                    {
                        Code = "PROMO-EXAMPLE-7D",
                        DurationDays = 7,
                        IsActive = true
                    }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.True(result.Success);
            Assert.True(service.IsPremium);
            Assert.False(string.IsNullOrWhiteSpace(settings.Settings.PromoGrant));
            Assert.Null(settings.Settings.ActivePromoCode);
            Assert.Null(settings.Settings.PromoPremiumExpiresAtUtc);
            Assert.Empty(settings.Settings.RedeemedPromoCodes);
        }

        [Fact]
        public void CurrentTier_WhenPromoGrantIsTampered_ShouldStayFreeAndNotThrow()
        {
            var settings = new TestSettingsService();
            settings.Settings.PromoGrant = "tampered-value";
            var service = CreateLicenseService(settings);

            Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
            Assert.False(service.IsPremium);
            Assert.Null(service.ActivePromoCode);
            Assert.Null(service.PromoPremiumExpiresAtUtc);
        }

        [Fact]
        public void CurrentTier_WhenLegacyPlainPromoStateExists_ShouldImportEncryptedGrant()
        {
            var settings = new TestSettingsService();
            settings.Settings.ActivePromoCode = "PROMO-EXAMPLE-7D";
            settings.Settings.PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddDays(7);
            settings.Settings.RedeemedPromoCodes.Add("PROMO-EXAMPLE-7D");
            var service = CreateLicenseService(settings);

            Assert.True(service.IsPremium);
            Assert.Equal("PROMO-EXAMPLE-7D", service.ActivePromoCode);
            Assert.False(string.IsNullOrWhiteSpace(settings.Settings.PromoGrant));
            Assert.Null(settings.Settings.ActivePromoCode);
            Assert.Null(settings.Settings.PromoPremiumExpiresAtUtc);
            Assert.Empty(settings.Settings.RedeemedPromoCodes);
        }

        [Fact]
        public void AppSettingsJson_WhenPromoStateExists_ShouldOnlySerializeEncryptedGrant()
        {
            var settings = new AppSettings
            {
                PromoGrant = "encrypted-grant",
                ActivePromoCode = "PROMO-EXAMPLE-7D",
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                RedeemedPromoCodes = new List<string> { "PROMO-EXAMPLE-7D" }
            };

            var json = JsonSerializer.Serialize(settings);

            Assert.Contains("PromoGrant", json);
            Assert.DoesNotContain("ActivePromoCode", json);
            Assert.DoesNotContain("PromoPremiumExpiresAtUtc", json);
            Assert.DoesNotContain("RedeemedPromoCodes", json);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenFetchingPromoCodes_ShouldRequestNoCache()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition
                    {
                        Code = "NOC-AAAA-QQQQ-WWWW",
                        DurationDays = 1,
                        IsActive = true
                    }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("NOC-AAAA-QQQQ-WWWW");

            Assert.True(result.Success);
            Assert.True(StaticHttpMessageHandler.LastRequestNoCache);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenLocalizationIsEnglish_ShouldReturnEnglishMessage()
        {
            using var _ = TemporarilyClearPromoCodesUrl();
            var localization = new LocalizationService();
            localization.SetLanguage("en");
            var service = CreateLicenseService(localizationService: localization);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Equal("Promo code configuration was not found. Make sure the promo code URL is configured by the app administrator.", result.Message);
        }

        private static LicenseService CreateLicenseService(
            TestSettingsService? settings = null,
            HttpClient? httpClient = null,
            ILocalizationService? localizationService = null)
        {
            var editionMock = new Mock<IAppEditionService>();
            editionMock.Setup(m => m.IsFreeEdition).Returns(true);
            return new LicenseService(
                editionMock.Object,
                settings ?? new TestSettingsService(),
                httpClient ?? new HttpClient(new StaticHttpMessageHandler(HttpStatusCode.NotFound, "{}")),
                localizationService);
        }

        private static HttpClient CreateHttpClient(HttpStatusCode statusCode, string content) =>
            new(new StaticHttpMessageHandler(statusCode, content));

        private static IDisposable TemporarilyClearPromoCodesUrl()
        {
            var previousValue = Environment.GetEnvironmentVariable("NOCTRA_PROMO_CODES_URL");
            Environment.SetEnvironmentVariable("NOCTRA_PROMO_CODES_URL", null);
            return new RestoreEnvironmentVariable("NOCTRA_PROMO_CODES_URL", previousValue);
        }

        private static IDisposable TemporarilySetPromoCodesUrl(string url)
        {
            var previousValue = Environment.GetEnvironmentVariable("NOCTRA_PROMO_CODES_URL");
            Environment.SetEnvironmentVariable("NOCTRA_PROMO_CODES_URL", url);
            return new RestoreEnvironmentVariable("NOCTRA_PROMO_CODES_URL", previousValue);
        }

        private sealed class TestSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public event Action? SettingsChanged;
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

        private sealed class StaticHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
        {
            public static bool LastRequestNoCache { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestNoCache = request.Headers.CacheControl?.NoCache == true;
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            }
        }

        private sealed class RestoreEnvironmentVariable(string name, string? value) : IDisposable
        {
            public void Dispose() => Environment.SetEnvironmentVariable(name, value);
        }
    }
}
