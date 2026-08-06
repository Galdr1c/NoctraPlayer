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
        public async Task ApplyPromoCodeAsync_WhenConfigUrlMissing_ShouldReturnServiceUnavailableError()
        {
            using var _ = TemporarilyClearPromoCodesUrl();
            var service = CreateLicenseService();

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ServiceUnavailable, result.Kind);
            Assert.Contains("şu anda kullanılamıyor", result.Message);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenRemoteConfigReturnsServerError_ShouldReturnServiceUnavailable()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            using var httpClient = CreateHttpClient(HttpStatusCode.InternalServerError, "{}");
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ServiceUnavailable, result.Kind);
            Assert.Contains("kullanılamıyor", result.Message);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenRemoteConfigReturnsNotFound_ShouldReturnConfigurationInvalid()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            using var httpClient = CreateHttpClient(HttpStatusCode.NotFound, "{}");
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ConfigurationInvalid, result.Kind);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenNetworkRequestFails_ShouldReturnOffline()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            using var httpClient = new HttpClient(new StaticHttpMessageHandler(
                HttpStatusCode.OK,
                "{}",
                throwException: new HttpRequestException("no route to host")));
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.Offline, result.Kind);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenRequestTimesOut_ShouldReturnTimeout()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            using var httpClient = new HttpClient(new StaticHttpMessageHandler(
                HttpStatusCode.OK,
                "{}",
                throwException: new TaskCanceledException("timed out")));
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.Timeout, result.Kind);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigJsonIsBroken_ShouldReturnConfigurationInvalid()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, "this is not json");
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ConfigurationInvalid, result.Kind);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigHasDuplicateNormalizedCodes_ShouldRejectConfig()
        {
            // "AB CD" ve "ABCD" normalize edilince aynı koda dönüşür; JSON
            // sırası davranışı belirlememeli, tüm yapılandırma reddedilmeli.
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition { Code = "AB CD", DurationDays = 7, IsActive = true },
                    new PromoCodeDefinition { Code = "ABCD", DurationDays = 7, IsActive = true }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("ABCD");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ConfigurationInvalid, result.Kind);
            Assert.False(service.IsPremium);
            Assert.Null(settings.Settings.PromoGrant);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigHasEmptyCodeEntry_ShouldRejectConfig()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition { Code = "   ", DurationDays = 7, IsActive = true }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("ABCD");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ConfigurationInvalid, result.Kind);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigHasUnsupportedSchemaVersion_ShouldRejectConfig()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            const string json = """
                {
                  "schemaVersion": 2,
                  "codes": [ { "code": "ABCD", "durationDays": 7, "isActive": true } ]
                }
                """;
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("ABCD");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ConfigurationInvalid, result.Kind);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigResponseExceedsSizeLimit_ShouldRejectConfig()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition { Code = "ABCD", DurationDays = 7, IsActive = true }
                }
            });
            // 300 KB'lik yanıt 256 KB sınırını aşar.
            var oversized = json + new string(' ', 300 * 1024);
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, oversized);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("ABCD");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ConfigurationInvalid, result.Kind);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigHasSchemaVersionOne_ShouldAcceptConfig()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            const string json = """
                {
                  "schemaVersion": 1,
                  "codes": [ { "code": "ABCD", "durationDays": 7, "isActive": true } ]
                }
                """;
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("ABCD");

            Assert.True(result.Success);
            Assert.True(service.IsPremium);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigStreamExceedsSizeLimit_ShouldRejectConfig()
        {
            // Content-Length başlığı olmayan (chunked benzeri) gövde: sınır
            // akış okumasında uygulanmalı, yalnızca başlık ön kontrolüne değil.
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition { Code = "ABCD", DurationDays = 7, IsActive = true }
                }
            });
            var oversized = Encoding.UTF8.GetBytes(json + new string(' ', 300 * 1024));
            using var httpClient = new HttpClient(new StaticHttpMessageHandler(
                HttpStatusCode.OK,
                "",
                contentOverride: new NoLengthContent(oversized)));
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("ABCD");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ConfigurationInvalid, result.Kind);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenConfigResponseIsHtml_ShouldRejectConfig()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            using var httpClient = new HttpClient(new StaticHttpMessageHandler(
                HttpStatusCode.OK,
                "<html><body>sign in</body></html>",
                contentType: "text/html"));
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("ABCD");

            Assert.False(result.Success);
            Assert.Equal(PromoCodeResultKind.ConfigurationInvalid, result.Kind);
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
            Assert.Contains("şu anda kullanılamıyor", result.Message);
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

            // Fail-closed davranış doğru; ancak sessiz kalmamalı — ayarlar ekranı
            // bu bayrağı görüp bozuk grant uyarısı gösterebilmeli.
            Assert.True(service.IsPromoGrantCorrupted);
        }

        [Fact]
        public void IsPromoGrantCorrupted_WhenGrantIsValid_ShouldBeFalse()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition { Code = "PROMO-EXAMPLE-7D", DurationDays = 7, IsActive = true }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = service.ApplyPromoCodeAsync("PROMO-EXAMPLE-7D").GetAwaiter().GetResult();

            Assert.True(result.Success);
            Assert.False(service.IsPromoGrantCorrupted);
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
            Assert.Equal("The promotion service is currently unavailable. Please make sure your app is up to date and try again later.", result.Message);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenSaveFailsAndNoPreviousGrant_ShouldFailAndKeepMemoryClean()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService { ThrowOnSave = true };
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

            Assert.False(result.Success);
            Assert.Contains("kaydedilemedi", result.Message);
            Assert.Null(settings.Settings.PromoGrant);
            Assert.False(service.IsPremium);
            Assert.Null(service.ActivePromoCode);
            Assert.Null(service.PromoPremiumExpiresAtUtc);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenSaveFailsAndPreviousGrantExists_ShouldRestorePreviousGrant()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition { Code = "PROMO-FIRST-7D", DurationDays = 7, IsActive = true },
                    new PromoCodeDefinition { Code = "PROMO-SECOND-7D", DurationDays = 7, IsActive = true }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var firstResult = await service.ApplyPromoCodeAsync("PROMO-FIRST-7D");
            Assert.True(firstResult.Success);

            var previousGrant = settings.Settings.PromoGrant;
            var previousExpiry = service.PromoPremiumExpiresAtUtc;
            settings.ThrowOnSave = true;

            var secondResult = await service.ApplyPromoCodeAsync("PROMO-SECOND-7D");

            Assert.False(secondResult.Success);
            Assert.Contains("kaydedilemedi", secondResult.Message);
            Assert.Equal("PROMO-FIRST-7D", service.ActivePromoCode);
            Assert.Equal(previousExpiry, service.PromoPremiumExpiresAtUtc);
            Assert.True(service.IsPremium);

            // Şifrelenmiş grant her yazımda yeni IV ile üretildiğinden string
            // eşitliği doğrulanamaz; rollback'i davranışsal doğrula:
            // kaydedilemeyen ikinci kod redeemed listesine girmedi.
            settings.ThrowOnSave = false;
            var retrySecond = await service.ApplyPromoCodeAsync("PROMO-SECOND-7D");
            Assert.True(retrySecond.Success, $"Rollback sonrası ikinci kod tekrar denenebilir olmalı. Mesaj: {retrySecond.Message}");

            var retryFirst = await service.ApplyPromoCodeAsync("PROMO-FIRST-7D");
            Assert.False(retryFirst.Success, "Rollback, ilk kodun kullanılmış durumunu korumalı.");
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenDurationExceedsMaximum_ShouldFailWithoutException()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition
                    {
                        Code = "PROMO-TOO-LONG",
                        DurationDays = int.MaxValue,
                        IsActive = true
                    }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-TOO-LONG");

            Assert.False(result.Success);
            Assert.Contains("süresi geçersiz", result.Message);
            Assert.Null(settings.Settings.PromoGrant);
            Assert.False(service.IsPremium);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenDurationAtMaximum_ShouldSucceed()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition
                    {
                        Code = "PROMO-MAX-365",
                        DurationDays = 365,
                        IsActive = true
                    }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var result = await service.ApplyPromoCodeAsync("PROMO-MAX-365");

            Assert.True(result.Success);
            Assert.True(service.IsPremium);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenAccumulatedDurationExceedsLimit_ShouldFail()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition { Code = "PROMO-ACC-1", DurationDays = 365, IsActive = true },
                    new PromoCodeDefinition { Code = "PROMO-ACC-2", DurationDays = 365, IsActive = true },
                    new PromoCodeDefinition { Code = "PROMO-ACC-3", DurationDays = 365, IsActive = true }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var first = await service.ApplyPromoCodeAsync("PROMO-ACC-1");
            Assert.True(first.Success);

            // 365 + 365 = 730 → tam sınıra ulaşır, kabul edilir
            var second = await service.ApplyPromoCodeAsync("PROMO-ACC-2");
            Assert.True(second.Success);

            // 730 + 365 = 1095 > 730 → reddedilir
            var third = await service.ApplyPromoCodeAsync("PROMO-ACC-3");
            Assert.False(third.Success);
            Assert.Contains("üst sınırına ulaşıldı", third.Message);
            Assert.Equal("PROMO-ACC-2", service.ActivePromoCode);
        }

        [Fact]
        public async Task ApplyPromoCodeAsync_WhenCalledConcurrently_BothCodesSurvive()
        {
            using var _ = TemporarilySetPromoCodesUrl("https://example.com/noctra-promo-codes.json");
            var settings = new TestSettingsService();
            var json = JsonSerializer.Serialize(new PromoCodeConfiguration
            {
                Codes =
                {
                    new PromoCodeDefinition { Code = "PROMO-CONC-A", DurationDays = 7, IsActive = true },
                    new PromoCodeDefinition { Code = "PROMO-CONC-B", DurationDays = 30, IsActive = true }
                }
            });
            using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
            var service = CreateLicenseService(settings, httpClient);

            var results = await Task.WhenAll(
                service.ApplyPromoCodeAsync("PROMO-CONC-A"),
                service.ApplyPromoCodeAsync("PROMO-CONC-B"));

            Assert.All(results, r => Assert.True(r.Success, r.Message));

            // Seri işleme ile süreler birikir: 7 + 30 = 37 gün (hangi sıra olursa olsun)
            Assert.NotNull(service.PromoPremiumExpiresAtUtc);
            Assert.InRange(
                service.PromoPremiumExpiresAtUtc.Value - DateTime.UtcNow,
                TimeSpan.FromDays(36),
                TimeSpan.FromDays(38));

            // İkisi de redeemed geçmişinde olmalı: tekrar deneme "zaten kullanılmış" döner
            var retryA = await service.ApplyPromoCodeAsync("PROMO-CONC-A");
            var retryB = await service.ApplyPromoCodeAsync("PROMO-CONC-B");
            Assert.False(retryA.Success);
            Assert.False(retryB.Success);
            Assert.Contains("kullanılmış", retryA.Message);
            Assert.Contains("kullanılmış", retryB.Message);
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
            public bool ThrowOnSave { get; set; }
            public AppSettings Settings { get; } = new();
            public event Action? SettingsChanged;
            public Task LoadAsync() => Task.CompletedTask;
            public Task LoadProfileSettingsAsync(int profileId) => Task.CompletedTask;
            public Task<AppSettings?> PeekProfileSettingsAsync(int profileId) => Task.FromResult<AppSettings?>(Settings);
            public Task SaveAsync()
            {
                if (ThrowOnSave)
                {
                    throw new SettingsPersistenceException(
                        "Simulated write failure",
                        new System.IO.IOException("disk full"));
                }

                SettingsChanged?.Invoke();
                return Task.CompletedTask;
            }
            public void NotifySettingsChanged() => SettingsChanged?.Invoke();
            public void ResetToDefaults() => SettingsChanged?.Invoke();
            public Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds) => Task.FromResult(0);
        }

        private sealed class StaticHttpMessageHandler(
            HttpStatusCode statusCode,
            string content,
            string? contentType = "application/json",
            Exception? throwException = null,
            HttpContent? contentOverride = null) : HttpMessageHandler
        {
            public static bool LastRequestNoCache { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestNoCache = request.Headers.CacheControl?.NoCache == true;

                if (throwException is not null)
                {
                    throw throwException;
                }

                var response = new HttpResponseMessage(statusCode)
                {
                    Content = contentOverride ?? (contentType is null
                        ? new StringContent(content, Encoding.UTF8)
                        : new StringContent(content, Encoding.UTF8, contentType))
                };
                return Task.FromResult(response);
            }
        }

        /// <summary>
        /// Content-Length üretmeyen (chunked benzeri) gövde — akış sınırı yolunu test eder.
        /// </summary>
        private sealed class NoLengthContent : HttpContent
        {
            private readonly byte[] _bytes;

            public NoLengthContent(byte[] bytes) => _bytes = bytes;

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
                => stream.WriteAsync(_bytes, 0, _bytes.Length);

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }

        private sealed class RestoreEnvironmentVariable(string name, string? value) : IDisposable
        {
            public void Dispose() => Environment.SetEnvironmentVariable(name, value);
        }
    }
}
