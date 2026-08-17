namespace Noctra.Core.Services;

/// <summary>
/// Promosyon kodu biçimlendirme ve doğrulama servisi.
/// Hem mobil hem masaüstü platformlarda ortak kullanılır.
///
/// Kod formatı: NOC-XXXX-XXXX-XXXX (yalnızca ASCII alfanümerik, büyük harf;
/// ilk grup 3 karakter, sonraki gruplar 4'er; en fazla 15 karakter, 3 tire).
/// Örn: NOC-G8K2-XW9P-7L4Q. Doğrulama, doğal gruplu kodları da kabul eder.
/// </summary>
public static class PromoCodeFormatter
{
    /// <summary>
    /// Bir kodda bulunabilecek maksimum alfanümerik karakter sayısı (3+4+4+4).
    /// </summary>
    public const int MaxCharacters = 15;

    /// <summary>
    /// İlk gruptaki karakter sayısı (ilk grup '-' ile ayrılmaz, kod 'NOC-' gibi başlar).
    /// </summary>
    public const int FirstGroupSize = 3;

    /// <summary>
    /// İlk gruptan sonraki her gruptaki karakter sayısı (gruplar '-' ile ayrılır).
    /// </summary>
    public const int GroupSize = 4;

    /// <summary>
    /// Yalnızca ASCII harf (A-Z) ve rakam (0-9) içeren karakterler.
    /// Türkçe karakterler (Ç, Ğ, İ, Ö, Ş, Ü vb.) dahil edilmez.
    /// </summary>
    private static bool IsAllowedAsciiAlphanumeric(char c)
        => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

    /// <summary>
    /// Kanonik form regex'i: '-' ile ayrılmış büyük harf/rakam grupları,
    /// en fazla 5 grup (gruplar arasında tek '-', başta/sonda tire yok).
    /// Örn: NOC-G8K2-XW9P-7L4Q, ABCD, PROMO-EXAMPLE-7D
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex CanonicalRegex = new(
        @"^[A-Z0-9]+(?:-[A-Z0-9]+){0,4}$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Kanonik yapı kuralları: büyük harf/rakam grupları tek '-' ile ayrılmış,
    /// en fazla 5 grup ve toplam en fazla 15 alfanümerik karakter.
    /// </summary>
    private static bool MatchesCanonicalStructure(string value)
        => CanonicalRegex.IsMatch(value)
           && value.Count(c => c != '-') <= MaxCharacters;

    /// <summary>
    /// Verilen değerin kanonik biçimde (büyük harf, tireli gruplar) geçerli
    /// bir kod olup olmadığını doğrular.
    /// </summary>
    /// <param name="value">Kontrol edilecek kod</param>
    /// <returns>Format kurallarına uyuyorsa true</returns>
    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && MatchesCanonicalStructure(value.Trim());

    /// <summary>
    /// Bir kodun kanonik biçimde olup olmadığını doğrular (birebir aynı).
    /// </summary>
    /// <param name="value">Kontrol edilecek kod</param>
    /// <returns>Tam kanonik biçimdeyse true</returns>
    public static bool IsCanonical(string? value)
        => !string.IsNullOrWhiteSpace(value) && MatchesCanonicalStructure(value);

    /// <summary>
    /// Ham girişi kanonik biçime dönüştürür: yalnızca ASCII alfanümerik
    /// karakterleri alır, büyük harfe çevirir, 15 karaktere sınırlar ve
    /// 3-4-4-4 şeklinde (ilk grup 3, sonraki gruplar 4'er) '-' ekler.
    /// </summary>
    /// <param name="rawInput">Ham kod girişi (boşluk, tire, Türkçe karakter içerebilir)</param>
    /// <returns>Büyük harfli, tireli biçimde kod</returns>
    public static string Normalize(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return string.Empty;

        // Sadece ASCII alfanümerik karakterleri al, büyük harfe çevir
        var alnum = new string(rawInput.Where(IsAllowedAsciiAlphanumeric).ToArray())
            .ToUpperInvariant();

        // Maksimum 15 karakter
        if (alnum.Length > MaxCharacters)
            alnum = alnum[..MaxCharacters];

        return FormatWithDashes(alnum);
    }

    /// <summary>
    /// Alfanümerik diziyi 3-4-4-4 şeklinde biçimlendirir: ilk grup 3 karakter,
    /// sonraki gruplar 4'er karakter, araya '-' eklenir.
    /// </summary>
    /// <param name="alnum">Yalnızca alfanümerik karakterler içeren dize</param>
    /// <returns>Gruplu biçimlendirilmiş kod</returns>
    public static string FormatWithDashes(string alnum)
    {
        if (string.IsNullOrEmpty(alnum))
            return string.Empty;

        var parts = new List<string>();

        // İlk grup 3 karakter (NOC gibi), sonraki gruplar 4'er karakter
        var firstLen = Math.Min(FirstGroupSize, alnum.Length);
        parts.Add(alnum.Substring(0, firstLen));
        for (int i = firstLen; i < alnum.Length; i += GroupSize)
        {
            var len = Math.Min(GroupSize, alnum.Length - i);
            parts.Add(alnum.Substring(i, len));
        }

        return string.Join("-", parts);
    }

    /// <summary>
    /// Ham (tiresiz) karakter sayısına karşılık gelen biçimlendirilmiş dizedeki
    /// caret pozisyonunu hesaplar. Caret koruması için kullanılır: Kullanıcı
    /// yazarken '-' otomatik eklendiğinde caret'in anlamsız bir konuma
    /// zıplamasını engeller.
    /// </summary>
    /// <param name="alnumCountBeforeCaret">Caret öncesindeki alfanümerik karakter sayısı</param>
    /// <param name="formattedLength">Biçimlendirilmiş kod uzunluğu</param>
    /// <returns>Caret'in yerleştirilmesi gereken pozisyon</returns>
    public static int CalculateCaretPosition(int alnumCountBeforeCaret, int formattedLength)
    {
        var clamped = Math.Min(alnumCountBeforeCaret, MaxCharacters);
        if (clamped == 0) return 0;
        // İlk grup 3 karakter, sonraki gruplar 4'er: tireler 3., 7. ve 11.
        // karakterden sonra eklenir.
        // Formül: count + (count>3) + (count>7) + (count>11) → 3→3, 4→5, 8→10,
        // 12→15, 15→18
        var dashesBefore = (clamped > FirstGroupSize ? 1 : 0)
                         + (clamped > FirstGroupSize + GroupSize ? 1 : 0)
                         + (clamped > FirstGroupSize + 2 * GroupSize ? 1 : 0);
        return Math.Min(clamped + dashesBefore, formattedLength);
    }
}
