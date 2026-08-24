namespace Noctra.Tests;

public sealed class AdultCategoryClassifierTests
{
    [Theory]
    [InlineData("For Adults")]
    [InlineData("♦️[HOT] Adultos")]
    [InlineData("Pour adultes")]
    [InlineData("Für Erwachsene")]
    [InlineData("Yetişkin +18")]
    [InlineData("Conteúdo adulto")]
    [InlineData("Per adulti")]
    [InlineData("للبالغين")]
    [InlineData("Для взрослых")]
    [InlineData("Bang Bros")]
    [InlineData("Reality-Kings")]
    [InlineData("Digital_Playground")]
    [InlineData("Naughty America")]
    public void IsAdultCategory_RecognizesCommonLocalizedCategoryNames(string category)
    {
        Assert.True(IsAdultCategory(category));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("News")]
    [InlineData("Sussex Local")]
    [InlineData("Adult Swim Classics")]
    [InlineData("Pink TV")]
    [InlineData("Amateur Sports")]
    [InlineData("Gay News")]
    [InlineData("Passion Documentary")]
    [InlineData("Cam Weather")]
    public void IsAdultCategory_DoesNotMatchNormalCategories(string? category)
    {
        Assert.False(IsAdultCategory(category));
    }

    private static bool IsAdultCategory(string? category)
        => Noctra.Services.AdultCategoryClassifier.IsAdultCategory(category);
}
