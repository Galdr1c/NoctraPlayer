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

        // Intentionally present only in en-US — verifies cascade to FallbackLanguage ("en-US").
        Assert.Equal("__EN_FALLBACK_ONLY__", service.GetString("Localization.Tests.FallbackEnglishOnly"));
    }

    [Theory]
    [InlineData("tr", "tr-TR")]
    [InlineData("en", "en-US")]
    [InlineData("de-DE", "de-DE")]
    [InlineData("fr", "fr-FR")]
    [InlineData("es", "es-ES")]
    [InlineData("unknown", "en-US")]
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

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void PromoResultKeys_ExistInEverySupportedLanguage(string language)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        var keys = new[]
        {
            "GlobalSettings.Promo.SuccessFormat",
            "GlobalSettings.Promo.Error.PremiumEdition",
            "GlobalSettings.Promo.Error.EmptyCode",
            "GlobalSettings.Promo.Error.InvalidCode",
            "GlobalSettings.Promo.Error.Inactive",
            "GlobalSettings.Promo.Error.InvalidDuration",
            "GlobalSettings.Promo.Error.Expired",
            "GlobalSettings.Promo.Error.AlreadyRedeemed",
            "GlobalSettings.Promo.Error.Offline",
            "GlobalSettings.Promo.Error.Timeout",
            "GlobalSettings.Promo.Error.ServiceUnavailable",
            "GlobalSettings.Promo.Error.ConfigurationInvalid",
            "GlobalSettings.Promo.Error.Generic",
            "GlobalSettings.Promo.Error.GrantCorrupted"
        };

        foreach (var key in keys)
        {
            Assert.NotEqual(key, service.GetString(key));
        }
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void PlayerAccessibilityKeys_ExistInEverySupportedLanguage(string language)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        var keys = new[]
        {
            "Player.Accessibility.Play",
            "Player.Accessibility.Pause",
            "Player.Accessibility.Mute",
            "Player.Accessibility.Unmute",
            "Player.Accessibility.AddFavorite",
            "Player.Accessibility.RemoveFavorite",
            "Player.Accessibility.Lock",
            "Player.Accessibility.Unlock",
            "Player.Accessibility.GoToLive",
            "Player.Accessibility.Timeline.Progress",
            "Player.Accessibility.Timeline.UnknownDuration",
            "Player.Accessibility.Timeline.LiveProgress",
            "Player.Accessibility.Timeline.LiveProgressWithTitle",
            "Player.Accessibility.Time.Hour.One",
            "Player.Accessibility.Time.Hour.Many",
            "Player.Accessibility.Time.Minute.One",
            "Player.Accessibility.Time.Minute.Many",
            "Player.Accessibility.Time.Second.One",
            "Player.Accessibility.Time.Second.Many"
        };

        foreach (var key in keys)
        {
            Assert.NotEqual(key, service.GetString(key));
        }
    }
}
