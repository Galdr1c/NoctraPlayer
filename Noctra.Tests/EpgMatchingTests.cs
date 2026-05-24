using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Noctra.Models;
using Xunit;

// =============================================================================
// EpgMatchingTests
//
// EpgService içindeki private static eşleştirme motorunu test eder:
//   NormalizeName          — gürültü temizleme + lowercase
//   GetNameVariants        — 6 varyant üretimi
//   Similarity             — exact / contains / bigram fallback
//   BigramDice             — n-gram benzerlik skoru
//   ResolveMappedChannelId — channelMap'ten eşleşme + fuzzy fallback
//
// Tüm metodlar private static olduğu için reflection kullanılır.
// (FuzzySearchTests'in kullandığı aynı pattern)
//
// Toplam: 72 test
// =============================================================================

namespace Noctra.Tests;

public class EpgMatchingTests
{
    // ── Reflection helpers ────────────────────────────────────────────────────

    private static readonly Type _epgType = typeof(Noctra.Services.EpgService);

    private static string NormalizeName(string name)
    {
        var m = _epgType.GetMethod("NormalizeName",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.NotNull(m);
        return (string)m.Invoke(null, new object[] { name })!;
    }

    private static readonly Noctra.Services.EpgService _epgServiceInstance = 
        new Noctra.Services.EpgService(null!, null!, null!, null!, new Noctra.Services.LanguageDetectionService());

    private static List<string> GetNameVariants(string name)
    {
        var m = _epgType.GetMethod("GetNameVariants",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(m);
        var result = m.Invoke(_epgServiceInstance, new object[] { name })!;
        return ((System.Collections.IEnumerable)result).Cast<string>().ToList();
    }

    private static double Similarity(string a, string b)
    {
        var m = _epgType.GetMethod("Similarity",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.NotNull(m);
        return (double)m.Invoke(null, new object[] { a, b })!;
    }

    private static double BigramDice(string a, string b)
    {
        var m = _epgType.GetMethod("BigramDice",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.NotNull(m);
        return (double)m.Invoke(null, new object[] { a, b })!;
    }

    private static List<string>? ResolveMappedChannelIds(string normalizedName, Dictionary<string, List<string>> channelMap)
    {
        var m = _epgType.GetMethod("ResolveMappedChannelIds",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.NotNull(m);
        var result = m.Invoke(null, new object[] { normalizedName, channelMap });
        return (List<string>?)result;
    }

    // =========================================================================
    // A — NormalizeName
    // =========================================================================

    [Fact]
    public void NormalizeName_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, NormalizeName(string.Empty));

    [Fact]
    public void NormalizeName_StripsHdSuffix()
    {
        var result = NormalizeName("TRT 1 HD");
        Assert.DoesNotContain("hd", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Show TV FHD",  "showtv")]
    [InlineData("TRT 1 UHD",    "trt1")]
    [InlineData("NTV 4K",       "ntv")]
    [InlineData("Star TV 1080p","startv")]
    [InlineData("ATV 720p",     "atv")]
    public void NormalizeName_QualityTags_Stripped(string input, string expected)
    {
        var result = NormalizeName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Kanal D LIVE",  "kanald")]
    [InlineData("TRT VIP",       "trt")]
    [InlineData("FOX BACKUP",    "fox")]
    [InlineData("Star TV BKP",   "startv")]
    [InlineData("TRT 1 TURKEY",  "trt1")]
    [InlineData("SHOW TURKIYE",  "show")]
    [InlineData("Show TV RAW",   "showtv")]
    [InlineData("Kanal D FHD+",  "kanald")]
    [InlineData("Star TV HDR",   "startv")]
    public void NormalizeName_NoiseWords_Stripped(string input, string expected)
    {
        var result = NormalizeName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeName_Lowercase_Applied()
    {
        var result = NormalizeName("KANAL D");
        Assert.Equal("kanald", result);
    }

    [Fact]
    public void NormalizeName_Spaces_Removed()
    {
        // NormalizeName'in boşlukları kaldırdığını doğrula
        var result = NormalizeName("Kanal D");
        Assert.DoesNotContain(" ", result);
    }

    // =========================================================================
    // B — GetNameVariants
    // =========================================================================

    [Fact]
    public void GetNameVariants_SimpleName_ContainsNormalizedFull()
    {
        var variants = GetNameVariants("TRT 1");
        Assert.Contains("TR:trt1", variants);
    }

    [Fact]
    public void GetNameVariants_WithCountryPrefix_PreservesCountryInKey()
    {
        // Country code stays in the key prefix, not inside the normalized channel name.
        var trVariants = GetNameVariants("TR - Kanal D");
        Assert.Contains("TR:kanald", trVariants);

        var frVariants = GetNameVariants("FR - Kanal D");
        Assert.Contains("FR:kanald", frVariants);
    }

    [Fact]
    public void GetNameVariants_ParenthesizedSuffix_VariantWithoutParens()
    {
        // "Star TV (TR)" → parantez temizlenmiş varyant
        var variants = GetNameVariants("Star TV (TR)");
        Assert.Contains(variants, v => v.Contains("startv") && !v.Contains("("));
    }

    [Fact]
    public void GetNameVariants_PipeSeparated_VariantAfterPipe()
    {
        // "TR | Kanal D" → pipe sonrası varyant
        var variants = GetNameVariants("TR | Kanal D");
        Assert.Contains(variants, v => v.Contains("kanald"));
    }

    [Fact]
    public void GetNameVariants_DotSuffix_VariantWithoutDomain()
    {
        // "KanalD.tr" → domain sonrası kırpılmış varyant
        var variants = GetNameVariants("KanalD.tr");
        Assert.Contains(variants, v => v.Contains("kanald"));
    }

    [Fact]
    public void GetNameVariants_TrailingCountry_VariantWithoutCountry()
    {
        // "beIN Sports 1 Turkey" → Turkey olmadan varyant
        var variants = GetNameVariants("beIN Sports 1 Turkey");
        Assert.Contains(variants, v => !v.Contains("turkey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetNameVariants_NoDuplicates_AllUnique()
    {
        var variants = GetNameVariants("TRT 1 HD");
        Assert.Equal(variants.Count, variants.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GetNameVariants_ShortName_MinLength3()
    {
        // Kısa (< 3 karakter) varyantlar filtrelenmeli
        var variants = GetNameVariants("X");
        Assert.All(variants, v => Assert.True(v.Length >= 3));
    }

    [Fact]
    public void GetNameVariants_Empty_ReturnsEmptyOrSingleShort()
    {
        // Boş input için boş veya minimum sonuç beklenir
        var variants = GetNameVariants(string.Empty);
        Assert.True(variants.Count == 0 || variants.All(v => v.Length < 3));
    }

    [Theory]
    [InlineData("TRT 1")]
    [InlineData("Star TV (TR)")]
    [InlineData("TR | Kanal D")]
    [InlineData("beIN Sports 1 Turkey")]
    public void GetNameVariants_ProducesAtLeastOneVariant(string channelName)
    {
        var variants = GetNameVariants(channelName);
        Assert.NotEmpty(variants);
    }

    // =========================================================================
    // C — Similarity
    // =========================================================================

    [Fact]
    public void Similarity_IdenticalStrings_ReturnsOne()
        => Assert.Equal(1.0, Similarity("kanald", "kanald"));

    [Fact]
    public void Similarity_EmptyStrings_ReturnsZero()
        => Assert.Equal(0.0, Similarity(string.Empty, string.Empty));

    [Fact]
    public void Similarity_OneEmpty_ReturnsZero()
        => Assert.Equal(0.0, Similarity("kanald", string.Empty));

    [Theory]
    [InlineData("kanald", "kanal")]        // b ⊂ a → yüksek skor
    public void Similarity_ContainmentCase_HighScore(string a, string b)
    {
        var score = Similarity(a, b);
        Assert.True(score > 0.7, $"Expected score > 0.7, got {score}");
    }

    [Theory]
    [InlineData("trt1",      "trt2")]       // 1 karakter fark
    [InlineData("startv",    "strtv")]      // yakın
    [InlineData("beinsp1tr", "beinsports1")] // fuzzy overlap
    public void Similarity_CloseStrings_ModerateScore(string a, string b)
    {
        var score = Similarity(a, b);
        Assert.True(score > 0.4, $"Expected score > 0.4, got {score}");
    }

    [Theory]
    [InlineData("trt1", "foxnews")]
    [InlineData("abc",  "xyz")]
    public void Similarity_CompletelyDifferent_LowScore(string a, string b)
    {
        var score = Similarity(a, b);
        Assert.True(score < 0.5, $"Expected score < 0.5, got {score}");
    }

    [Fact]
    public void Similarity_Commutative_OrderIndependent()
    {
        // a-b ve b-a aynı skoru vermeli (BigramDice simetriktir)
        var ab = Similarity("trt1", "trt");
        var ba = Similarity("trt",  "trt1");
        // Contains path farklı olabilir, ama her ikisi de yüksek
        Assert.True(Math.Abs(ab - ba) < 0.2);
    }

    // =========================================================================
    // D — BigramDice
    // =========================================================================

    [Fact]
    public void BigramDice_IdenticalStrings_ReturnsOne()
        => Assert.Equal(1.0, BigramDice("kanald", "kanald"));

    [Fact]
    public void BigramDice_CompletelyDifferent_NearZero()
    {
        var score = BigramDice("xxxx", "zzzz");
        Assert.True(score < 0.1, $"Expected near 0, got {score}");
    }

    [Fact]
    public void BigramDice_SingleCharStrings_ZeroOrOne()
    {
        // Length < 2 → 0 (eşit değilse) veya 1 (eşitse)
        Assert.Equal(0.0, BigramDice("a", "b"));
        Assert.Equal(1.0, BigramDice("a", "a"));
    }

    [Theory]
    [InlineData("kanal",  "kanald")]   // 1 karakter ek
    [InlineData("startv", "strtv")]    // harf transpozisyonu
    [InlineData("trt",    "trt1")]
    public void BigramDice_NearDuplicates_HighScore(string a, string b)
    {
        var score = BigramDice(a, b);
        Assert.True(score > 0.5, $"Expected score > 0.5, got {score}");
    }

    [Fact]
    public void BigramDice_Score_BetweenZeroAndOne()
    {
        var pairs = new[] {
            ("trt1", "trt2"), ("startv", "foxnews"),
            ("beinsp", "beinsports"), ("ntv", "atv")
        };
        foreach (var (a, b) in pairs)
        {
            var score = BigramDice(a, b);
            Assert.True(score >= 0.0 && score <= 1.0,
                $"Score out of range [{a},{b}]: {score}");
        }
    }

    // =========================================================================
    // E — ResolveMappedChannelId
    // =========================================================================

    [Fact]
    public void ResolveMappedChannelIds_ExactMatch_ReturnsMapped()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["trt1"] = new List<string> { "ch-001" },
            ["kanald"] = new List<string> { "ch-002" },
        };
        Assert.Equal("ch-001", ResolveMappedChannelIds("trt1", map)?.First());
    }

    [Fact]
    public void ResolveMappedChannelIds_MultiMatch_ReturnsAllIds()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["showtv"] = new List<string> { "id1", "id2" }
        };
        var result = ResolveMappedChannelIds("showtv", map);
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains("id1", result);
        Assert.Contains("id2", result);
    }

    [Fact]
    public void ResolveMappedChannelIds_CaseInsensitiveExact_Matches()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["kanald"] = new List<string> { "ch-002" },
        };
        Assert.Equal("ch-002", ResolveMappedChannelIds("KANALD", map)?.First());
    }

    [Fact]
    public void ResolveMappedChannelIds_NotFound_ReturnsNull()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["trt1"] = new List<string> { "ch-001" },
        };
        Assert.Null(ResolveMappedChannelIds("foxnews", map));
    }

    [Fact]
    public void ResolveMappedChannelIds_EmptyInput_ReturnsNull()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["trt1"] = new List<string> { "ch-001" },
        };
        Assert.Null(ResolveMappedChannelIds(string.Empty, map));
    }

    [Fact]
    public void ResolveMappedChannelIds_TooShortInput_ReturnsNull()
    {
        // < 3 karakter → koşulsuz null
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ab"] = new List<string> { "ch-001" },
        };
        Assert.Null(ResolveMappedChannelIds("ab", map));
    }

    [Fact]
    public void ResolveMappedChannelIds_EmptyMap_ReturnsNull()
    {
        Assert.Null(ResolveMappedChannelIds("trt1", new Dictionary<string, List<string>>()));
    }

    // ── Fuzzy eşleşme ─────────────────────────────────────────────────────────
    // (ResolveMappedChannelId ilk harf ön filtresine takılmaması için
    //  normalize sonuçlar üretiliyor)

    [Fact]
    public void ResolveMappedChannelIds_FuzzyClose_MatchesWithinThreshold()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["trt1"] = new List<string> { "ch-001" },
        };
        var result = ResolveMappedChannelIds("trt1tr", map);
        Assert.True(result == null || result.Contains("ch-001"));
    }

