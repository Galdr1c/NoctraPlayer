using System.Net;

namespace Noctra.Services;

public static class UserFriendlyErrorMessage
{
    private const string DefaultMessage = "Bilinmeyen bir hata olustu. Lutfen tekrar deneyin.";

    public static string WithPrefix(string prefix, Exception? ex, string? fallback = null)
        => WithPrefix(prefix, FromException(ex, fallback));

    public static string WithPrefix(string prefix, string? message, string? fallback = null)
    {
        var resolved = string.IsNullOrWhiteSpace(message)
            ? (fallback ?? DefaultMessage)
            : message.Trim();

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return resolved;
        }

        return $"{prefix}: {resolved}";
    }

    public static string FromException(Exception? ex, string? fallback = null)
    {
        var defaultMessage = fallback ?? DefaultMessage;
        if (ex == null)
        {
            return defaultMessage;
        }

        var baseException = ex is AggregateException aggregate
            ? aggregate.GetBaseException()
            : ex.GetBaseException();

        if (baseException is TimeoutException or TaskCanceledException)
        {
            return "Sunucu zaman aşımına uğradı veya yanıt vermiyor. Bağlantı adresini kontrol edin.";
        }

        if (baseException is HttpRequestException http)
        {
            if (http.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return "Kimlik dogrulama hatasi. Bilgilerinizi kontrol edip tekrar deneyin.";
            }

            if (http.StatusCode == HttpStatusCode.NotFound)
            {
                return "Icerik bulunamadi. Kaynak guncel olmayabilir.";
            }

            if (http.StatusCode is HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.InternalServerError)
            {
                return "Sunucuya su anda ulasilamiyor. Biraz sonra tekrar deneyin.";
            }

            return "Ag hatasi olustu. Baglantinizi kontrol edip tekrar deneyin.";
        }

        if (baseException is UnauthorizedAccessException)
        {
            return "Erisim izni hatasi. Dosya izinlerini kontrol edip tekrar deneyin.";
        }

        if (baseException is InvalidDataException)
        {
            return "Dosya dogrulamasi basarisiz. Icerigi yeniden indirip tekrar deneyin.";
        }

        if (baseException is IOException ioEx)
        {
            return FromText(ioEx.Message, "Dosya islemi sirasinda hata olustu. Disk alanini ve dosya erisimini kontrol edin.");
        }

        return FromText(baseException.Message, defaultMessage);
    }

    public static string FromText(string? rawMessage, string? fallback = null)
    {
        var defaultMessage = fallback ?? DefaultMessage;
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return defaultMessage;
        }

        var text = rawMessage.Trim();
        var normalized = text.ToLowerInvariant();

        if (ContainsAny(normalized,
                "response ended prematurely",
                "response ended",
                "unexpected end",
                "incomplete",
                "end of stream",
                "sunucudan yanit alinamadi",
                "baglanti kesildi"))
        {
            return "Ag baglantisi kesildi. Lutfen tekrar deneyin.";
        }

        if (ContainsAny(normalized,
                "timeout",
                "timed out",
                "zaman asimi",
                "taskcanceledexception"))
        {
            return "Ag zaman asimina ugradi. Baglantinizi kontrol edip tekrar deneyin.";
        }

        if (ContainsAny(normalized,
                "unauthorized",
                "forbidden",
                "401",
                "403",
                "kimlik dogrulama basarisiz",
                "token yenilenemedi"))
        {
            return "Kimlik dogrulama hatasi. Bilgilerinizi kontrol edip tekrar deneyin.";
        }

        if (ContainsAny(normalized, "404", "not found", "bulunamadi"))
        {
            return "Icerik bulunamadi. Kaynak guncel olmayabilir.";
        }

        if (ContainsAny(normalized, "500", "502", "503", "504", "server error", "sunucu"))
        {
            return "Sunucu hatasi olustu. Biraz sonra tekrar deneyin.";
        }

        if (ContainsAny(normalized,
                "dosya dogrulamasi basarisiz",
                "bozuk indirme dosyasi",
                "gecersiz dosya basligi",
                "desteklenmeyen dosya formati",
                "sifreli veri yok"))
        {
            return "Dosya dogrulamasi basarisiz. Icerigi yeniden indirip tekrar deneyin.";
        }

        if (ContainsAny(normalized,
                "yeterli alan",
                "insufficient disk",
                "not enough space",
                "there is not enough space"))
        {
            return "Yetersiz depolama alani. Lutfen disk alanini kontrol edip tekrar deneyin.";
        }

        if (ContainsAny(normalized,
                "process cannot access the file",
                "file is being used",
                "dosya kullanimda"))
        {
            return "Dosya baska bir islem tarafindan kullaniliyor. Biraz sonra tekrar deneyin.";
        }

        if (ContainsAny(normalized, "ag baglantisi bulunamadi", "network"))
        {
            return "Ag hatasi olustu. Baglantinizi kontrol edip tekrar deneyin.";
        }

        if (ContainsAny(normalized, "0 program", "0 programs", "program bulunamadi"))
        {
            return "EPG kaynaginda kanallarinizla eslesen program bulunamadi.";
        }

        return defaultMessage;
    }

    private static bool ContainsAny(string source, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (source.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
