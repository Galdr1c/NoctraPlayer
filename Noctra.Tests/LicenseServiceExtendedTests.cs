using System;
using Noctra.Models;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// Mevcut LicenseServiceTests'in kapsamadığı durumları test eder:
    ///
    /// - Bilinmeyen feature → false
    /// - ResumePlayback feature (pending implementasyon)
    /// - Multiview free tier → false
    /// - Limitlerin sınır değerleri (boundary value analysis)
    /// - DeactivatePremium (premium → free geçişi)
    /// - SubscriptionChanged event birden fazla subscriber ile çalışıyor
    /// - IsWithinLimit sonsuz limit (Premium) her zaman true
    /// </summary>
    public class LicenseServiceExtendedTests
    {
        // ─── Bilinmeyen Feature ───────────────────────────────────────────────────────

        [Fact]
        public void IsFeatureAvailable_UnknownFeature_ReturnsFalse()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Premium);

            // Var olmayan feature → switch default → false
            Assert.False(service.IsFeatureAvailable("nonexistent_feature_xyz"));
        }

        [Fact]
        public void IsFeatureAvailable_EmptyString_ReturnsFalse()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Premium);
            Assert.False(service.IsFeatureAvailable(string.Empty));
        }

        // ─── ResumePlayback Feature (Pending Implementation) ─────────────────────────
        //
        // BU TESTLER ŞU AN FAIL VERECEK — pending implementasyonu hatırlatır:
        // LicenseService.Features.ResumePlayback sabiti ve IsFeatureAvailable case'i
        // eklenmeden bu testler geçmez.

        [Fact]
        public void IsFeatureAvailable_ResumePlayback_FreeTier_ReturnsFalse()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Free);

            Assert.False(service.IsFeatureAvailable(LicenseService.Features.ResumePlayback));
        }

        [Fact]
        public void IsFeatureAvailable_ResumePlayback_PremiumTier_ReturnsTrue()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Premium);

            Assert.True(service.IsFeatureAvailable(LicenseService.Features.ResumePlayback));
        }

        // ─── Free Tier Feature Checks ─────────────────────────────────────────────────

        [Theory]
        [InlineData(LicenseService.Features.Multiview)]
        [InlineData(LicenseService.Features.MiniPlayer)]
        [InlineData(LicenseService.Features.AdFree)]
        [InlineData(LicenseService.Features.AudioTrackSelection)]
        public void IsFeatureAvailable_AllPremiumFeatures_FreeTier_ReturnFalse(string feature)
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Free);
            Assert.False(service.IsFeatureAvailable(feature));
        }

        // ─── Limit Boundary Value Analysis ───────────────────────────────────────────

        [Fact]
        public void IsWithinLimit_Profiles_AtExactLimit_ReturnsFalse()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Free);

            // Free tier profil limiti = 3; count=3 → false (limit aşıldı / doldu)
            Assert.False(service.IsWithinLimit(LicenseService.Limits.Profiles, 3));
        }

        [Fact]
        public void IsWithinLimit_Profiles_OneBelowLimit_ReturnsTrue()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Free);

            Assert.True(service.IsWithinLimit(LicenseService.Limits.Profiles, 2));
        }

        [Fact]
        public void IsWithinLimit_Favorites_AtExactLimit_ReturnsFalse()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Free);

            // Free tier favori limiti = 50; count=50 → false
            Assert.False(service.IsWithinLimit(LicenseService.Limits.Favorites, 50));
        }

        [Fact]
        public void IsWithinLimit_Favorites_OneBelowLimit_ReturnsTrue()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Free);

            Assert.True(service.IsWithinLimit(LicenseService.Limits.Favorites, 49));
        }

        [Fact]
        public void IsWithinLimit_Premium_AlwaysTrue_Regardless()
        {
            var service = new LicenseService();
            service.SetTierForTesting(SubscriptionTier.Premium);

            // Premium'da limit yok — 1000 favori de olsa true
            Assert.True(service.IsWithinLimit(LicenseService.Limits.Profiles, 1000));
            Assert.True(service.IsWithinLimit(LicenseService.Limits.Favorites, 1000));
        }

        // ─── DeactivatePremium ────────────────────────────────────────────────────────

        [Fact]
        public void DeactivatePremium_SetsBackToFree_AndNotifies()
        {
            var service = new LicenseService();
            service.ActivatePremium();
            Assert.True(service.IsPremium); // ön koşul

            bool notified = false;
            service.SubscriptionChanged += () => notified = true;

            service.DeactivatePremium();

            Assert.Equal(SubscriptionTier.Free, service.CurrentTier);
            Assert.False(service.IsPremium);
            Assert.True(notified);
        }

        [Fact]
        public void DeactivatePremium_WhenAlreadyFree_DoesNotThrow()
        {
            var service = new LicenseService();
            // Zaten Free — exception olmamalı
            var ex = Record.Exception(() => service.DeactivatePremium());
            Assert.Null(ex);
        }

        // ─── Multiple Subscribers ─────────────────────────────────────────────────────

        [Fact]
        public void SubscriptionChanged_NotifiesAllSubscribers()
        {
            var service = new LicenseService();
            int callCount = 0;

            service.SubscriptionChanged += () => callCount++;
            service.SubscriptionChanged += () => callCount++;
            service.SubscriptionChanged += () => callCount++;

            service.ActivatePremium();

            Assert.Equal(3, callCount);
        }

        // ─── SetTierForTesting ────────────────────────────────────────────────────────

        [Fact]
        public void SetTierForTesting_SwitchesTierWithoutFiringEvent()
        {
            var service = new LicenseService();
            bool notified = false;
            service.SubscriptionChanged += () => notified = true;

            // SetTierForTesting test yardımcı metodu — event fırlatmamalı
            service.SetTierForTesting(SubscriptionTier.Premium);

            Assert.Equal(SubscriptionTier.Premium, service.CurrentTier);
            Assert.False(notified, "SetTierForTesting event fırlatmamalı");
        }

        // ─── IsPremium Consistency ────────────────────────────────────────────────────

        [Fact]
        public void IsPremium_ConsistentWithCurrentTier()
        {
            var service = new LicenseService();

            service.SetTierForTesting(SubscriptionTier.Free);
            Assert.False(service.IsPremium);
            Assert.Equal(SubscriptionTier.Free, service.CurrentTier);

            service.SetTierForTesting(SubscriptionTier.Premium);
            Assert.True(service.IsPremium);
            Assert.Equal(SubscriptionTier.Premium, service.CurrentTier);
        }
    }
}
