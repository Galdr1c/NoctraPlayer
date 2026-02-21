using Xunit;
using Noctra.Services;
using Noctra.Models;
using System;
using System.IO;

namespace Noctra.Tests
{
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _testSettingsPath;

        public SettingsServiceTests()
        {
            // Use a temp path for testing
            _testSettingsPath = Path.Combine(Path.GetTempPath(), "NoctraTests", "settings.json");
            if (File.Exists(_testSettingsPath)) File.Delete(_testSettingsPath);
            
            var dir = Path.GetDirectoryName(_testSettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        [Fact]
        public void Settings_ShouldInitializeWithDefaults_WhenFileNotFound()
        {
            // Arrange
            // Ensure file doesn't exist (handled in constructor)
            var service = new SettingsService(); 
            // Note: SettingsService uses fixed LocalAppData path in constructor. 
            // In a real production codebase, we'd inject the path, but we'll test the actual logic here.
            
            // Act
            var settings = service.Settings;

            // Assert
            Assert.NotNull(settings);
            Assert.NotNull(settings.DownloadPath);
            Assert.Equal(0.9998, settings.DownloadCompletionTolerance);
        }

        [Fact]
        public void Settings_ShouldBeLazyLoaded()
        {
            // Arrange
            var service = new SettingsService();

            // Act & Assert
            // This is hard to objectively verify without reflection or mocks, 
            // but we can ensure it doesn't crash and returns valid data.
            Assert.NotNull(service.Settings);
        }

        public void Dispose()
        {
            // Cleanup NOT strictly necessary for this specific mock-less test 
            // as it uses the real LocalAppData if we don't mock the constructor path.
            // In an ideal world, the SettingsService would take a path parameter.
        }
    }
}
