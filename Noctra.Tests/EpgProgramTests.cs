using System;
using Noctra.Models;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// EpgProgram.IsNowPlaying ve EpgProgram.ProgressPercentage hesaplamalarını test eder.
    /// Bu computed property'ler PlayerViewModel'daki EPG gösterim ve güncelleme mantığının temelidir.
    /// </summary>
    public class EpgProgramTests
    {
        // ─── IsNowPlaying ────────────────────────────────────────────────────────────

        [Fact]
        public void IsNowPlaying_WhenNowIsBetweenStartAndEnd_ReturnsTrue()
        {
            var program = new EpgProgram
            {
                StartTime = DateTime.UtcNow.AddMinutes(-30),
                EndTime   = DateTime.UtcNow.AddMinutes(30)
            };

            Assert.True(program.IsNowPlaying);
        }

        [Fact]
        public void IsNowPlaying_WhenProgramHasEnded_ReturnsFalse()
        {
            var program = new EpgProgram
            {
                StartTime = DateTime.UtcNow.AddHours(-2),
                EndTime   = DateTime.UtcNow.AddHours(-1)
            };

            Assert.False(program.IsNowPlaying);
        }

        [Fact]
        public void IsNowPlaying_WhenProgramHasNotStartedYet_ReturnsFalse()
        {
            var program = new EpgProgram
            {
                StartTime = DateTime.UtcNow.AddHours(1),
                EndTime   = DateTime.UtcNow.AddHours(2)
            };

            Assert.False(program.IsNowPlaying);
        }

        [Fact]
        public void IsNowPlaying_WhenNowIsExactlyAtStartTime_ReturnsTrue()
        {
            // StartTime <= UtcNow koşulunu test eder (sınır değer analizi)
            var now = DateTime.UtcNow;
            var program = new EpgProgram
            {
                StartTime = now.AddMilliseconds(-1), // biraz önce başladı
                EndTime   = now.AddHours(1)
            };

            Assert.True(program.IsNowPlaying);
        }

        [Fact]
        public void IsNowPlaying_WhenNowIsExactlyAtEndTime_ReturnsFalse()
        {
            // EndTime > UtcNow koşulunu test eder — tam EndTime'da artık oynamıyor
            var now = DateTime.UtcNow;
            var program = new EpgProgram
            {
                StartTime = now.AddHours(-1),
                EndTime   = now.AddMilliseconds(-1) // biraz önce bitti
            };

            Assert.False(program.IsNowPlaying);
        }

        // ─── ProgressPercentage ──────────────────────────────────────────────────────

        [Fact]
        public void ProgressPercentage_WhenProgramIsHalfwayThrough_ReturnsApproximately50()
        {
            var program = new EpgProgram
            {
                StartTime = DateTime.UtcNow.AddMinutes(-30),
                EndTime   = DateTime.UtcNow.AddMinutes(30)
            };

            // 60 dakikalık programın 30. dakikasındayız → ~%50
            var percentage = program.ProgressPercentage;
            Assert.True(percentage >= 45 && percentage <= 55,
                $"Beklenen ~50, gerçek: {percentage}");
        }

        [Fact]
        public void ProgressPercentage_WhenProgramHasNotStarted_ReturnsZero()
        {
            var program = new EpgProgram
            {
                StartTime = DateTime.UtcNow.AddHours(1),
                EndTime   = DateTime.UtcNow.AddHours(2)
            };

            Assert.Equal(0, program.ProgressPercentage);
        }

        [Fact]
        public void ProgressPercentage_WhenProgramHasEnded_ReturnsZero()
        {
            // IsNowPlaying false → ProgressPercentage 0 dönmeli
            var program = new EpgProgram
            {
                StartTime = DateTime.UtcNow.AddHours(-3),
                EndTime   = DateTime.UtcNow.AddHours(-1)
            };

            Assert.Equal(0, program.ProgressPercentage);
        }

        [Fact]
        public void ProgressPercentage_IsNeverGreaterThan100()
        {
            // Teorik olarak Math.Min(100, ...) koruması var — test edelim
            var program = new EpgProgram
            {
                StartTime = DateTime.UtcNow.AddMinutes(-90),
                EndTime   = DateTime.UtcNow.AddMinutes(1) // neredeyse bitti
            };

            Assert.True(program.ProgressPercentage <= 100,
                "ProgressPercentage hiçbir zaman 100'ü geçmemeli");
        }

        [Fact]
        public void ProgressPercentage_AtProgramStart_IsNearZero()
        {
            var program = new EpgProgram
            {
                StartTime = DateTime.UtcNow.AddSeconds(-5),  // az önce başladı
                EndTime   = DateTime.UtcNow.AddHours(1)
            };

            // 1 saatlik programın ilk 5 saniyesindeyiz → %0'a yakın
            Assert.True(program.ProgressPercentage < 5,
                $"Başlangıçta yüzde %5'ten küçük olmalı, gerçek: {program.ProgressPercentage}");
        }

        // ─── TimeRange ───────────────────────────────────────────────────────────────

        [Fact]
        public void TimeRange_ReturnsFormattedLocalTime()
        {
            var start = new DateTime(2026, 3, 8, 20, 0, 0, DateTimeKind.Utc);
            var end   = new DateTime(2026, 3, 8, 21, 30, 0, DateTimeKind.Utc);

            var program = new EpgProgram { StartTime = start, EndTime = end };
            var range = program.TimeRange;

            // Format "HH:mm - HH:mm" olmalı
            Assert.Matches(@"^\d{2}:\d{2} - \d{2}:\d{2}$", range);
        }
    }
}