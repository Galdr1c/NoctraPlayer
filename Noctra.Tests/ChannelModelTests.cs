using System;
using Noctra.Models;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// Channel.WatchedPercentage ve Channel.CoverUrl computed property'lerini test eder.
    /// WatchedPercentage: VodCard, SeriesCard, ContinueWatchingCard'daki progress bar'ı besler.
    /// CoverUrl: Tüm medya kartlarının poster/backdrop seçim mantığını yönetir.
    /// </summary>
    public class ChannelModelTests
    {
        // ─── WatchedPercentage ───────────────────────────────────────────────────────

        [Fact]
        public void WatchedPercentage_WhenNoDuration_ReturnsZero()
        {
            var channel = new Channel
            {
                WatchedPosition = TimeSpan.FromMinutes(30),
                Duration = null
            };

            Assert.Equal(0, channel.WatchedPercentage);
        }

        [Fact]
        public void WatchedPercentage_WhenNoWatchedPosition_ReturnsZero()
        {
            var channel = new Channel
            {
                WatchedPosition = null,
                Duration = TimeSpan.FromHours(2)
            };

            Assert.Equal(0, channel.WatchedPercentage);
        }

        [Fact]
        public void WatchedPercentage_WhenDurationIsZero_ReturnsZero()
        {
            var channel = new Channel
            {
                WatchedPosition = TimeSpan.FromMinutes(10),
                Duration = TimeSpan.Zero
            };

            Assert.Equal(0, channel.WatchedPercentage);
        }

        [Fact]
        public void WatchedPercentage_WhenHalfWatched_Returns50()
        {
            var channel = new Channel
            {
                Duration        = TimeSpan.FromHours(2),
                WatchedPosition = TimeSpan.FromHours(1)
            };

            Assert.Equal(50.0, channel.WatchedPercentage, precision: 1);
        }

        [Fact]
        public void WatchedPercentage_WhenFullyWatched_Returns100()
        {
            var channel = new Channel
            {
                Duration        = TimeSpan.FromMinutes(90),
                WatchedPosition = TimeSpan.FromMinutes(90)
            };

            Assert.Equal(100.0, channel.WatchedPercentage, precision: 1);
        }

        [Fact]
        public void WatchedPercentage_IsClampedTo100_WhenPositionExceedsDuration()
        {
            // Hatalı veri (pozisyon > süre) durumunda 100 üzerine çıkmamalı
            var channel = new Channel
            {
                Duration        = TimeSpan.FromMinutes(60),
                WatchedPosition = TimeSpan.FromMinutes(75) // Hatalı veri
            };

            Assert.Equal(100.0, channel.WatchedPercentage, precision: 1);
        }

        [Fact]
        public void WatchedPercentage_IsClampedToZero_WhenNegative()
        {
            // Negatif değer durumunda 0'ın altına inmemeli
            var channel = new Channel
            {
                Duration        = TimeSpan.FromMinutes(60),
                WatchedPosition = TimeSpan.FromMinutes(-5) // Hatalı veri
            };

            Assert.Equal(0, channel.WatchedPercentage);
        }

        [Fact]
        public void WatchedPercentage_CorrectlyCalculatesPartialProgress()
        {
            // 2 saatlik film, 45 dakika izlendi → %37.5
            var channel = new Channel
            {
                Duration        = TimeSpan.FromHours(2),
                WatchedPosition = TimeSpan.FromMinutes(45)
            };

            Assert.Equal(37.5, channel.WatchedPercentage, precision: 1);
        }

        [Theory]
        [InlineData(0, 3600, 0)]           // Hiç izlenmedi
        [InlineData(1800, 3600, 50)]        // Yarısı izlendi
        [InlineData(3240, 3600, 90)]        // %90 izlendi
        [InlineData(3600, 3600, 100)]       // Tamamen izlendi
        public void WatchedPercentage_Theory(double watchedSeconds, double durationSeconds, double expectedPercent)
        {
            var channel = new Channel
            {
                Duration        = TimeSpan.FromSeconds(durationSeconds),
                WatchedPosition = TimeSpan.FromSeconds(watchedSeconds)
            };

            Assert.Equal(expectedPercent, channel.WatchedPercentage, precision: 1);
        }

        // ─── CoverUrl ────────────────────────────────────────────────────────────────

        [Fact]
        public void CoverUrl_WhenBackdropExists_ReturnsBackdropUrl()
        {
            var channel = new Channel
            {
                BackdropUrl = "https://image.tmdb.org/backdrop.jpg",
                LogoUrl     = "https://cdn.provider.com/logo.png"
            };

            Assert.Equal("https://image.tmdb.org/backdrop.jpg", channel.CoverUrl);
        }

        [Fact]
        public void CoverUrl_WhenNoBackdrop_FallsBackToLogoUrl()
        {
            var channel = new Channel
            {
                BackdropUrl = null,
                LogoUrl     = "https://cdn.provider.com/logo.png"
            };

            Assert.Equal("https://cdn.provider.com/logo.png", channel.CoverUrl);
        }

        [Fact]
        public void CoverUrl_WhenBothNull_ReturnsNull()
        {
            var channel = new Channel
            {
                BackdropUrl = null,
                LogoUrl     = null
            };

            Assert.Null(channel.CoverUrl);
        }

        [Fact]
        public void CoverUrl_WhenBackdropIsEmpty_FallsBackToLogoUrl()
        {
            var channel = new Channel
            {
                BackdropUrl = "",
                LogoUrl     = "https://cdn.provider.com/logo.png"
            };

            // BackdropUrl boş string → string.IsNullOrEmpty → logo'ya düşmeli
            Assert.Equal("https://cdn.provider.com/logo.png", channel.CoverUrl);
        }

        // ─── Description ─────────────────────────────────────────────────────────────

        [Fact]
        public void Description_MapsToPlot()
        {
            var channel = new Channel { Plot = "Bir dedektif hikayesi." };
            Assert.Equal("Bir dedektif hikayesi.", channel.Description);
        }

        [Fact]
        public void Description_WhenPlotIsNull_ReturnsNull()
        {
            var channel = new Channel { Plot = null };
            Assert.Null(channel.Description);
        }

        // ─── IsCompleted flag ────────────────────────────────────────────────────────

        [Fact]
        public void IsCompleted_DefaultIsFalse()
        {
            var channel = new Channel();
            Assert.False(channel.IsCompleted);
        }

        [Fact]
        public void WatchedPercentage_WhenIsCompleted_CanShowFullProgress()
        {
            // IsCompleted true olan bir içeriğin WatchedPosition = Duration olduğunda %100 göstermesi
            var duration = TimeSpan.FromHours(2);
            var channel = new Channel
            {
                Duration        = duration,
                WatchedPosition = duration,
                IsCompleted     = true
            };

            Assert.Equal(100.0, channel.WatchedPercentage, precision: 1);
        }
    }
}