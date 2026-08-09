using System.Reflection;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests;

public sealed class EpgProgrammeLanguageTests
{
    [Fact]
    public void SelectLocalizedText_PrefersExactLocale_RegardlessOfXmlOrder()
    {
        var values = Values(
            ("en", "Main News"),
            ("tr-TR", "Ana Haber"),
            ("de", "Nachrichten"));

        Assert.Equal("Ana Haber", SelectLocalizedText(values, "tr-TR"));
    }

    [Theory]
    [InlineData("tr-TR", "tr", "Ana Haber")]
    [InlineData("tr", "tur", "Ana Haber")]
    [InlineData("de-DE", "ger", "Nachrichten")]
    [InlineData("fr-FR", "fre", "Informations")]
    public void SelectLocalizedText_MatchesBaseLanguageAndXmltvAliases(
        string preferredLanguage,
        string candidateLanguage,
        string expected)
    {
        var values = Values(
            ("en", "Fallback"),
            (candidateLanguage, expected));

        Assert.Equal(expected, SelectLocalizedText(values, preferredLanguage));
    }

    [Fact]
    public void SelectLocalizedText_PrefersUntaggedValue_WhenPreferredLanguageIsMissing()
    {
        var values = Values(
            ("de", "Nachrichten"),
            (null, "International News"),
            ("en", "Main News"));

        Assert.Equal("International News", SelectLocalizedText(values, "tr-TR"));
    }

    [Fact]
    public void SelectLocalizedText_UsesFirstNonEmptyValue_AsFinalDeterministicFallback()
    {
        var values = Values(
            ("de", "Nachrichten"),
            ("en", "Main News"),
            ("tr", "  "));

        Assert.Equal("Nachrichten", SelectLocalizedText(values, "es-ES"));
    }

    [Fact]
    public void ResolveEpgSources_CarriesPreferredLanguageToEverySource()
    {
        var sources = new EpgSourceResolver().ResolveEpgSources(
            providerEpgUrl: "https://provider.test/epg.xml",
            m3uEpgUrl: "https://playlist.test/guide.xml",
            customEpgUrls: new[] { "https://custom.test/xmltv.xml" },
            preferredLanguageCode: "TR-tr");

        var property = typeof(EpgSource).GetProperty(
            "PreferredLanguageCode",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.All(sources, source =>
            Assert.Equal("tr-TR", property!.GetValue(source)));
    }

    private static IReadOnlyList<KeyValuePair<string?, string>> Values(
        params (string? Language, string Value)[] values) =>
        values.Select(value =>
            new KeyValuePair<string?, string>(value.Language, value.Value)).ToList();

    private static string? SelectLocalizedText(
        IReadOnlyList<KeyValuePair<string?, string>> values,
        string? preferredLanguage)
    {
        var method = typeof(EpgService).GetMethod(
            "SelectLocalizedText",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return (string?)method!.Invoke(null, new object?[] { values, preferredLanguage });
    }
}
