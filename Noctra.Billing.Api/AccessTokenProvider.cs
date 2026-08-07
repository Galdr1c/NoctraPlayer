using Google.Apis.Auth.OAuth2;

namespace Noctra.Billing.Api;

/// <summary>
/// Google Play Developer API için erişim token'ı sağlayıcısı (test için fake'lenir).
/// </summary>
public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Application Default Credentials (ADC) üzerinden token alır.
///
/// Cloud Run'da: service identity (Cloud Run'a bağlı service account) metadata
/// sunucusundan otomatik token verir — private key yok, Secret Manager yok,
/// manuel RS256 JWT assertion yok. Yerel geliştirmede gcloud ADC kullanılır.
/// Google, Play API erişimi için bu service account'a Play Console'da gerekli
/// izinlerin verilmesini önerir.
/// </summary>
public sealed class GoogleAdcTokenProvider : IAccessTokenProvider
{
    private const string AndroidPublisherScope = "https://www.googleapis.com/auth/androidpublisher";

    private readonly GoogleCredential _scoped;

    public GoogleAdcTokenProvider()
    {
        // Metadata sunucusundan (Cloud Run) veya GOOGLE_APPLICATION_CREDENTIALS'ten.
        _scoped = GoogleCredential
            .GetApplicationDefault()
            .CreateScoped(AndroidPublisherScope);
    }

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        // GetAccessTokenForRequestAsync, ICredential üzerinde tanımlıdır
        // (GoogleCredential bunu uygular); token'ı Google dahili olarak cache'ler.
        ((Google.Apis.Auth.OAuth2.ICredential)_scoped)
            .GetAccessTokenForRequestAsync(null, cancellationToken);
}
