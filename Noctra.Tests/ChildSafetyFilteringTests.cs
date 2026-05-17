using Noctra.Models;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests
{
    public class ChildSafetyFilteringTests
    {
        [Theory]
        [InlineData("Niloya", true)]
        [InlineData("Peppa Pig", true)]
        [InlineData("Adult Movie", false)]
        [InlineData("XXX Content", false)]
        [InlineData("Sex and the City", false)]
        [InlineData("Pornstar", false)]
        [InlineData("18+ Horror", false)]
        [InlineData("Matrix (1998)", false)] // Old year check
        [InlineData("Superman", true)]  // 'man' blacklist'ten çıkarıldı, Superman bariz çocuk içeriği
        [InlineData("Spiderman", true)] // 'man' blacklist'ten çıkarıldı
        [InlineData("Toy Story Dublaj", true)]  // 'dublaj' blacklist'ten çıkarıldı
        [InlineData("Lara Croft Dublaj", false)] // 'dublaj' çıktı ama 'lara' hala blacklist'te
        public void ChildSafetyHelper_Keywords_Are_Identified(string input, bool expectedSafe)
        {
            var blacklist = ChildSafetyHelper.GetCriticalBlacklist();
            bool hasBlacklist = blacklist.Any(b => input.Contains(b, StringComparison.OrdinalIgnoreCase));
            bool isOld = ChildSafetyHelper.IsOldContent(input);
            
            bool isSafe = !hasBlacklist && !isOld;
            
            Assert.Equal(expectedSafe, isSafe);
        }

        [Theory]
        [InlineData("G", true)]
        [InlineData("TV-Y", true)]
        [InlineData("TV-G", true)]
        [InlineData("PG", true)]
        [InlineData("TV-MA", false)]
        [InlineData("R", false)]
        [InlineData("NC-17", false)]
        [InlineData("18", false)]
        [InlineData("Adult", false)]
        [InlineData("PG-13", false)] // Conservative fallback
        public void ChildSafetyHelper_Ratings_Are_Validated(string rating, bool expectedSafe)
        {
            bool isSafe = ChildSafetyHelper.IsSafeRating(rating);
            Assert.Equal(expectedSafe, isSafe);
        }
    }
}
