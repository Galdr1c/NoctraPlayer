using System;
using Moq;
using Noctra.Models;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// Event-driven review prompt uygunluk kurallarini dogrular:
    /// prompt, ilk acilis + 3 dakika gibi zaman tabanli bir kural yerine
    /// anlamli kullanim deneyimine dayanmalidir.
    /// </summary>
    public class ReviewPromptPolicyTests
    {
        private static AppSettings SettingsWithAllCriteriaMet() => new()
        {
            ReviewPromptLaunchCount = 3,
            ReviewPromptProviderAddCount = 1,
            ReviewPromptPlaybackCount = 3,
            ReviewPromptTotalPlaybackSeconds = 30 * 60 // 30 dk (minimum esik)
        };

        [Fact]
        public void IsEligible_WhenAllCriteriaMet_ReturnsTrue()
        {
            Assert.True(ReviewPromptPolicy.IsEligible(SettingsWithAllCriteriaMet(), DateTime.UtcNow));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void IsEligible_WhenLaunchCountBelowThree_ReturnsFalse(int launchCount)
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptLaunchCount = launchCount;

            Assert.False(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void IsEligible_WhenPlaybackSessionsBelowThree_ReturnsFalse(int playbackCount)
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptPlaybackCount = playbackCount;

            Assert.False(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Fact]
        public void IsEligible_WhenNoProviderAdded_ReturnsFalse()
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptProviderAddCount = 0;

            Assert.False(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10 * 60)]
        [InlineData(30 * 60 - 1)]
        public void IsEligible_WhenTotalPlaybackBelowThirtyMinutes_ReturnsFalse(double totalSeconds)
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptTotalPlaybackSeconds = totalSeconds;

            Assert.False(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Fact]
        public void IsEligible_WhenDismissed_ReturnsFalse()
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptDismissed = true;

            Assert.False(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Fact]
        public void IsEligible_WhenAlreadyCompleted_ReturnsFalse()
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptCompletedAtUtc = DateTime.UtcNow;

            Assert.False(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Fact]
        public void IsEligible_WhenSnoozedInFuture_ReturnsFalse()
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.AddDays(13);

            Assert.False(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Fact]
        public void IsEligible_WhenSnoozeExpired_ReturnsTrue()
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.AddDays(-1);

            Assert.True(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Fact]
        public void IsEligible_WhenShownRecently_ReturnsFalse()
        {
            var settings = SettingsWithAllCriteriaMet();
            settings.ReviewPromptLastShownAtUtc = DateTime.UtcNow.AddDays(-5);

            Assert.False(ReviewPromptPolicy.IsEligible(settings, DateTime.UtcNow));
        }

        [Fact]
        public void SnoozeDuration_IsAtLeastTenDays()
        {
            // Kullanici "Later" derse en az 10 gun sorulmamali.
            Assert.True(ReviewPromptPolicy.SnoozeDuration >= TimeSpan.FromDays(10));
        }
    }

    public class ReviewPromptTrackerTests
    {
        private static (ReviewPromptTracker tracker, Mock<ISettingsService> settingsMock, AppSettings settings) CreateTracker()
        {
            var settings = new AppSettings();
            var settingsMock = new Mock<ISettingsService>();
            settingsMock.SetupGet(s => s.Settings).Returns(settings);
            settingsMock.Setup(s => s.SaveAsync()).Returns(System.Threading.Tasks.Task.CompletedTask);

            var tracker = new ReviewPromptTracker(settingsMock.Object);
            return (tracker, settingsMock, settings);
        }

        [Fact]
        public void RecordProviderAdded_IncrementsProviderAddCount()
        {
            var (tracker, _, settings) = CreateTracker();

            tracker.RecordProviderAdded();

            Assert.Equal(1, settings.ReviewPromptProviderAddCount);
        }

        [Fact]
        public void RecordPlaybackSession_AccumulatesTotalWatchTime()
        {
            var (tracker, _, settings) = CreateTracker();

            tracker.RecordPlaybackSession(TimeSpan.FromMinutes(10));
            tracker.RecordPlaybackSession(TimeSpan.FromMinutes(20));

            // 10 + 20 = 30 dk = esige tam ulasilir, cap'te kesilebilir
            Assert.Equal(30 * 60, settings.ReviewPromptTotalPlaybackSeconds, precision: 3);
        }

        [Fact]
        public void RecordPlaybackSession_DoesNotExceedCap()
        {
            var (tracker, _, settings) = CreateTracker();

            // Esigi asacak kadar cok izleme
            tracker.RecordPlaybackSession(TimeSpan.FromMinutes(20));
            tracker.RecordPlaybackSession(TimeSpan.FromMinutes(20));
            tracker.RecordPlaybackSession(TimeSpan.FromMinutes(20));

            // Cap: MinimumTotalPlayback = 30 dk = 1800 s
            Assert.Equal(ReviewPromptPolicy.MinimumTotalPlayback.TotalSeconds,
                settings.ReviewPromptTotalPlaybackSeconds, precision: 3);
        }

        [Fact]
        public void RecordPlaybackSession_ShortSession_DoesNotIncrementPlaybackCount()
        {
            var (tracker, _, settings) = CreateTracker();

            tracker.RecordPlaybackSession(TimeSpan.FromSeconds(10));

            Assert.Equal(0, settings.ReviewPromptPlaybackCount);
            Assert.Equal(10, settings.ReviewPromptTotalPlaybackSeconds, precision: 3);
        }

        [Fact]
        public void RecordPlaybackSession_MeaningfulSession_IncrementsPlaybackCount()
        {
            var (tracker, _, settings) = CreateTracker();

            tracker.RecordPlaybackSession(TimeSpan.FromMinutes(2));

            Assert.Equal(1, settings.ReviewPromptPlaybackCount);
        }

        [Fact]
        public void RecordPlaybackSession_ZeroDuration_IsIgnored()
        {
            var (tracker, _, settings) = CreateTracker();

            tracker.RecordPlaybackSession(TimeSpan.Zero);

            Assert.Equal(0, settings.ReviewPromptPlaybackCount);
            Assert.Equal(0, settings.ReviewPromptTotalPlaybackSeconds);
        }

        [Fact]
        public void RecordProviderAdded_RaisesPromptRequested_WhenEligible()
        {
            var (tracker, _, settings) = CreateTracker();
            settings.ReviewPromptLaunchCount = 3;
            settings.ReviewPromptProviderAddCount = 0;
            settings.ReviewPromptPlaybackCount = 3;
            settings.ReviewPromptTotalPlaybackSeconds = 30 * 60; // 30 dk = minimum esik

            var raised = false;
            tracker.PromptRequested += () => raised = true;

            tracker.RecordProviderAdded();

            Assert.True(raised);
        }

        [Fact]
        public void RecordProviderAdded_DoesNotRaisePromptRequested_WhenNotEligible()
        {
            var (tracker, _, settings) = CreateTracker();
            settings.ReviewPromptLaunchCount = 1; // yetersiz launch

            var raised = false;
            tracker.PromptRequested += () => raised = true;

            tracker.RecordProviderAdded();

            Assert.False(raised);
        }

        [Fact]
        public void RecordPlaybackSession_DoesNotRaisePromptRequested_WhenSnoozed()
        {
            var (tracker, _, settings) = CreateTracker();
            settings.ReviewPromptLaunchCount = 3;
            settings.ReviewPromptProviderAddCount = 1;
            settings.ReviewPromptPlaybackCount = 3;
            settings.ReviewPromptTotalPlaybackSeconds = 30 * 60; // 30 dk = minimum esik
            settings.ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.AddDays(7);

            var raised = false;
            tracker.PromptRequested += () => raised = true;

            tracker.RecordPlaybackSession(TimeSpan.FromMinutes(5));

            Assert.False(raised);
        }
    }
}