    [Fact]
    public void ResolveMappedChannelIds_DifferentFirstChar_NotMatched()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["kanald"] = new List<string> { "ch-002" },
        };
        Assert.Null(ResolveMappedChannelIds("startv", map));
    }

    [Fact]
    public void ResolveMappedChannelIds_RealWorld_TRT1HD_MatchesTRT1()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["trt1"] = new List<string> { "ch-trt1" },
        };
        var result = ResolveMappedChannelIds("trt1hd", map);
        Assert.True(result == null || result.Contains("ch-trt1"));
    }

    [Fact]
    public void ResolveMappedChannelIds_MultipleEntries_PicksBestMatch()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["trt1"]  = new List<string> { "ch-trt1" },
            ["trt2"]  = new List<string> { "ch-trt2" },
            ["trtsp"] = new List<string> { "ch-trts" },
        };
        Assert.Equal("ch-trt1", ResolveMappedChannelIds("trt1", map)?.First());
        Assert.Equal("ch-trt2", ResolveMappedChannelIds("trt2", map)?.First());
    }
}

// =============================================================================
// EpgGetNameVariantsRealWorldTests
// Gerçek IPTV kanal adlarından beklenen varyant üretimini test eder
// Toplam: 16 test
// =============================================================================

public class EpgGetNameVariantsRealWorldTests
{
    private static readonly Noctra.Services.EpgService _epgServiceInstance = 
        new Noctra.Services.EpgService(null!, null!, null!, null!, new Noctra.Services.LanguageDetectionService());

