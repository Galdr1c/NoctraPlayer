using System;
using System.Reflection;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// MainViewModel'daki fuzzy arama algoritmalarını test eder.
    /// NormalizeFuzzyText, LevenshteinDistance ve IsLikelySimilar private static metodları
    /// reflection üzerinden erişilerek test edilir.
    ///
    /// Bu testler aşağıdaki pending bug fix'leri de kapsar:
    ///   - Türkçe karakter normalizasyonu (ı→i, ş→s, ğ→g, ü→u, ö→o, ç→c)
    ///   - "vıkıng" → "viking" ile eşleşme
    /// </summary>
    public class FuzzySearchTests
    {
        private static readonly Type _vmType = typeof(MainViewModel);

        private static string NormalizeFuzzyText(string? value)
        {
            var method = _vmType.GetMethod("NormalizeFuzzyText",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method); // metod bulunamazsa testi anlamlı şekilde başarısız yap
            return (string)method!.Invoke(null, new object?[] { value })!;
        }

        private static int LevenshteinDistance(string source, string target, int maxDistance)
        {
            var method = _vmType.GetMethod("LevenshteinDistance",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (int)method!.Invoke(null, new object[] { source, target, maxDistance })!;
        }

        private static bool IsLikelySimilar(string query, string? candidate)
        {
            var method = _vmType.GetMethod("IsLikelySimilar",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (bool)method!.Invoke(null, new object?[] { query, candidate })!;
        }

        // ─── NormalizeFuzzyText ──────────────────────────────────────────────────────

        [Fact]
        public void NormalizeFuzzyText_NullInput_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, NormalizeFuzzyText(null));
        }

        [Fact]
        public void NormalizeFuzzyText_WhitespaceInput_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, NormalizeFuzzyText("   "));
        }

        [Fact]
        public void NormalizeFuzzyText_TrimsAndLowercases()
        {
            var result = NormalizeFuzzyText("  Breaking Bad  ");
            Assert.Equal("breaking bad", result);
        }

        [Fact]
        public void NormalizeFuzzyText_RemovesSpecialCharacters()
        {
            // Noktalama işaretleri boşluğa dönüşmeli
            var result = NormalizeFuzzyText("Game of Thrones: Season 1");
            Assert.DoesNotContain(":", result);
        }

        [Fact]
        public void NormalizeFuzzyText_CollapsesMultipleSpaces()
        {
            var result = NormalizeFuzzyText("The   Dark   Knight");
            Assert.Equal("the dark knight", result);
        }

        // ─── Türkçe Karakter Normalizasyonu (Pending Fix) ────────────────────────────

        [Theory]
        [InlineData("vıkıng",    "viking")]   // ı → i
        [InlineData("şimşek",    "simsek")]   // ş → s
        [InlineData("değer",     "deger")]    // ğ → g
        [InlineData("üzüm",      "uzum")]     // ü → u
        [InlineData("özel",      "ozel")]     // ö → o
        [InlineData("çiçek",     "cicek")]    // ç → c
        [InlineData("İstanbul",  "istanbul")] // İ → i (büyük)
        [InlineData("Şehir",     "sehir")]    // Ş → s (büyük)
        public void NormalizeFuzzyText_ConvertsTurkishCharactersToAscii(string input, string expected)
        {
            // BU TEST PENDING FIX'İ KAPSAR:
            // MainViewModel.NormalizeFuzzyText içinde Türkçe → ASCII dönüşümü eklenmeli.
            // Fix eklendikten sonra bu testler geçmeli.
            var result = NormalizeFuzzyText(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void NormalizeFuzzyText_TurkishQuery_MatchesEnglishTitle()
        {
            // "vıkıng" aranınca "Vikings" ile eşleşmeli (normalizasyon sonrası)
            var normalizedQuery     = NormalizeFuzzyText("vıkıng");
            var normalizedCandidate = NormalizeFuzzyText("Vikings");
            Assert.Equal("viking", normalizedQuery);
            Assert.Equal("vikings", normalizedCandidate);
        }

        // ─── LevenshteinDistance ─────────────────────────────────────────────────────

        [Fact]
        public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
        {
            Assert.Equal(0, LevenshteinDistance("breaking", "breaking", 5));
        }

        [Fact]
        public void LevenshteinDistance_SingleCharDifference_Returns1()
        {
            // "brak" → "brak" + 1 harf = distance 1
            Assert.Equal(1, LevenshteinDistance("breaking", "braaking", 5));
        }

        [Fact]
        public void LevenshteinDistance_Transposition_Returns1()
        {
            // "recive" → "receive" (bir karakter eksik)
            Assert.Equal(1, LevenshteinDistance("recieve", "receive", 3));
        }

        [Fact]
        public void LevenshteinDistance_WhenDistanceExceedsMax_ReturnsNegativeOne()
        {
            // maxDistance=1 ama gerçek mesafe 3 → -1 dönmeli (early exit)
            var result = LevenshteinDistance("abc", "xyz", 1);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void LevenshteinDistance_EmptyVsNonEmpty_ReturnsLengthOrNegative()
        {
            // "abc" ile "" arası mesafe = 3; maxDistance=2 → -1
            var result = LevenshteinDistance("abc", "", 2);
            // length diff > maxDistance olduğu için -1 dönmeli
            Assert.Equal(-1, result);
        }

        [Theory]
        [InlineData("dark",    "dark",   0, 0)]   // aynı
        [InlineData("dark",    "drak",   2, 1)]   // transpozisyon (1 edit in Damerau-Levenshtein)
        [InlineData("vikings", "viking", 3, 1)]   // 1 karakter eksik
        [InlineData("lost",    "last",   3, 1)]   // 1 karakter değişmiş
        public void LevenshteinDistance_Theory(string s, string t, int maxDist, int expectedOrNeg)
        {
            var result = LevenshteinDistance(s, t, maxDist);
            if (expectedOrNeg >= 0)
                Assert.Equal(expectedOrNeg, result);
            else
                Assert.Equal(-1, result);
        }

        // ─── IsLikelySimilar ─────────────────────────────────────────────────────────

        [Fact]
        public void IsLikelySimilar_ExactMatch_ReturnsTrue()
        {
            Assert.True(IsLikelySimilar("breaking bad", "Breaking Bad"));
        }

        [Fact]
        public void IsLikelySimilar_SubstringContainment_ReturnsTrue()
        {
            // Query, candidate'in içinde geçiyorsa similar sayılmalı
            Assert.True(IsLikelySimilar("dark", "Dark Knight"));
        }

        [Fact]
        public void IsLikelySimilar_OneTypo_ReturnsTrue()
        {
            // "brekking bad" — 1 harf yanlış, yine de benzer sayılmalı
            Assert.True(IsLikelySimilar("brekking bad", "breaking bad"));
        }

        [Fact]
        public void IsLikelySimilar_CompletelyDifferent_ReturnsFalse()
        {
            Assert.False(IsLikelySimilar("game of thrones", "breaking bad"));
        }

        [Fact]
        public void IsLikelySimilar_NullCandidate_ReturnsFalse()
        {
            Assert.False(IsLikelySimilar("vikings", null));
        }

        [Fact]
        public void IsLikelySimilar_EmptyQuery_ReturnsFalse()
        {
            Assert.False(IsLikelySimilar("", "Vikings"));
        }

        [Fact]
        public void IsLikelySimilar_TurkishTypo_ReturnsTrue()
        {
            // "vıkıng" ile "Vikings" — normalizasyon sonrası benzer olmalı
            // Bu test pending Türkçe fix'i ile birlikte geçmeli
            Assert.True(IsLikelySimilar("vıkıng", "Vikings"));
        }
    }
}
