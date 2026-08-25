using System.Text.Json;

namespace Noctra.Tests;

public class LegalDocumentTranslationTests
{
    private static readonly IReadOnlyDictionary<string, LegalExpectations> Expectations =
        new Dictionary<string, LegalExpectations>
        {
            ["en-US.json"] = new(
                "Last updated: August 25, 2026",
                ["Local Storage", "Network Connections and Third-Party Services", "Data Retention and Deletion", "Data Sharing", "Security"],
                ["No Content or Subscription Is Provided", "Downloads and Offline Use", "Microsoft Store Purchases", "Limitation of Liability"],
                "Children and Profiles", "Profiles, PINs, and Child Features"),
            ["tr-TR.json"] = new(
                "Son güncelleme: 25 Ağustos 2026",
                ["Yerel Depolama", "Ağ Bağlantıları ve Üçüncü Taraf Hizmetleri", "Verilerin Saklanması ve Silinmesi", "Veri Paylaşımı", "Güvenlik"],
                ["İçerik veya Abonelik Sağlanmaz", "İndirmeler ve Çevrimdışı Kullanım", "Microsoft Store Satın Alımları", "Sorumluluğun Sınırlandırılması"],
                "Çocuklar ve Profiller", "Profiller, PIN'ler ve Çocuk Özellikleri"),
            ["de-DE.json"] = new(
                "Letzte Aktualisierung: 25. August 2026",
                ["Lokale Speicherung", "Netzwerkverbindungen und Dienste Dritter", "Datenspeicherung und Löschung", "Datenweitergabe", "Sicherheit"],
                ["Keine Inhalte oder Abonnements", "Downloads und Offline-Nutzung", "Microsoft Store-Käufe", "Haftungsbeschränkung"],
                "Kinder und Profile", "Profile, PINs und Kinderfunktionen"),
            ["es-ES.json"] = new(
                "Última actualización: 25 de agosto de 2026",
                ["Almacenamiento local", "Conexiones de red y servicios de terceros", "Conservación y eliminación de datos", "Compartición de datos", "Seguridad"],
                ["No se proporciona contenido ni suscripciones", "Descargas y uso sin conexión", "Compras en Microsoft Store", "Limitación de responsabilidad"],
                "Niños y perfiles", "Perfiles, PIN y funciones infantiles"),
            ["fr-FR.json"] = new(
                "Dernière mise à jour : 25 août 2026",
                ["Stockage local", "Connexions réseau et services tiers", "Conservation et suppression des données", "Partage des données", "Sécurité"],
                ["Aucun contenu ni abonnement fourni", "Téléchargements et utilisation hors ligne", "Achats Microsoft Store", "Limitation de responsabilité"],
                "Enfants et profils", "Profils, PIN et fonctions enfants")
        };

    [Fact]
    public void LegalDocumentTranslations_MatchCurrentPolicyRevisionAndCoverage()
    {
        foreach (var (fileName, expected) in Expectations)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(FindTranslation(fileName)));
            var root = document.RootElement;
            var privacy = root.GetProperty("GlobalSettings.Privacy.Message.Current").GetString()!;
            var terms = root.GetProperty("GlobalSettings.Terms.Message.Current").GetString()!;

            Assert.Contains(expected.Revision, privacy, StringComparison.Ordinal);
            Assert.Contains(expected.Revision, terms, StringComparison.Ordinal);

            foreach (var heading in expected.PrivacyHeadings)
            {
                Assert.Contains(heading, privacy, StringComparison.Ordinal);
            }

            foreach (var heading in expected.TermsHeadings)
            {
                Assert.Contains(heading, terms, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("May 22, 2026", privacy, StringComparison.Ordinal);
            Assert.DoesNotContain("May 22, 2026", terms, StringComparison.Ordinal);
            Assert.DoesNotContain(expected.ForbiddenPrivacyHeading, privacy, StringComparison.Ordinal);
            Assert.DoesNotContain(expected.ForbiddenTermsHeading, terms, StringComparison.Ordinal);
        }
    }