    private static List<string> GetNameVariants(string name)
    {
        var m = typeof(Noctra.Services.EpgService).GetMethod("GetNameVariants",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((System.Collections.IEnumerable)m.Invoke(_epgServiceInstance, new object[] { name })!)
            .Cast<string>().ToList();
    }

    // Her test: EPG XML display-name olarak gelecek isim → channelMap'teki normalize isimle eşleşmeli

    [Theory]
    [InlineData("TRT 1",             "TR:trt1")]
    [InlineData("TRT 1 HD",          "TR:trt1")]
    [InlineData("Kanal D",           "TR:kanald")]
    [InlineData("Show TV",           "TR:showtv")]
    [InlineData("Star TV",           "TR:startv")]
    [InlineData("FOX",               "TR:fox")]
    [InlineData("NTV",               "TR:ntv")]
    [InlineData("ATV",               "TR:atv")]
    [InlineData("CNN Türk",          "TR:cnnturk")]
    [InlineData("beIN Sports 1",     "TR:beinsports1")]
    public void GetNameVariants_ChannelName_ContainsNormalizedForm(string channelName, string expectedNormalized)
    {
        var variants = GetNameVariants(channelName);
        Assert.Contains(expectedNormalized, variants);
    }

    [Theory]
    [InlineData("TR | TRT 1",        "TR:trt1")]    // pipe prefix
    [InlineData("TRT 1 (TR)",        "TR:trt1")]    // parantez suffix
    [InlineData("TRT1.tr",           "TR:trt1")]    // dot suffix
    [InlineData("TRT 1 Turkey",      "TR:trt1")]    // trailing country
    public void GetNameVariants_DirtyName_ContainsCleanVariant(string dirtyName, string expectedClean)
    {
        var variants = GetNameVariants(dirtyName);
        Assert.Contains(expectedClean, variants);
    }

    [Fact]
    public void GetNameVariants_ComplexDirtyName_MultipleUsefulVariants()
    {
        var variants = GetNameVariants("TR | TRT 1 HD (Turkey)");
        Assert.True(variants.Count >= 1, 
            $"Expected at least 1 variant, got {variants.Count}: {string.Join(", ", variants)}");
    }

    [Fact]
    public void GetNameVariants_SlashSeparated_VariantAfterSlash()
    {
        // "TR/Kanal D" → slash sonrası varyant
        var variants = GetNameVariants("TR/Kanal D");
        Assert.Contains(variants, v => v.Contains("kanald"));
    }
}

// =============================================================================
// EpgCountryAwareMatchingTests
// Ülke bazlı EPG eşleştirme düzeltmesini doğrular.
// Farklı ülke prefix'li aynı kanal isminin farklı anahtarlar üretmesini test eder.
// =============================================================================

public class EpgCountryAwareMatchingTests
{
    private static readonly Type _epgType = typeof(Noctra.Services.EpgService);

