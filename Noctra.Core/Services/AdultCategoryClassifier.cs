using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Noctra.Services;

/// <summary>
/// Classifies provider category names only. Content titles deliberately stay
/// out of this policy so legitimate titles cannot be demoted by a single word.
/// </summary>
internal static partial class AdultCategoryClassifier
{
    internal static bool IsAdultCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        var normalized = Normalize(category);
        if (normalized.Length == 0)
        {
            return false;
        }

        // Adult Swim is a general entertainment brand. Remove only that phrase;
        // an additional explicit marker such as "XXX" still classifies the group.
        normalized = AdultSwimPhraseRegex().Replace(normalized, " ");
        return AdultCategoryRegex().IsMatch(normalized);
    }

    internal static int GetSortRank(string? category)
        => IsAdultCategory(category) ? 1 : 0;

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = true;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character) || character == '+')
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator)
            {
                builder.Append(' ');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])adult\s+swim(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex AdultSwimPhraseRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:" +
        @"adult(?:s|o|os|a|as|i|e|es)?|" +
        @"yetiskin(?:ler)?|" +
        @"erwachsene(?:n|r|s)?|volwassen(?:en)?|dorosli|doroslych|" +
        @"для\s+взрослых|взросл(?:ые|ых)|" +
        @"للبالغين|بالغين|للكبار|اباحي(?:ة)?|" +
        @"成人|アダルト|성인|" +
        @"xxx|porn(?:o|os|a|as|ography|ographie|ografia|ografico|ografica|star)?|pornhub|" +
        @"erotic(?:a|o|os|as)?|erotique|erotik|erotiek|erotyka|эротика|" +
        @"sex|sexy|18\s*\+|\+\s*18|ab\s+18|mayores\s+de\s+18|x\s+rated|" +
        @"red\s*light|hentai|brazzers|bang\s*bros|reality\s*kings|" +
        @"digital\s*playground|naughty\s*america|penthouse|hustler|playboy|" +
        @"blue\s*movie|hardcore|softcore|web\s*cams?|striptease|fetish|bondage|bdsm|" +
        @"milf|mature" +
        @")(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex AdultCategoryRegex();
}
