using Xunit;
using Noctra.Services;
using System.Runtime.InteropServices;

namespace Noctra.Tests
{
    public class SecurityServiceTests
    {
        private readonly DesktopSecurityService _securityService;

        public SecurityServiceTests()
        {
            _securityService = new DesktopSecurityService();
        }

        [Fact]
        public void Encrypt_Decrypt_ShouldReturnOriginalText_OnWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // This test is expected to bypass DPAPI on non-Windows
                return;
            }

            // Arrange
            string original = "SecretPassword123";

            // Act
            string? encrypted = _securityService.Encrypt(original);
            string? decrypted = _securityService.Decrypt(encrypted);

            // Assert
            Assert.NotNull(encrypted);
            Assert.NotEqual(original, encrypted);
            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void Encrypt_ShouldRejectUnsupportedPlatforms()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Arrange
            string original = "SecretPassword123";

            // Act
            Assert.Throws<PlatformNotSupportedException>(() =>
                _securityService.Encrypt(original));
        }

        [Fact]
        public void Encrypt_Decrypt_NullOrEmpty_ShouldReturnNull()
        {
            Assert.Null(_securityService.Encrypt(null));
            Assert.Null(_securityService.Decrypt(null));
            Assert.Null(_securityService.Encrypt(string.Empty));
            Assert.Null(_securityService.Decrypt(string.Empty));
        }

        [Fact]
        public void Decrypt_InvalidOrCorruptPayload_ShouldReturnNull_OnWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            Assert.Null(_securityService.Decrypt("not-a-base64-payload"));
            Assert.Null(_securityService.Decrypt(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("plain-text"))));
        }

    }
}
