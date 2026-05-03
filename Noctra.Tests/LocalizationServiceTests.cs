using Noctra.Services;

namespace Noctra.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void GetString_ReturnsSelectedLanguageValue()
    {
        var service = new LocalizationService();

        service.SetLanguage("en");

        Assert.Equal("Settings", service.GetString("GlobalSettings.Title"));
    }

    [Fact]
    public void GetString_FallsBackToEnglish_WhenSelectedLanguageMissesKey()
    {
        var service = new LocalizationService();

        service.SetLanguage("de");

        Assert.Equal("App Language", service.GetString("Settings.Language.Title"));
    }

    [Theory]
    [InlineData("tr", "tr-TR")]
    [InlineData("en", "en-US")]
    [InlineData("de-DE", "de-DE")]
    [InlineData("fr", "fr-FR")]
    [InlineData("es", "es-ES")]
    [InlineData("unknown", "tr-TR")]
    public void NormalizeLanguageCode_ReturnsExpectedValue(string input, string expected)
    {
        Assert.Equal(expected, LocalizationService.NormalizeLanguageCode(input));
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChanged_WhenValueChanges()
    {
        var service = new LocalizationService();
        var raised = false;

        service.LanguageChanged += () => raised = true;

        service.SetLanguage("fr");

        Assert.True(raised);
        Assert.Equal("fr-FR", service.CurrentLanguage);
    }

    [Fact]
    public void GetString_ReturnsKey_WhenTranslationDoesNotExist()
    {
        var service = new LocalizationService();

        Assert.Equal("Missing.Key", service.GetString("Missing.Key"));
    }
}
