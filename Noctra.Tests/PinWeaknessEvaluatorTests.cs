using Noctra.Services;
using Xunit;

namespace Noctra.Tests
{
    public class PinWeaknessEvaluatorTests
    {
        [Theory]
        [InlineData("0000")]
        [InlineData("1111")]
        [InlineData("2222")]
        [InlineData("9999")]
        public void IsWeak_AllSameDigits_ReturnsTrue(string pin)
        {
            Assert.True(PinWeaknessEvaluator.IsWeak(pin));
        }

        [Theory]
        [InlineData("0123")]
        [InlineData("1234")]
        [InlineData("2345")]
        [InlineData("6789")]
        [InlineData("9876")]
        [InlineData("8765")]
        [InlineData("4321")]
        [InlineData("3210")]
        public void IsWeak_SequentialDigits_ReturnsTrue(string pin)
        {
            Assert.True(PinWeaknessEvaluator.IsWeak(pin));
        }

        [Theory]
        [InlineData("1212")] // abab
        [InlineData("6969")] // abab
        [InlineData("1122")] // aabb
        [InlineData("9988")] // aabb
        [InlineData("1221")] // abba
        [InlineData("1001")] // abba
        public void IsWeak_RepeatedPairPatterns_ReturnsTrue(string pin)
        {
            Assert.True(PinWeaknessEvaluator.IsWeak(pin));
        }

        [Theory]
        [InlineData("2026")] // current year
        [InlineData("2000")] // year
        [InlineData("1990")] // birth year
        [InlineData("2099")] // upper year bound
        [InlineData("1900")] // lower year bound
        public void IsWeak_YearPatterns_ReturnsTrue(string pin)
        {
            Assert.True(PinWeaknessEvaluator.IsWeak(pin));
        }

        [Theory]
        [InlineData("2580")] // vertical keypad column
        [InlineData("0852")] // reverse vertical column
        [InlineData("1357")] // odd digits
        [InlineData("2468")] // even digits
        [InlineData("1004")] // common list
        public void IsWeak_CommonPins_ReturnsTrue(string pin)
        {
            Assert.True(PinWeaknessEvaluator.IsWeak(pin));
        }

        [Theory]
        [InlineData("4837")]
        [InlineData("5291")]
        [InlineData("8674")]
        [InlineData("6398")]
        [InlineData("7513")]
        public void IsWeak_RandomLookingPins_ReturnsFalse(string pin)
        {
            Assert.False(PinWeaknessEvaluator.IsWeak(pin));
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("12a4")]
        [InlineData("")]
        [InlineData(null)]
        public void IsWeak_InvalidShape_ReturnsFalse(string? pin)
        {
            Assert.False(PinWeaknessEvaluator.IsWeak(pin));
        }
    }
}
