namespace Noctra.Core.Services;

/// <summary>
/// Stalker Portal MAC adresi biçimlendirme ve doğrulama servisi.
/// Hem mobil hem masaüstü platformlarda ortak kullanılır.
///
/// MAC formatı: 00:1A:79:XX:XX:XX (6 byte, : ile ayrılmış, büyük harf)
/// Stalker Portal'ın standart MAC prefix'i: 00:1A:79
/// </summary>
public static class StalkerMacFormatter
{
    /// <summary>
    /// Stalker Portal MAC adreslerinin standart prefix'i.
    /// </summary>
    public const string Prefix = "00:1A:79:";

    /// <summary>
    /// Geçerli Stalker MAC adresi regex'i: XX:XX:XX:XX:XX:XX
    /// Her byte iki hex karakter, beş : ile ayrılmış, toplam 17 karakter.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex MacRegex = new(
        @"^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Verilen MAC değerinin geçerli formatta olup olmadığını doğrular.
    /// </summary>
    /// <param name="value">Kontrol edilecek MAC değeri</param>
    /// <returns>XX:XX:XX:XX:XX:XX formatındaysa true</returns>
    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && MacRegex.IsMatch(value.Trim());

    /// <summary>
    /// Ham hex girişini (001A79ABCDEF veya 001a79abcdef gibi) XX:XX:XX:XX:XX:XX
    /// formatına dönüştürür. Mevcut ':' karakterlerini temizler, büyük harfe
    /// çevirir ve 12 hex karaktere kadar sınırlar.
    /// </summary>
    /// <param name="rawInput">Ham MAC girişi (hexonly veya yarı biçimlendirilmiş)</param>
    /// <returns>Büyük harfli, ':' ile biçimlendirilmiş MAC adresi</returns>
    public static string Normalize(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return string.Empty;

        // Sadece hex karakterleri al
        var hexOnly = new string(rawInput.Where(Uri.IsHexDigit).ToArray())
            .ToUpperInvariant();

        // Maksimum 12 hex karakter (6 byte)
        if (hexOnly.Length > 12)
            hexOnly = hexOnly[..12];

        return FormatMacWithColons(hexOnly);
    }

    /// <summary>
    /// Ham hex dizisini XX:XX:XX:XX:XX:XX formatına dönüştürür.
    /// Her iki karakterden sonra ':' ekler.
    /// </summary>
    /// <param name="hex">Sadece hex karakterler içeren dize (maks. 12 karakter)</param>
    /// <returns>Biçimlendirilmiş MAC adresi</returns>
    public static string FormatMacWithColons(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return string.Empty;

        var parts = new List<string>();
        for (int i = 0; i < hex.Length; i += 2)
        {
            var len = Math.Min(2, hex.Length - i);
            parts.Add(hex.Substring(i, len));
        }
        return string.Join(":", parts);
    }

    /// <summary>
    /// Biçimlendirilmiş bir MAC adresinde verilen hex-only pozisyonuna karşılık
    /// gelen formatlanmış dizedeki caret pozisyonunu hesaplar.
    /// Caret koruması için kullanılır: Kullanıcı yazarken ':' otomatik eklendiğinde
    /// caret'in anlamsız bir konuma zıplamasını engeller.
    /// </summary>
    /// <param name="hexCountBeforeCaret">Caret öncesindeki hex karakter sayısı</param>
    /// <param name="formattedLength">Biçimlendirilmiş MAC uzunluğu</param>
    /// <returns>Caret'in yerleştirilmesi gereken pozisyon</returns>
    public static int CalculateCaretPosition(int hexCountBeforeCaret, int formattedLength)
    {
        var clampedHex = Math.Min(hexCountBeforeCaret, 12);
        if (clampedHex == 0) return 0;
        // Her iki hex byte'a bir ':' eklenir (ilk byte hariç)
        // Formül: hexCount + (hexCount-1)/2 → 2→2, 4→5, 6→8, 8→11, 10→14, 12→17
        return Math.Min(clampedHex + (clampedHex - 1) / 2, formattedLength);
    }
}
