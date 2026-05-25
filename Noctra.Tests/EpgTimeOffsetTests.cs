using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Noctra.Models;
using Noctra.Data;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System.Net.Http;

namespace Noctra.Tests
{
    public class EpgTimeOffsetTests
    {
        private AppDbContext CreateContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public async Task GetProgramsAsync_ShouldApplyTimeOffset()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using (var ctx = CreateContext(dbName))
            {
                ctx.EpgPrograms.Add(new EpgProgram
                {
                    Id = 1,
                    ChannelId = "test_channel",
                    Title = "Test Program",
                    StartTime = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                    EndTime = new DateTime(2023, 1, 1, 14, 0, 0, DateTimeKind.Utc)
                });
                await ctx.SaveChangesAsync();
            }

            var mockContextFactory = new Mock<IDbContextFactory<AppDbContext>>();
            mockContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(() => CreateContext(dbName));

            var mockSettingsService = new Mock<ISettingsService>();
            mockSettingsService.Setup(s => s.Settings).Returns(new AppSettings { EpgTimeOffsetHours = 3 });

            var mockLocalizationService = new Mock<ILocalizationService>();
            mockLocalizationService.Setup(l => l.GetString(It.IsAny<string>())).Returns<string>(k => k);

            var httpClient = new HttpClient();
            var languageDetectionService = new LanguageDetectionService();
            var epgService = new EpgService(mockContextFactory.Object, httpClient, mockSettingsService.Object, mockLocalizationService.Object, languageDetectionService);
            // Act
            var from = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2023, 1, 1, 23, 59, 59, DateTimeKind.Utc);
            var programs = await epgService.GetProgramsAsync("test_channel", from, to);

            // Assert
            Assert.Single(programs);
            var program = programs.First();
            
            // Expected: 12:00 UTC + 3 hours offset = 15:00
            Assert.Equal(new DateTime(2023, 1, 1, 15, 0, 0, DateTimeKind.Utc), program.StartTime);
            // Expected: 14:00 UTC + 3 hours offset = 17:00
            Assert.Equal(new DateTime(2023, 1, 1, 17, 0, 0, DateTimeKind.Utc), program.EndTime);
        }

        [Fact]
        public async Task GetProgramsAsync_NegativeOffset_ShouldApplyCorrectly()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using (var ctx = CreateContext(dbName))
            {
                ctx.EpgPrograms.Add(new EpgProgram
                {
                    Id = 1,
                    ChannelId = "test_channel",
                    Title = "Test Program",
                    StartTime = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                    EndTime = new DateTime(2023, 1, 1, 14, 0, 0, DateTimeKind.Utc)
                });
                await ctx.SaveChangesAsync();
            }

            var mockContextFactory = new Mock<IDbContextFactory<AppDbContext>>();
            mockContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(() => CreateContext(dbName));

            var mockSettingsService = new Mock<ISettingsService>();
            mockSettingsService.Setup(s => s.Settings).Returns(new AppSettings { EpgTimeOffsetHours = -5 });

            var mockLocalizationService = new Mock<ILocalizationService>();
            mockLocalizationService.Setup(l => l.GetString(It.IsAny<string>())).Returns<string>(k => k);

            var httpClient = new HttpClient();
            var languageDetectionService = new LanguageDetectionService();
            var epgService = new EpgService(mockContextFactory.Object, httpClient, mockSettingsService.Object, mockLocalizationService.Object, languageDetectionService);
            // Act
            var from = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2023, 1, 1, 23, 59, 59, DateTimeKind.Utc);
            var programs = await epgService.GetProgramsAsync("test_channel", from, to);

            // Assert
            Assert.Single(programs);
            var program = programs.First();
            
            // Expected: 12:00 UTC - 5 hours offset = 07:00
            Assert.Equal(new DateTime(2023, 1, 1, 7, 0, 0, DateTimeKind.Utc), program.StartTime);
            // Expected: 14:00 UTC - 5 hours offset = 09:00
            Assert.Equal(new DateTime(2023, 1, 1, 9, 0, 0, DateTimeKind.Utc), program.EndTime);
        }

        [Fact]
        public async Task LoadEpgAsync_WhenEpgDisabled_ShouldReleaseSemaphore()
        {
            var service = CreateEpgServiceForLoad(new AppSettings { EpgEnabled = false });

            var first = await service.LoadEpgAsync("https://example.com/epg.xml", isPrimary: false);
            var second = await service.LoadEpgAsync("https://example.com/epg.xml", isPrimary: false);

            Assert.Equal(0, first);
            Assert.Equal(0, second);
        }

        [Fact]
        public async Task LoadEpgAsync_WhenUrlEmpty_ShouldReleaseSemaphore()
        {
            var service = CreateEpgServiceForLoad(new AppSettings { EpgEnabled = true });

            var first = await service.LoadEpgAsync(string.Empty, isPrimary: false);
            var second = await service.LoadEpgAsync(string.Empty, isPrimary: false);

            Assert.Equal(0, first);
            Assert.Equal(0, second);
        }

        private EpgService CreateEpgServiceForLoad(AppSettings settings)
        {
            var dbName = Guid.NewGuid().ToString();
            var mockContextFactory = new Mock<IDbContextFactory<AppDbContext>>();
            mockContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(() => CreateContext(dbName));

            var mockSettingsService = new Mock<ISettingsService>();
            mockSettingsService.Setup(s => s.Settings).Returns(settings);

            var mockLocalizationService = new Mock<ILocalizationService>();
            mockLocalizationService.Setup(l => l.GetString(It.IsAny<string>())).Returns<string>(k => k);

            return new EpgService(
                mockContextFactory.Object,
                new HttpClient(),
                mockSettingsService.Object,
                mockLocalizationService.Object,
                new LanguageDetectionService());
        }
    }
}
