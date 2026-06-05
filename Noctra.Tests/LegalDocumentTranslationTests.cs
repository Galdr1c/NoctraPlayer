using System.Text.Json;

namespace Noctra.Tests;

public class LegalDocumentTranslationTests
{
    private static readonly IReadOnlyDictionary<string, LegalExpectations> Expectations =
        new Dictionary<string, LegalExpectations>
        {
            ["en-US.json"] = new(
                "Last updated: June 5, 2026",
                ["Local Storage", "Network Connections and Third-Party Services", "Data Retention and Deletion", "Data Sharing", "Security"],
                ["No Content or Subscription Is Provided", "Downloads and Offline Use", "Microsoft Store Purchases", "Limitation of Liability"]),
            ["tr-TR.json"] = new(
                "Son güncelleme: 5 Haziran 2026",
                ["Yerel Depolama", "Ağ Bağlantıları ve Üçüncü Taraf Hizmetleri", "Verilerin Saklanması ve Silinmesi", "Veri Paylaşımı", "Güvenlik"],
                ["İçerik veya Abonelik Sağlanmaz", "İndirmeler ve Çevrimdışı Kullanım", "Microsoft Store Satın Alımları", "Sorumluluğun Sınırlandırılması"]),
            ["de-DE.json"] = new(
                "Letzte Aktualisierung: 5. Juni 2026",
                ["Lokale Speicherung", "Netzwerkverbindungen und Dienste Dritter", "Datenspeicherung und Löschung", "Datenweitergabe", "Sicherheit"],
                ["Keine Inhalte oder Abonnements", "Downloads und Offline-Nutzung", "Microsoft Store-Käufe", "Haftungsbeschränkung"]),
            ["es-ES.json"] = new(
                "Última actualización: 5 de junio de 2026",
                ["Almacenamiento local", "Conexiones de red y servicios de terceros", "Conservación y eliminación de datos", "Compartición de datos", "Seguridad"],
                ["No se proporciona contenido ni suscripciones", "Descargas y uso sin conexión", "Compras en Microsoft Store", "Limitación de responsabilidad"]),
            ["fr-FR.json"] = new(
                "Dernière mise à jour : 5 juin 2026",
                ["Stockage local", "Connexions réseau et services tiers", "Conservation et suppression des données", "Partage des données", "Sécurité"],
                ["Aucun contenu ni abonnement fourni", "Téléchargements et utilisation hors ligne", "Achats Microsoft Store", "Limitation de responsabilité"])
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
        IReadOnlyList<string> TermsHeadings);
}
