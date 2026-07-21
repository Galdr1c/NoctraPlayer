using Noctra.Core.Services;

namespace Noctra.Tests;

/// <summary>
/// StalkerMacFormatter için kapsamlı unit testler.
/// Raporunda belirtilen tüm senaryoları kapsar.
/// </summary>
public class StalkerMacFormatterTests
{
    // ═══════════════════════════════════════════════════════════
    // IsValid Tests
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData("00:1A:79:AB:CD:EF", true)]
    [InlineData("00:1a:79:ab:cd:ef", true)]
    [InlineData("FF:FF:FF:FF:FF:FF", true)]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    [InlineData("00:00:00:00:00:00", true)]
    public void IsValid_WithValidMac_ReturnsTrue(string mac, bool expected)
    {
        Assert.Equal(expected, StalkerMacFormatter.IsValid(mac));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00:1A:79:AB:CD")]
    [InlineData("00:1A:79:AB:CD:EF:12")]
    [InlineData("001A79ABCDEF")]
    [InlineData("00:1A:79:AB:CD:ZZ")]
    [InlineData("00:1A:79:AB:CD:EF:")]
    public void IsValid_WithInvalidMac_ReturnsFalse(string? mac)
    {
        Assert.False(StalkerMacFormatter.IsValid(mac));
    }

    // ═══════════════════════════════════════════════════════════
    // Normalize Tests — Raporunda belirtilen senaryolar
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Normalize_RawHexInput_FormatsCorrectly()
    {
        // Senaryo: "001a79abcdef" → "00:1A:79:AB:CD:EF"
        var result = StalkerMacFormatter.Normalize("001a79abcdef");
        Assert.Equal("00:1A:79:AB:CD:EF", result);
    }

    [Fact]
    public void Normalize_RawHexInputUpperCase_FormatsCorrectly()
    {
        // Senaryo: "001A79ABCDEF" → "00:1A:79:AB:CD:EF"
        var result = StalkerMacFormatter.Normalize("001A79ABCDEF");
        Assert.Equal("00:1A:79:AB:CD:EF", result);
    }

    [Fact]
    public void Normalize_AlreadyFormattedMac_ReturnsSameFormat()
    {
        // Senaryo: Formatlanmış MAC yapıştırma
        var result = StalkerMacFormatter.Normalize("00:1A:79:AB:CD:EF");
        Assert.Equal("00:1A:79:AB:CD:EF", result);
    }

    [Fact]
    public void Normalize_PartiallyFormatted_ReturnsCorrectFormat()
    {
        // Senaryo: Yarı biçimlendirilmiş giriş
        var result = StalkerMacFormatter.Normalize("00:1A:79ABCD");
        Assert.Equal("00:1A:79:AB:CD", result);
    }

    [Fact]
    public void Normalize_MoreThan12HexChars_Truncates()
    {
        // Senaryo: 12 karakterden uzun giriş — kırpılmalı
        var result = StalkerMacFormatter.Normalize("001A79ABCDEF123456");
        Assert.Equal("00:1A:79:AB:CD:EF", result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_NullOrEmpty_ReturnsEmpty(string? input, string expected)
    {
        Assert.Equal(expected, StalkerMacFormatter.Normalize(input!));
    }

    [Fact]
    public void Normalize_HexWithColonsMixed_FormatsCorrectly()
    {
        // Senaryo: ':' karakterleri karışık
        var result = StalkerMacFormatter.Normalize("00:1A:79:AB:CD:EF");
        Assert.Equal("00:1A:79:AB:CD:EF", result);
    }

    [Fact]
    public void Normalize_LowerCaseInput_ConvertsToUpper()
    {
        // Senaryo: Küçük harfli giriş büyük harfe çevirilmeli
        var result = StalkerMacFormatter.Normalize("001a79abcdef");
        Assert.Equal("00:1A:79:AB:CD:EF", result);
    }

    [Fact]
    public void Normalize_NonHexCharacters_AreRemoved()
    {
        // Senaryo: Hex olmayan karakterler temizlenmeli
        var result = StalkerMacFormatter.Normalize("001A79ABCDEFXYZ");
        // XYZ hex değil, temizlenmeli → 001A79ABCDEF
        Assert.Equal("00:1A:79:AB:CD:EF", result);
    }

    // ═══════════════════════════════════════════════════════════
    // FormatMacWithColons Tests
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData("001A79ABCDEF", "00:1A:79:AB:CD:EF")]
    [InlineData("AABBCCDDEEFF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("00", "00")]
    [InlineData("001A", "00:1A")]
    [InlineData("001A79", "00:1A:79")]
    public void FormatMacWithColons_FormatsCorrectly(string hex, string expected)
    {
        Assert.Equal(expected, StalkerMacFormatter.FormatMacWithColons(hex));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void FormatMacWithColons_NullOrEmpty_ReturnsEmpty(string? hex, string expected)
    {
        Assert.Equal(expected, StalkerMacFormatter.FormatMacWithColons(hex!));
    }

    // ═══════════════════════════════════════════════════════════
    // CalculateCaretPosition Tests
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, 2, 0)]     // Boş — ilk pozisyon
    [InlineData(2, 5, 2)]     // İlk byte: "00" → pozisyon 2
    [InlineData(4, 8, 5)]     // İki byte: "001A" → "00:1A" → pozisyon 5
    [InlineData(6, 11, 8)]    // Üç byte: "001A79" → "00:1A:79" → pozisyon 8
    [InlineData(8, 14, 11)]   // Dört byte: "001A79AB" → "00:1A:79:AB" → pozisyon 11
    [InlineData(10, 17, 14)]  // Beş byte: "001A79ABCD" → "00:1A:79:AB:CD" → pozisyon 14
    [InlineData(12, 17, 17)]  // Altı byte: tam MAC → pozisyon 17
    public void CalculateCaretPosition_ReturnsCorrectPosition(
        int hexCountBeforeCaret, int formattedLength, int expected)
    {
        Assert.Equal(expected, StalkerMacFormatter.CalculateCaretPosition(hexCountBeforeCaret, formattedLength));
    }

    [Fact]
    public void CalculateCaretPosition_ClampsHexCountTo12()
    {
        // 12'den fazla hex karakter — 12'ye kırpılmalı
        var result = StalkerMacFormatter.CalculateCaretPosition(20, 17);
        // hexCount 12'ye kırpılır → 12 + (12/2) = 18 → min(18, 17) = 17
        Assert.Equal(17, result);
    }

    // ═══════════════════════════════════════════════════════════
    // Prefix Tests
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Prefix_IsCorrectStalkerMacPrefix()
    {
        Assert.Equal("00:1A:79:", StalkerMacFormatter.Prefix);
    }
}
