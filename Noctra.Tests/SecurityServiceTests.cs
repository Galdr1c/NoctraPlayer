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

        [Fact]
        public void HashPin_ShouldReturnVersionedPbkdf2Hash()
        {
            var hash = _securityService.HashPin("1234");

            Assert.StartsWith("PBKDF2$SHA256$", hash);
            Assert.Equal(5, hash.Split('$').Length);
        }

        [Fact]
        public void HashPin_SamePin_ShouldReturnDifferentHashes()
        {
            var hash1 = _securityService.HashPin("1234");
            var hash2 = _securityService.HashPin("1234");

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

        [Fact]
        public void VerifyPin_LegacySha256Hash_ShouldReturnTrue()
        {
            const string legacyHashFor1234 = "83D837DD7E939316F5A94A1216FF2E6F2DC9E9859441F333CC12FA2414468B88";

            Assert.True(_securityService.VerifyPin("1234", legacyHashFor1234));
        }

        [Fact]
        public void VerifyPin_InvalidHashFormat_ShouldReturnFalse()
        {
            Assert.False(_securityService.VerifyPin("1234", "not-a-valid-hash"));
        }
    }
}
