namespace Noctra.Core.Services;

/// <summary>
/// Promosyon kodu biçimlendirme ve doğrulama servisi.
/// Hem mobil hem masaüstü platformlarda ortak kullanılır.
///
/// Kod formatı: XXXX-XXXX-XXXX-XXXX-XXXX (yalnızca ASCII alfanümerik,
/// büyük harf, her 4 karakterde bir '-', maksimum 20 karakter).
/// </summary>
public static class PromoCodeFormatter
{
    /// <summary>
    /// Bir kodda bulunabilecek maksimum alfanümerik karakter sayısı.
    /// </summary>
    public const int MaxCharacters = 20;

    /// <summary>
    /// Her gruptaki karakter sayısı (gruplar '-' ile ayrılır).
    /// </summary>
    public const int GroupSize = 4;

    /// <summary>
    /// Yalnızca ASCII harf (A-Z) ve rakam (0-9) içeren karakterler.
    /// Türkçe karakterler (Ç, Ğ, İ, Ö, Ş, Ü vb.) dahil edilmez.
    /// </summary>
    private static bool IsAllowedAsciiAlphanumeric(char c)
        => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

    /// <summary>
    /// Kanonik form regex'i: 1-4 karakterden oluşan en fazla 5 grup, '-' ile ayrılmış.
    /// Örn: ABCD, ABCD-EFGH, ABCD-EFGH-IJKL-MNOP-QRST
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex CanonicalRegex = new(
        @"^[A-Z0-9]{1,4}(?:-[A-Z0-9]{1,4}){0,4}$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Verilen değerin kanonik biçimde (büyük harf, tireli gruplar) geçerli
    /// bir kod olup olmadığını doğrular.
    /// </summary>
    /// <param name="value">Kontrol edilecek kod</param>
    /// <returns>Format kurallarına uyuyorsa true</returns>
    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && CanonicalRegex.IsMatch(value.Trim());

    /// <summary>
    /// Bir kodun kanonik biçimde olup olmadığını doğrular (birebir aynı).
    /// </summary>
    /// <param name="value">Kontrol edilecek kod</param>
    /// <returns>Tam kanonik biçimdeyse true</returns>
    public static bool IsCanonical(string? value)
        => !string.IsNullOrWhiteSpace(value) && CanonicalRegex.IsMatch(value);

    /// <summary>
    /// Ham girişi kanonik biçime dönüştürür: yalnızca ASCII alfanümerik
    /// karakterleri alır, büyük harfe çevirir, 20 karaktere sınırlar ve
    /// her 4 karakterde bir '-' ekler.
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

        // Maksimum 20 karakter
        if (alnum.Length > MaxCharacters)
            alnum = alnum[..MaxCharacters];

        return FormatWithDashes(alnum);
    }

    /// <summary>
    /// Alfanümerik diziyi her 4 karakterde bir '-' ekleyerek biçimlendirir.
    /// </summary>
    /// <param name="alnum">Yalnızca alfanümerik karakterler içeren dize</param>
    /// <returns>Gruplu biçimlendirilmiş kod</returns>
    public static string FormatWithDashes(string alnum)
    {
        if (string.IsNullOrEmpty(alnum))
            return string.Empty;

        var parts = new List<string>();
        for (int i = 0; i < alnum.Length; i += GroupSize)
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
        // Her 4 karaktere bir '-' eklenir (ilk grup hariç)
        // Formül: count + (count-1)/4 → 4→4, 5→5, 8→9, 12→14, 16→19, 20→24
        return Math.Min(clamped + (clamped - 1) / GroupSize, formattedLength);
    }
}
