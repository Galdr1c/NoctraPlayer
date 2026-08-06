using Noctra.Core.Services;
using Xunit;

namespace Noctra.Tests;

public class PromoCodeFormatterTests
{
    [Theory]
    [InlineData("PROM-OEXA-MPLE-7D", true)]
    [InlineData("ABCD", true)]
    [InlineData("ABCD-EFGH", true)]
    [InlineData("ABCD-EFGH-IJKL-MNOP-QRST", true)]
    [InlineData("A1B2-C3D4", true)]
    public void IsValid_WithValidCode_ReturnsTrue(string code, bool expected)
    {
        Assert.Equal(expected, PromoCodeFormatter.IsValid(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ABCD-")]
    [InlineData("-ABCD")]
    [InlineData("ABCD--EFGH")]
    [InlineData("ABCD-EFGH-IJKL-MNOP-QRST-UVWX")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUV")]
    [InlineData("abcd-efgh")]
    [InlineData("ABC D")]
    [InlineData("ABCD_EFGH")]
    [InlineData("A1B2-C3D4-E5F6-G7H8-I9J0-K1L2")]
    public void IsValid_WithInvalidCode_ReturnsFalse(string? code)
    {
        Assert.False(PromoCodeFormatter.IsValid(code));
    }

    [Fact]
    public void IsValid_NonAsciiAlphanumericCharacters_AreRejected()
    {
        Assert.False(PromoCodeFormatter.IsValid("ÇÖZÜM-TEST"));
    }

    [Fact]
    public void Normalize_RawCodeWithSpacesAndDashes_FormatsCorrectly()
    {
        Assert.Equal("PROM-OEXA-MPLE-7D", PromoCodeFormatter.Normalize("promo example 7d"));
    }

    [Fact]
    public void Normalize_AlreadyFormattedCode_ReturnsSameFormat()
    {
        Assert.Equal("PROM-OEXA-MPLE-7D", PromoCodeFormatter.Normalize("PROMO-EXAMPLE-7D"));
    }

    [Fact]
    public void Normalize_LowerCaseInput_ConvertsToUpper()
    {
        Assert.Equal("PROM-OEXA-MPLE-7D", PromoCodeFormatter.Normalize("promo-example-7d"));
    }

    [Fact]
    public void Normalize_TurkishCharacters_AreRemoved()
    {
        Assert.Equal("ZMTE-ST", PromoCodeFormatter.Normalize("çözüm-test"));
    }

    [Fact]
    public void Normalize_MoreThan20Chars_Truncates()
    {
        Assert.Equal("ABCD-EFGH-IJKL-MNOP-QRST", PromoCodeFormatter.Normalize("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_NullOrEmpty_ReturnsEmpty(string? input, string expected)
    {
        Assert.Equal(expected, PromoCodeFormatter.Normalize(input));
    }

    [Fact]
    public void Normalize_SymbolsAndDigits_FormatsCorrectly()
    {
        Assert.Equal("A1B2-C3D4", PromoCodeFormatter.Normalize("a1-b2 c3 d4"));
    }

    [Fact]
    public void Normalize_MaxCharacters_IsTwenty()
    {
        Assert.Equal(20, PromoCodeFormatter.MaxCharacters);
    }

    [Theory]
    [InlineData("PROMOEXAMPLE7D", "PROM-OEXA-MPLE-7D")]
    [InlineData("ABCDEFGHIJKL", "ABCD-EFGH-IJKL")]
    [InlineData("AB", "AB")]
    [InlineData("ABCDE", "ABCD-E")]
    public void FormatWithDashes_FormatsCorrectly(string alnum, string expected)
    {
        Assert.Equal(expected, PromoCodeFormatter.FormatWithDashes(alnum));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void FormatWithDashes_NullOrEmpty_ReturnsEmpty(string? alnum, string expected)
    {
        Assert.Equal(expected, PromoCodeFormatter.FormatWithDashes(alnum ?? string.Empty));
    }

    [Fact]
    public void CalculateCaretPosition_ReturnsCorrectPosition()
    {
        Assert.Equal(0, PromoCodeFormatter.CalculateCaretPosition(0, 24));
        Assert.Equal(1, PromoCodeFormatter.CalculateCaretPosition(1, 24));
        Assert.Equal(4, PromoCodeFormatter.CalculateCaretPosition(4, 24));
        Assert.Equal(5, PromoCodeFormatter.CalculateCaretPosition(5, 24));
        Assert.Equal(9, PromoCodeFormatter.CalculateCaretPosition(8, 24));
        Assert.Equal(14, PromoCodeFormatter.CalculateCaretPosition(12, 24));
        Assert.Equal(19, PromoCodeFormatter.CalculateCaretPosition(16, 24));
        Assert.Equal(24, PromoCodeFormatter.CalculateCaretPosition(20, 24));
    }

    [Fact]
    public void CalculateCaretPosition_ClampsCountToMaxCharacters()
    {
        Assert.Equal(24, PromoCodeFormatter.CalculateCaretPosition(25, 24));
    }

    [Fact]
    public void IsCanonical_ReturnsTrueForCanonicalOnly()
    {
        Assert.True(PromoCodeFormatter.IsCanonical("PROMO-EXAMPLE-7D"));
        Assert.False(PromoCodeFormatter.IsCanonical("promo-example-7d"));
        Assert.False(PromoCodeFormatter.IsCanonical("  PROMO-EXAMPLE-7D  "));
    }
}
