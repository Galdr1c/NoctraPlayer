using System;
using System.IO;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Noctra.Android.DependencyInjection;
using Noctra.Mobile;

namespace Noctra.Android;

[Application]
public class Application : AvaloniaAndroidApplication<App>
{
    private readonly object _servicesGate = new();
    private IServiceProvider? _services;

    protected Application(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public IServiceProvider Services
    {
        get
        {
            lock (_servicesGate)
            {
                return _services ??=
                    (ApplicationContext ?? this).CreateNoctraAndroidServiceProvider();
            }
        }
    }

    public override void OnCreate()
    {
#if DEBUG
        // Masaüstünde .env çalışma dizininden runtime'da okunur; Android'de
        // dosya sistemi olmadığından .env Debug APK'ya asset olarak paketlenir
        // (bkz. Noctra.Android.csproj → AndroidAsset) ve burada süreç ortamına
        // aktarılır. LicenseService/HttpBillingVerificationClient DEBUG dalında
        // ortam değişkenini okuduğu için promo/verify uç noktaları Debug APK'da
        // da çalışır. Release'de URL'ler build-time metadata'dan gelir; bu yol
        // yalnızca geliştirme içindir.
        LoadDebugEndpointEnvironment();
#endif
        App.ServiceProviderFactory ??= () => Services;
        base.OnCreate();
    }

#if DEBUG
    /// <summary>
    /// APK'ya paketlenen .env asset'inden yalnızca NOCTRA_* endpoint
    /// değişkenlerini süreç ortamına yükler. .env'deki diğer gizli değerler
    /// (TMDB, Upstash, DEV_PASSWORD) işlem ortamına ALINMAZ — yalnızca promo
    /// ve billing uç nokta adresleri paylaşılır. Zaten ayarlanmış değişkenler
    /// üzerine yazılmaz (EnvFileLoader ile aynı kural).
    /// </summary>
    private void LoadDebugEndpointEnvironment()
    {
        try
        {
            using var stream = Assets.Open("env/noctra-dev.env");
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = trimmed.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, separator).Trim();
                if (!IsSupportedEndpointKey(key))
                {
                    continue;
                }

                var value = trimmed.Substring(separator + 1).Trim();
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
        catch (Exception ex)
        {
            // Asset yoksa (ör. .env paketlenmemiş bir build) endpoint değişkenleri
            // ayarlanmaz; promo/verify hizmeti o zaman Debug APK'da da kullanılamaz
            // kalır. Hata loglanır, başlatma asla engellenmez.
            System.Diagnostics.Debug.WriteLine($"[Noctra] Debug .env asset yüklenemedi: {ex.Message}");
        }
    }

    // DİKKAT: Yeni bir NOCTRA_* endpoint değişkeni eklendiğinde onu hem buraya
    // hem de Noctra.Core/Noctra.Core.csproj içindeki ilgili Load*FromDotEnv
    // target'ına kaydetmek gerekir; atlarsan hata sessiz olur (uç nokta yalnızca
    // "kullanılamıyor" görünür).
    private static bool IsSupportedEndpointKey(string key) => key switch
    {
        "NOCTRA_PROMO_CODES_URL" or
        "NOCTRA_BILLING_VERIFY_URL" or
        "NOCTRA_BILLING_API_KEY" or
        "NOCTRA_ADVERTISING_CONFIG_URL" => true,
        _ => false
    };
#endif

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
