using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Noctra.Services;

/// <summary>
/// 4 haneli sayisal PIN'lerin tahmin edilmesi kolay olup olmadigini degerlendirir.
/// PIN'ler yalnizca yerel profil kilidi oldugundan zayif PIN'ler YASAKLANMAZ —
/// yalnizca kullaniciya bildirim (konfirmasyon) tetiklenir.
/// </summary>
public static class PinWeaknessEvaluator
{
    // Kalip kurallarina girmeyen, en yaygin kullanilan PIN'ler.
    private static readonly HashSet<string> CommonPins = new()
    {
        "0852", "1004", "1010", "1025", "1357", "1478", "1590",
        "2468", "2580", "2589", "3690", "4040", "5683", "8969",
        "9413", "9515",
    };

    /// <summary>
    /// PIN zayif kabul edilir mi? Gecerli kabul edilen girdiler (4 hane, tamami
    /// rakam) icin anlamlidir; bu kosullara uymayan girdiler zayif sayilmaz.
    /// </summary>
    public static bool IsWeak(string? pin)
    {
        if (pin is null || pin.Length != 4 || !pin.All(char.IsDigit))
        {
            return false;
        }

        // 1) Tum haneler ayni: 0000, 1111, ...
        if (pin.Distinct().Count() == 1)
        {
            return true;
        }

        // 2) 4'lu ardIsik seri (artan veya azalan): 1234, 6789, 9876, 3210, ...
        if (IsSequential(pin))
        {
            return true;
        }

        // 3) Tekrar eden cift desenleri: abab (1212), aabb (1122), abba (1221)
        if (pin[0] == pin[2] && pin[1] == pin[3] ||
            pin[0] == pin[1] && pin[2] == pin[3] ||
            pin[0] == pin[3] && pin[1] == pin[2])
        {
            return true;
        }

        // 4) Yil deseni: 1900-2099 (dogum yili / guncel yil olarak cok yaygin)
        if (int.TryParse(pin, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value is >= 1900 and <= 2099)
        {
            return true;
        }

        // 5) En yaygin PIN listesi (kalip kurallarina girmeyenler)
        return CommonPins.Contains(pin);
    }

    private static bool IsSequential(string pin)
    {
        var d1 = pin[1] - pin[0];
        var d2 = pin[2] - pin[1];
        var d3 = pin[3] - pin[2];

        return d1 == 1 && d2 == 1 && d3 == 1 ||
               d1 == -1 && d2 == -1 && d3 == -1;
    }
}
