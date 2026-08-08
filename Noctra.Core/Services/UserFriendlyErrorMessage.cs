using System.Net;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public static class UserFriendlyErrorMessage
{
    private static ILocalizationService? _localizationService;

    public static void Initialize(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    private static string GetString(string key, string fallback) => _localizationService?.GetString(key) ?? fallback;

    private const string DefaultMessage = "Bilinmeyen bir hata oluştu. Lütfen tekrar deneyin.";

    public static string WithPrefix(string prefix, Exception? ex, string? fallback = null)
        => WithPrefix(prefix, FromException(ex, fallback));

    public static string WithPrefix(string prefix, string? message, string? fallback = null)
    {
        var resolved = string.IsNullOrWhiteSpace(message)
            ? (fallback ?? GetString("Error.Common.Default", DefaultMessage))
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
            return GetString("Error.Network.Timeout", "Sunucu zaman aşımına uğradı veya yanıt vermiyor. Bağlantı adresini kontrol edin.");
        }

        if (baseException is HttpRequestException http)
        {
            if (http.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return GetString("Error.Http.Auth", "Kimlik doğrulama hatası. Bilgilerinizi kontrol edip tekrar deneyin.");
            }

            if (http.StatusCode == HttpStatusCode.NotFound)
            {
                return GetString("Error.Http.NotFound", "İçerik bulunamadı. Kaynak güncel olmayabilir.");
            }

            if (http.StatusCode is HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.InternalServerError)
            {
                return GetString("Error.Http.ServerUnreachable", "Sunucuya şu anda ulaşılamıyor. Biraz sonra tekrar deneyin.");
            }

            return GetString("Error.Network.Generic", "Ağ hatası oluştu. Bağlantınızı kontrol edip tekrar deneyin.");
        }

        if (baseException is UnauthorizedAccessException)
        {
            return GetString("Error.System.Access", "Erişim izni hatası. Dosya izinlerini kontrol edip tekrar deneyin.");
        }

        if (baseException is InvalidDataException)
        {
            return GetString("Error.System.DataValidation", "Dosya doğrulama hatası. İçeriği yeniden indirip tekrar deneyin.");
        }

        if (baseException is InvalidOperationException invalidOpEx)
        {
            var message = invalidOpEx.Message;
            if (!string.IsNullOrWhiteSpace(message)
                && !message.StartsWith("Exception of type", StringComparison.OrdinalIgnoreCase)
                && !ContainsAny(message.ToLowerInvariant(),
                    "object reference",
                    "sequence contains",
                    "collection was modified",
                    "index was out of range",
                    "key not found",
                    "operation is not valid",
                    "procedure or function",
                    "database is locked",
                    "aggregation interrupted",
                    "simulated"))
            {
                return message;
            }
            return GetString("Error.System.InvalidOperation", "İşlem beklendiği gibi tamamlanamadı. Kaynak veri eksik veya hatalı olabilir.");
        }

        if (baseException is NullReferenceException || baseException.GetType().Name == "ArgumentNullException")
        {
            return GetString("Error.System.NullOrMissing", "Geçersiz veri veya eksik bilgi ile karşılaşıldı. Lütfen işlemi tekrar deneyin.");
        }

        if (baseException is FormatException)
        {
            return GetString("Error.Format.Generic", "Veri okunamadı. EPG veya kanal listenizin formatı hatalı olabilir.");
        }

        if (baseException is IOException ioEx)
        {
            return FromText(ioEx.Message, GetString("Error.System.IO", "Dosya işlemi sırasında hata oluştu. Disk alanını ve dosya erişimini kontrol edin."));
        }

        if (baseException.GetType().Name == "SocketException")
        {
            return GetString("Error.Network.Socket", "Bağlantı kurulamadı. İnternet bağlantınızı ve kaynak adresini kontrol edin.");
        }

        if (baseException.GetType().Name == "AuthenticationException" || baseException.Message.Contains("SSL") || baseException.Message.Contains("certificate"))
        {
            return GetString("Error.Security.Ssl", "Bağlantı güvenliği doğrulanamadı. Sunucunun güvenlik sertifikasını kontrol edin ve tekrar deneyin.");
        }

        return FromText(baseException.Message, GetString("Error.Generic.Unknown", "Bilinmeyen bir hata oluştu. Lütfen tekrar deneyin."));
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
                "android_getaddrinfo",
                "eai_nodata",
                "no address associated with hostname",
                "nameresolutionfailure",
                "nodename nor servname",
                "name or service not known",
                "temporary failure in name resolution",
                "host not found",
                "could not resolve host"))
        {
            return GetString("Error.Network.Dns", "Sunucu adresi çözümlenemedi. Alan adını ve internet bağlantınızı kontrol edin.");
        }

        if (ContainsAny(normalized,
                "response ended prematurely",
                "response ended",
                "unexpected end",
                "incomplete",
                "end of stream",
                "sunucudan yanit alinamadi",
                "baglanti kesildi"))
        {
            return GetString("Error.Network.Disconnected", "Ağ bağlantısı kesildi. Lütfen tekrar deneyin.");
        }

        if (ContainsAny(normalized,
                "timeout",
                "timed out",
                "zaman asimi",
                "taskcanceledexception"))
        {
            return GetString("Error.Network.Timeout", "Ağ zaman aşımına uğradı. Bağlantınızı kontrol edip tekrar deneyin.");
        }

        if (ContainsAny(normalized,
                "unauthorized",
                "forbidden",
                "401",
                "403",
                "kimlik dogrulama basarisiz",
                "token yenilenemedi"))
        {
            return GetString("Error.Http.Auth", "Kimlik doğrulama hatası. Bilgilerinizi kontrol edip tekrar deneyin.");
        }

        if (ContainsAny(normalized, "404", "not found", "bulunamadi"))
        {
            return GetString("Error.Http.NotFound", "İçerik bulunamadı. Kaynak güncel olmayabilir.");
        }

        if (ContainsAny(normalized, "500", "502", "503", "504", "server error", "sunucu"))
        {
            return GetString("Error.Http.ServerUnreachable", "Sunucuya şu anda ulaşılamıyor. Biraz sonra tekrar deneyin.");
        }

        if (ContainsAny(normalized,
                "dosya dogrulamasi basarisiz",
                "bozuk indirme dosyasi",
                "gecersiz dosya basligi",
                "desteklenmeyen dosya formati",
                "sifreli veri yok"))
        {
            return GetString("Error.System.DataValidation", "Dosya doğrulama hatası. İçeriği yeniden indirip tekrar deneyin.");
        }

        if (ContainsAny(normalized,
                "yeterli alan",
                "insufficient disk",
                "not enough space",
                "there is not enough space"))
        {
            return GetString("Error.System.StorageFull", "Yetersiz depolama alanı. Lütfen disk alanını kontrol edip tekrar deneyin.");
        }

        if (ContainsAny(normalized,
                "process cannot access the file",
                "file is being used",
                "dosya kullanimda"))
        {
            return GetString("Error.System.FileInUse", "Dosya başka bir işlem tarafından kullanılıyor. Biraz sonra tekrar deneyin.");
        }

        if (ContainsAny(normalized, "ag baglantisi bulunamadi", "network"))
        {
            return GetString("Error.Network.Generic", "Ağ hatası oluştu. Bağlantınızı kontrol edip tekrar deneyin.");
        }

        if (ContainsAny(normalized, "0 program", "0 programs", "program bulunamadi"))
        {
            return GetString("Error.Epg.NoMatch", "EPG kaynağı yüklendi ancak mevcut kanallarınızla eşleşen yayın bilgisi bulunamadı.");
        }

        if (ContainsAny(normalized,
                "xml",
                "m3u",
                "parse",
                "parsing",
                "ayristirma",
                "format exception",
                "unrecognized element",
                "unexpected token"))
        {
            return GetString("Error.Format.ConnectionCheck", "Veri formatı okunamadı. EPG veya kanal listenizin bağlantısını kontrol edin.");
        }

        if (ContainsAny(normalized,
                "cs8602",
                "nullreferenceexception",
                "invalidoperationexception",
                "object reference not set",
                "nesne basvurusu",
                "indexoutofrangeexception",
                "argumentnullexception",
                "exception of type",
                "an error occurred"))
        {
            return GetString("Error.System.Generic", "Sistemde anlık bir hata oluştu. Lütfen işlemi tekrar deneyin.");
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