    private static readonly IReadOnlyDictionary<string, MobileLegalExpectations> MobileExpectations =
        new Dictionary<string, MobileLegalExpectations>
        {
            ["en-US.json"] = new(
                "Last updated: August 25, 2026",
                ["Advertising in the Free Version", "Consent", "Provider Connections", "Metadata (TMDB)", "Purchases, Subscriptions, and Backend Verification", "Data Sharing"],
                ["Free and Premium Features", "Advertising Consent", "Google Play Purchases and Subscriptions"],
                ["watch history", "purchase token"],
                "Children and Profiles", "Profiles, PINs, and Child Features"),
            ["tr-TR.json"] = new(
                "Son güncelleme: 25 Ağustos 2026",
                ["Ücretsiz Sürümde Reklamlar", "Onam", "Sağlayıcı Bağlantıları", "Meta Veri (TMDB)", "Satın Alımlar, Abonelikler ve Backend Doğrulaması", "Veri Paylaşımı"],
                ["Free ve Premium Özellikler", "Reklam Onamı", "Google Play Satın Alımları ve Abonelikler"],
                ["izleme geçmişi", "satın alma jetonu"],
                "Çocuklar ve Profiller", "Profiller, PIN'ler ve Çocuk Özellikleri"),
            ["de-DE.json"] = new(
                "Letzte Aktualisierung: 25. August 2026",
                ["Werbung in der kostenlosen Version", "Einwilligung", "Anbieterverbindungen", "Metadaten (TMDB)", "Käufe, Abos und Backend-Verifizierung", "Datenweitergabe"],
                ["Free- und Premium-Funktionen", "Werbeeinwilligung", "Google Play-Käufe und Abos"],
                ["Wiedergabeverlauf", "Kauf-Token"],
                "Kinder und Profile", "Profile, PINs und Kinderfunktionen"),
            ["fr-FR.json"] = new(
                "Dernière mise à jour : 25 août 2026",
                ["Publicité dans la version gratuite", "Consentement", "Connexions aux fournisseurs", "Métadonnées (TMDB)", "Achats, abonnements et vérification backend", "Partage des données"],
                ["Fonctions Free et Premium", "Consentement publicitaire", "Achats et abonnements Google Play"],
                ["historique de visionnage", "jeton d'achat"],
                "Enfants et profils", "Profils, PIN et fonctions enfants"),
            ["es-ES.json"] = new(
                "Última actualización: 25 de agosto de 2026",
                ["Publicidad en la versión gratuita", "Consentimiento", "Conexiones del proveedor", "Metadatos (TMDB)", "Compras, suscripciones y verificación backend", "Compartición de datos"],
                ["Funciones Free y Premium", "Consentimiento publicitario", "Compras y suscripciones Google Play"],
                ["historial de visualización", "token de compra"],
                "Niños y perfiles", "Perfiles, PIN y funciones infantiles")
        };

    [Fact]
    public void MobileLegalDocument_DeclaresAndroidAdBillingAndNetworkPractices()
    {
        foreach (var (fileName, expected) in MobileExpectations)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(FindTranslation(fileName)));
            var root = document.RootElement;
            var privacy = root.GetProperty("GlobalSettings.Privacy.Message.Mobile").GetString()!;
            var terms = root.GetProperty("GlobalSettings.Terms.Message.Mobile").GetString()!;

            foreach (var text in new[] { privacy, terms })
            {
                Assert.Contains(expected.Revision, text, StringComparison.Ordinal);
                Assert.Contains("kynora.studio@gmail.com", text, StringComparison.Ordinal);
                Assert.Contains("Google AdMob", text, StringComparison.Ordinal);
                Assert.Contains("Huawei Petal Ads", text, StringComparison.Ordinal);
            }

            foreach (var heading in expected.PrivacyHeadings)
            {
                Assert.Contains(heading, privacy, StringComparison.Ordinal);
            }

            foreach (var heading in expected.TermsHeadings)
            {
                Assert.Contains(heading, terms, StringComparison.Ordinal);
            }

            Assert.Contains("Google Mobile Ads SDK", privacy, StringComparison.Ordinal);
            Assert.Contains("User Messaging Platform (UMP)", privacy, StringComparison.Ordinal);
            Assert.Contains("Android Keystore", privacy, StringComparison.Ordinal);
            Assert.Contains("http://", privacy, StringComparison.Ordinal);

            Assert.Contains(expected.PrivacyMarkers[0], privacy, StringComparison.Ordinal);
            Assert.Contains(expected.PrivacyMarkers[1], privacy, StringComparison.Ordinal);
            Assert.DoesNotContain(expected.ForbiddenPrivacyHeading, privacy, StringComparison.Ordinal);
            Assert.DoesNotContain(expected.ForbiddenTermsHeading, terms, StringComparison.Ordinal);
        }
    }

    private static string FindTranslation(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solution = Path.Combine(directory.FullName, "NoctraPlayer.sln");
            if (File.Exists(solution))
            {
                return Path.Combine(directory.FullName, "Noctra.Core", "Localization", "Translations", fileName);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record LegalExpectations(
        string Revision,
        IReadOnlyList<string> PrivacyHeadings,
        IReadOnlyList<string> TermsHeadings,
        string ForbiddenPrivacyHeading,
        string ForbiddenTermsHeading);

    private sealed record MobileLegalExpectations(
        string Revision,
        IReadOnlyList<string> PrivacyHeadings,
        IReadOnlyList<string> TermsHeadings,
        IReadOnlyList<string> PrivacyMarkers,
        string ForbiddenPrivacyHeading,
        string ForbiddenTermsHeading);
}
