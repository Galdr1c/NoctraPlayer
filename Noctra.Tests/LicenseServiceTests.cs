using Xunit;
using Noctra.Services;
using Noctra.Models;

namespace Noctra.Tests
{
    public class LicenseServiceTests
    {
        private readonly LicenseService _licenseService;

        public LicenseServiceTests()
        {
            _licenseService = new LicenseService();
        }

        [Fact]
        public void DefaultTier_ShouldBeFree()
        {
            Assert.Equal(SubscriptionTier.Free, _licenseService.CurrentTier);
            Assert.False(_licenseService.IsPremium);
        }

        [Theory]
        [InlineData(LicenseService.Features.AudioTrackSelection, false)]
        [InlineData(LicenseService.Features.MiniPlayer, false)]
        [InlineData(LicenseService.Features.AdFree, false)]
        public void FreeTier_ShouldNotHavePremiumFeatures(string feature, bool expected)
        {
            _licenseService.SetTierForTesting(SubscriptionTier.Free);
            Assert.Equal(expected, _licenseService.IsFeatureAvailable(feature));
        }

        [Fact]
        public void PremiumTier_ShouldHaveAllFeatures()
        {
            _licenseService.SetTierForTesting(SubscriptionTier.Premium);
            
            Assert.True(_licenseService.IsFeatureAvailable(LicenseService.Features.AudioTrackSelection));
            Assert.True(_licenseService.IsFeatureAvailable(LicenseService.Features.MiniPlayer));
            Assert.True(_licenseService.IsFeatureAvailable(LicenseService.Features.Multiview));
            Assert.True(_licenseService.IsFeatureAvailable(LicenseService.Features.AdFree));
        }

        [Theory]
        [InlineData(LicenseService.Limits.Profiles, 2, true)]  // Limit is 3
        [InlineData(LicenseService.Limits.Profiles, 3, false)]
        [InlineData(LicenseService.Limits.Favorites, 49, true)] // Limit is 50
        [InlineData(LicenseService.Limits.Favorites, 50, false)]
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
    }
}
