using Noctra.Services;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// PIN2 (salt'lı SHA-256) doğrulayıcı testleri. Eski PBKDF2/legacy SHA-256
    /// akışı kaldırılmıştır; bu sınıf hızlı verifier'ın sözleşmesini doğrular:
    /// format, salt rastgeleliği, sabit zamanlı karşılaştırma ve bozuk girdi
    /// reddi. Asıl pratik koruma katmanı ProfileService'in 5/30 kilididir.
    /// </summary>
    public class ProfilePinVerifierTests
    {
        [Fact]
        public void Create_ReturnsVersionedPin2Format()
        {
            var verifier = ProfilePinVerifier.Create("1234");

            Assert.StartsWith("PIN2$", verifier, StringComparison.Ordinal);
            Assert.Equal(3, verifier.Split('$').Length);
            Assert.True(ProfilePinVerifier.IsCurrentFormat(verifier));
        }

        [Fact]
        public void Create_SamePin_ReturnsDifferentVerifiers_DueToRandomSalt()
        {
            var v1 = ProfilePinVerifier.Create("1234");
            var v2 = ProfilePinVerifier.Create("1234");

            Assert.NotEqual(v1, v2);
        }

        [Fact]
        public void Verify_CorrectPin_ReturnsTrue()
        {
            var verifier = ProfilePinVerifier.Create("9090");

            Assert.True(ProfilePinVerifier.Verify("9090", verifier));
        }

        [Fact]
        public void Verify_WrongPin_ReturnsFalse()
        {
            var verifier = ProfilePinVerifier.Create("1111");

            Assert.False(ProfilePinVerifier.Verify("2222", verifier));
        }

        [Fact]
        public void Verify_MalformedOrForeignFormats_ReturnFalse()
        {
            // Eski PBKDF2 formatı — PIN2'ye geçişte sıfırlanır, doğrulanmaz.
            Assert.False(ProfilePinVerifier.Verify("1234", "PBKDF2$SHA256$210000$c2FsdA==$aGFzaA=="));

            // Eski legacy SHA-256 (düz hex).
            Assert.False(ProfilePinVerifier.Verify("1234", "83D837DD7E939316F5A94A1216FF2E6F2DC9E9859441F333CC12FA2414468B88"));

            // Bozuk / elle kurcalanmış değerler.
            Assert.False(ProfilePinVerifier.Verify("1234", "not-a-verifier"));
            Assert.False(ProfilePinVerifier.Verify("1234", "PIN2$only-two-parts"));
            Assert.False(ProfilePinVerifier.Verify("1234", "PIN2$$$"));
            Assert.False(ProfilePinVerifier.Verify("1234", "PIN2$!!!not-base64!!!$!!!!"));
            Assert.False(ProfilePinVerifier.Verify("1234", string.Empty));
            Assert.False(ProfilePinVerifier.Verify("1234", null!));
        }

        [Fact]
        public void Verify_TamperedHashOrSaltLengths_ReturnFalse()
        {
            // 16 byte salt + 32 byte hash zorunludur; uzunluk sapması reddedilir.
            var shortSalt = Convert.ToBase64String(new byte[8]);
            var correctHash = Convert.ToBase64String(new byte[32]);
            Assert.False(ProfilePinVerifier.Verify("1234", $"PIN2${shortSalt}${correctHash}"));

            var correctSalt = Convert.ToBase64String(new byte[16]);
            var shortHash = Convert.ToBase64String(new byte[16]);
            Assert.False(ProfilePinVerifier.Verify("1234", $"PIN2${correctSalt}${shortHash}"));
        }

        [Fact]
        public void Verify_NonAsciiOrMalformedPin_ReturnsFalse()
        {
            // Tam genişlik (Unicode) rakamlar ASCII değildir — PIN klavyesinin
            // ürettiği ASCII rakamlarla aynı kural uygulanır.
            Assert.False(ProfilePinVerifier.Verify("１２３４", ProfilePinVerifier.Create("1234")));
            Assert.False(ProfilePinVerifier.Verify("12345", ProfilePinVerifier.Create("1234")));
            Assert.False(ProfilePinVerifier.Verify("12ab", ProfilePinVerifier.Create("1234")));
        }

        [Fact]
        public void Create_InvalidPin_Throws()
        {
            Assert.Throws<ArgumentException>(() => ProfilePinVerifier.Create("123"));
            Assert.Throws<ArgumentException>(() => ProfilePinVerifier.Create("12345"));
            Assert.Throws<ArgumentException>(() => ProfilePinVerifier.Create("12ab"));
            Assert.Throws<ArgumentException>(() => ProfilePinVerifier.Create("１２３４"));
            Assert.Throws<ArgumentException>(() => ProfilePinVerifier.Create(string.Empty));
            Assert.Throws<ArgumentException>(() => ProfilePinVerifier.Create(null!));
        }

        [Fact]
        public void IsCurrentFormat_OnlyAcceptsPin2Prefix()
        {
            Assert.True(ProfilePinVerifier.IsCurrentFormat("PIN2$AA==$BB=="));
            Assert.False(ProfilePinVerifier.IsCurrentFormat("PBKDF2$SHA256$210000$AA==$BB=="));
            Assert.False(ProfilePinVerifier.IsCurrentFormat("83D837DD7E939316F5A94A1216FF2E6F2DC9E9859441F333CC12FA2414468B88"));
            Assert.False(ProfilePinVerifier.IsCurrentFormat(null));
            Assert.False(ProfilePinVerifier.IsCurrentFormat(string.Empty));
        }
    }
}