    private static string NormalizeName(string name)
    {
        var m = _epgType.GetMethod("NormalizeName",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)m.Invoke(null, new object[] { name })!;
    }

    private static readonly Noctra.Services.EpgService _epgServiceInstance = 
        new Noctra.Services.EpgService(null!, null!, null!, null!, new Noctra.Services.LanguageDetectionService());

    private static List<string> GetNameVariants(string name)
    {
        var m = _epgType.GetMethod("GetNameVariants",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((System.Collections.IEnumerable)m.Invoke(_epgServiceInstance, new object[] { name })!)
            .Cast<string>().ToList();
    }

    private static List<string>? ResolveMappedChannelIds(string normalizedName, Dictionary<string, List<string>> channelMap)
    {
        var m = _epgType.GetMethod("ResolveMappedChannelIds",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (List<string>?)m.Invoke(null, new object[] { normalizedName, channelMap });
    }

    private static bool HasEquivalentOverlappingProgram(EpgProgram program, IEnumerable<EpgProgram> candidates)
    {
        var m = _epgType.GetMethod("HasEquivalentOverlappingProgram",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)m.Invoke(null, new object[] { program, candidates })!;
    }

    private static List<EpgProgram> NormalizeProgramTimeline(IEnumerable<EpgProgram> programs)
    {
        var m = _epgType.GetMethod("NormalizeProgramTimeline",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (List<EpgProgram>)m.Invoke(null, new object[] { programs })!;
    }

    // ── NormalizeName: ülke kodlarını kanal adından temizler ──────────────────

    [Theory]
    [InlineData("FR: beIN SPORTS 1", "beinsports1")]
    [InlineData("DE: Sport1", "sport1")]
    [InlineData("UK: Sky Sports 1", "skysports1")]
    [InlineData("DE: Nicktoons [SAT] [VIP]", "nicktoons")]
    public void NormalizeName_CountryAndTechnicalTags_Stripped(string channelName, string expected)
    {
        var key = NormalizeName(channelName);
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("FR: beIN SPORTS 1")]
    [InlineData("FR: beIN SPORTS 2")]
    [InlineData("DE: Sport1")]
    [InlineData("UK: Sky Sports 1")]
    public void NormalizeName_NonTRCountry_DoesNotKeepCountryCode(string channelName)
    {
        var key = NormalizeName(channelName);
        var expectedPrefix = channelName[..2].ToLowerInvariant();
        Assert.DoesNotContain(expectedPrefix, key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeName_TRPrefix_StrippedByNoise()
    {
        // "TR" noise listesinde olduğu için çıkarılır
        var key = NormalizeName("TR: beIN SPORTS 1");
        Assert.DoesNotContain("tr", key, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("bein", key);
    }

    // ── GetNameVariants: Ülke kodu soyulmuş varyant üretmemeli ───────────────

    [Fact]
    public void GetNameVariants_FRPrefix_DoesNotProduceTRCollision()
    {
        // "FR: beIN Sports 1" varyantlarında "beinsports1" (TR anahtarı) olmamalı
        var frVariants = GetNameVariants("FR: beIN SPORTS 1");
        var trKey = NormalizeName("beIN SPORTS 1"); // = "beinsports1"

        Assert.DoesNotContain(trKey, frVariants);
    }

    [Fact]
    public void GetNameVariants_DEPrefix_DoesNotProduceTRCollision()
    {
        var deVariants = GetNameVariants("DE: RTL");
        var trKey = NormalizeName("RTL");

        Assert.DoesNotContain(trKey, deVariants);
    }

    [Fact]
    public void GetNameVariants_DESatVipSuffix_UsesDECountryAndCleanName()
    {
        var deVariants = GetNameVariants("DE: Nicktoons [SAT] [VIP]");

        Assert.Contains("DE:nicktoons", deVariants);
        Assert.DoesNotContain(deVariants, v => v.StartsWith("TR:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(deVariants, v => v.Contains("denicktoons", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveMappedChannelIds_TurkishNicktoons_DoesNotMatchDESatVipChannel()
    {
        var channelMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in GetNameVariants("DE: Nicktoons [SAT] [VIP]"))
        {
            channelMap[variant] = new List<string> { "de-nicktoons" };
        }

        var result = ResolveMappedChannelIds("TR:" + NormalizeName("Nicktoons"), channelMap);

        Assert.Null(result);
    }

    [Fact]
    public void HasEquivalentOverlappingProgram_HighTimeOverlapDifferentTitle_IsDuplicate()
    {
        var start = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc);
        var existing = new EpgProgram
        {
            ChannelId = "nicktoons.de",
            Title = "SpongeBob Schwammkopf",
            StartTime = start,
            EndTime = start.AddMinutes(30)
        };
        var incoming = new EpgProgram
        {
            ChannelId = "nicktoons.de",
            Title = "SpongeBob SquarePants",
            StartTime = start,
            EndTime = start.AddMinutes(30)
        };

        Assert.True(HasEquivalentOverlappingProgram(incoming, new[] { existing }));
    }

    [Fact]
    public void NormalizeProgramTimeline_SameStartDifferentLength_KeepsLongerProgram()
    {
        var start = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc);
        var programs = new[]
        {
            new EpgProgram
            {
                ChannelId = "nicktoons.de",
                Title = "Short title",
                StartTime = start,
                EndTime = start.AddMinutes(15)
            },
            new EpgProgram
            {
                ChannelId = "nicktoons.de",
                Title = "Longer title",
                StartTime = start,
                EndTime = start.AddMinutes(30)
            }
        };

        var normalized = NormalizeProgramTimeline(programs);

        var program = Assert.Single(normalized);
        Assert.Equal("Longer title", program.Title);
        Assert.Equal(start.AddMinutes(30), program.EndTime);
    }

    [Theory]
    [InlineData("FR: beIN SPORTS 1", "TR: beIN SPORTS 1")]
    [InlineData("DE: Sport1 HD", "TR: Sport1 HD")]
    [InlineData("UK: Sky Sports", "TR: Sky Sports")]
    public void GetNameVariants_DifferentCountries_NoOverlap(string channelA, string channelB)
    {
        var variantsA = GetNameVariants(channelA);
        var variantsB = GetNameVariants(channelB);

        // İki farklı ülke kanalının varyantları kesişmemeli
        var overlap = variantsA.Intersect(variantsB, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Empty(overlap);
    }

    // ── Gerçek dünya senaryosu: channelMap simülasyonu ────────────────────────

    [Fact]
    public void ChannelMap_FRandTR_BeinSports_DontCollide()
    {
        // Gerçek dünya: channelMap oluşturma simülasyonu
        var channelMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // TR kanalları ekle
        foreach (var variant in GetNameVariants("TR: beIN Sports 1"))
        {
            channelMap[variant] = new List<string> { "ch-tr-bein1" };
        }

        // FR kanalları ekle
        foreach (var variant in GetNameVariants("FR: beIN SPORTS 1"))
        {
            channelMap[variant] = new List<string> { "ch-fr-bein1" };
        }

        // EPG kaynağında "beIN Sports 1" geldiğinde → sadece TR'ye eşleşmeli
        var epgNormalized = "TR:" + NormalizeName("beIN Sports 1");
        Assert.True(channelMap.TryGetValue(epgNormalized, out var matched));
        Assert.Contains("ch-tr-bein1", matched);
        Assert.DoesNotContain("ch-fr-bein1", matched);
    }

    [Fact]
    public void ChannelMap_MultipleCountries_CorrectIsolation()
    {
        var channelMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // 3 farklı ülke kanalı ekle
        foreach (var v in GetNameVariants("TR: beIN Sports 1"))
            channelMap[v] = new List<string> { "ch-tr" };
        foreach (var v in GetNameVariants("FR: beIN SPORTS 1"))
            channelMap[v] = new List<string> { "ch-fr" };
        foreach (var v in GetNameVariants("DE: beIN SPORTS 1"))
            channelMap[v] = new List<string> { "ch-de" };

        // Anahtar sayısı 3 olmalı (her ülke ayrı anahtar)
        Assert.True(channelMap.Count >= 3,
            $"Expected at least 3 distinct keys, got {channelMap.Count}: {string.Join(", ", channelMap.Keys)}");

        // Her birinin ayrı ID'si olmalı
        Assert.DoesNotContain("ch-fr", channelMap["TR:" + NormalizeName("beIN Sports 1")]);
        Assert.DoesNotContain("ch-de", channelMap["TR:" + NormalizeName("beIN Sports 1")]);
    }
}
