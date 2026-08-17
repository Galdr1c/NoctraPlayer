using Noctra.Core.Services;
using Xunit;

namespace Noctra.Tests;

public class PromoCodeFormatterTests
{
    [Theory]
    [InlineData("NOC-G8K2-XW9P-7L4Q", true)]
    [InlineData("PROM-OEXA-MPLE-7D", true)]
    [InlineData("PROMO-EXAMPLE-7D", true)]
    [InlineData("ABCD", true)]
    [InlineData("ABCD-EFGH", true)]
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
    [InlineData("ABCD-EFGH-IJKL-MNOP-QRST")]
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
        Assert.Equal("PRO-MOEX-AMPL-E7D", PromoCodeFormatter.Normalize("promo example 7d"));
    }

    [Fact]
    public void Normalize_AlreadyFormattedCode_ReturnsSameFormat()
    {
        Assert.Equal("PRO-MOEX-AMPL-E7D", PromoCodeFormatter.Normalize("PROMO-EXAMPLE-7D"));
    }

    [Fact]
    public void Normalize_LowerCaseInput_ConvertsToUpper()
    {
        Assert.Equal("PRO-MOEX-AMPL-E7D", PromoCodeFormatter.Normalize("promo-example-7d"));
    }

    [Fact]
    public void Normalize_TurkishCharacters_AreRemoved()
    {
        Assert.Equal("ZMT-EST", PromoCodeFormatter.Normalize("çözüm-test"));
    }

    [Fact]
    public void Normalize_MoreThan15Chars_Truncates()
    {
        Assert.Equal("ABC-DEFG-HIJK-LMNO", PromoCodeFormatter.Normalize("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
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
        Assert.Equal("A1B-2C3D-4", PromoCodeFormatter.Normalize("a1-b2 c3 d4"));
    }

    [Fact]
    public void Normalize_MaxCharacters_IsFifteen()
    {
        Assert.Equal(15, PromoCodeFormatter.MaxCharacters);
    }

    [Fact]
    public void Normalize_RealCode_IsPreservedAsIs()
    {
        Assert.Equal("NOC-G8K2-XW9P-7L4Q", PromoCodeFormatter.Normalize("NOC-G8K2-XW9P-7L4Q"));
        Assert.Equal("NOC-G8K2-XW9P-7L4Q", PromoCodeFormatter.Normalize("noc g8k2 xw9p 7l4q"));
    }

    [Theory]
    [InlineData("NOCG8K2XW9P7L4Q", "NOC-G8K2-XW9P-7L4Q")]
    [InlineData("ABCDEFGHIJKL", "ABC-DEFG-HIJK-L")]
    [InlineData("AB", "AB")]
    [InlineData("ABCDE", "ABC-DE")]
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
        Assert.Equal(0, PromoCodeFormatter.CalculateCaretPosition(0, 18));
        Assert.Equal(1, PromoCodeFormatter.CalculateCaretPosition(1, 18));
        Assert.Equal(3, PromoCodeFormatter.CalculateCaretPosition(3, 18));
        Assert.Equal(5, PromoCodeFormatter.CalculateCaretPosition(4, 18));
        Assert.Equal(6, PromoCodeFormatter.CalculateCaretPosition(5, 18));
        Assert.Equal(8, PromoCodeFormatter.CalculateCaretPosition(7, 18));
        Assert.Equal(10, PromoCodeFormatter.CalculateCaretPosition(8, 18));
        Assert.Equal(13, PromoCodeFormatter.CalculateCaretPosition(11, 18));
        Assert.Equal(15, PromoCodeFormatter.CalculateCaretPosition(12, 18));
        Assert.Equal(18, PromoCodeFormatter.CalculateCaretPosition(15, 18));
    }

    [Fact]
    public void CalculateCaretPosition_ClampsCountToMaxCharacters()
    {
        Assert.Equal(18, PromoCodeFormatter.CalculateCaretPosition(25, 18));
    }

    [Fact]
    public void IsCanonical_ReturnsTrueForCanonicalOnly()
    {
        Assert.True(PromoCodeFormatter.IsCanonical("PROMO-EXAMPLE-7D"));
        Assert.False(PromoCodeFormatter.IsCanonical("promo-example-7d"));
        Assert.False(PromoCodeFormatter.IsCanonical("  PROMO-EXAMPLE-7D  "));
    }
}
