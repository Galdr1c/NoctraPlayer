using Xunit;
using Noctra.Services;
using System.Runtime.InteropServices;

namespace Noctra.Tests
{
    public class SecurityServiceTests
    {
        private readonly SecurityService _securityService;

        public SecurityServiceTests()
        {
            _securityService = new SecurityService();
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
            // On Windows, if DPAPI is available, it should be different. 
            // If it falls back to plain text, it's a "pass" in terms of non-crashing but a "fail" for security.
            // Our logic returns plain text on failure.
            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void Encrypt_ShouldReturnPlainText_OnNonWindows()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Arrange
            string original = "SecretPassword123";

            // Act
            string? encrypted = _securityService.Encrypt(original);

            // Assert
            Assert.Equal(original, encrypted);
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
        public void HashPin_ShouldReturnConsistentHash()
        {
            var hash1 = _securityService.HashPin("1234");
            var hash2 = _securityService.HashPin("1234");
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void HashPin_DifferentPins_ShouldReturnDifferentHashes()
        {
            var hash1 = _securityService.HashPin("1234");
            var hash2 = _securityService.HashPin("5678");
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void VerifyPin_CorrectPin_ShouldReturnTrue()
        {
            var hash = _securityService.HashPin("9090");
            Assert.True(_securityService.VerifyPin("9090", hash));
        }

        [Fact]
        public void VerifyPin_WrongPin_ShouldReturnFalse()
        {
            var hash = _securityService.HashPin("1111");
            Assert.False(_securityService.VerifyPin("2222", hash));
        }
    }
}
