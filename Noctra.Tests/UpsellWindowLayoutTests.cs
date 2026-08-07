using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Noctra.Tests;

public class UpsellWindowLayoutTests
{
    private static readonly string[] SupportedTranslations =
    {
        "tr-TR.json",
        "en-US.json",
        "de-DE.json",
        "es-ES.json",
        "fr-FR.json"
    };

    [Fact]
    public void UpsellWindow_UsesCompactThemeAwareLayout()
    {
        var document = LoadProjectXaml("Noctra.Avalonia", "Views", "UpsellWindow.axaml");
        var window = document.Root!;
        var source = File.ReadAllText(FindProjectFile("Noctra.Avalonia", "Views", "UpsellWindow.axaml"));

        Assert.Equal("520", (string?)window.Attribute("Height"));
        Assert.Equal("760", (string?)window.Attribute("Width"));
        Assert.DoesNotMatch(new Regex(@"#[0-9A-Fa-f]{6,8}\b", RegexOptions.CultureInvariant), source);

        var rootBorder = window.Elements().Single(element => element.Name.LocalName == "Border");
        Assert.Equal("{DynamicResource BackgroundGradientBrush}", (string?)rootBorder.Attribute("Background"));
        Assert.Equal("{DynamicResource PremiumBadgeBrush}", (string?)rootBorder.Attribute("BorderBrush"));
    }

    [Fact]
    public void UpsellWindow_ShowsBuyThenContinueFreeWithoutPrice()
    {
        var document = LoadProjectXaml("Noctra.Avalonia", "Views", "UpsellWindow.axaml");
        var actionPanel = document.Descendants()
            .Single(element => element.Name.LocalName == "StackPanel"
                               && element.Elements().Count(child => child.Name.LocalName == "Button") == 2);
        var buttons = actionPanel.Elements()
            .Where(element => element.Name.LocalName == "Button")
            .ToList();

        Assert.Equal("Buy_Click", (string?)buttons[0].Attribute("Click"));
        Assert.Equal("ContinueFree_Click", (string?)buttons[1].Attribute("Click"));
        Assert.Contains("Upsell.Action.Dismiss", (string?)buttons[1].Attribute("Content"));

        var source = File.ReadAllText(FindProjectFile("Noctra.Avalonia", "Views", "UpsellWindow.axaml"));
        Assert.DoesNotContain("Upsell.Action.Buy.Price", source);
    }

    [Fact]
    public void UpsellTranslations_DescribeCurrentPremiumFeatures()
    {
        foreach (var translationFile in SupportedTranslations)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(
                FindProjectFile("Noctra.Core", "Localization", "Translations", translationFile)));
            var root = document.RootElement;

            Assert.False(root.TryGetProperty("Upsell.Action.Buy.Price", out _));
            AssertFeature(root, "Upsell.Premium.Feature1", "12");
            AssertFeature(root, "Upsell.Premium.Feature2", "10");
            AssertMeaningfulFeature(root, "Upsell.Premium.Feature3");
            AssertMeaningfulFeature(root, "Upsell.Premium.Feature4");
            AssertMeaningfulFeature(root, "Upsell.Premium.Feature5");
            AssertMeaningfulFeature(root, "Upsell.Premium.Feature6");
            AssertMeaningfulFeature(root, "Upsell.Premium.Feature7");

            var dismissText = root.GetProperty("Upsell.Action.Dismiss").GetString();
            Assert.False(string.IsNullOrWhiteSpace(dismissText));
            Assert.DoesNotContain("thanks", dismissText!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("teşekkür", dismissText!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ActiveSource_DoesNotContainProductPriceOrPriceApi()
    {
        var root = FindRepositoryRoot();
        var activeFiles = Directory.EnumerateFiles(Path.Combine(root, "Noctra.Core"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Noctra.Avalonia"), "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && Path.GetExtension(path) is ".cs" or ".axaml" or ".json")
            .ToList();

        var source = string.Join(Environment.NewLine, activeFiles.Select(File.ReadAllText));

        Assert.DoesNotMatch(new Regex(@"499[.,]\d{1,2}|14[.,]99", RegexOptions.CultureInvariant), source);
        Assert.DoesNotContain("GetPriceText", source);
        Assert.DoesNotContain("Upsell.Action.Buy.Price", source);
    }

    [Fact]
    public void MobileUpsell_MissingPlan_DoesNotFallBackToAnotherPlan()
    {
        // Regresyon koruması: Lifetime kartına basılıp ürün Play'den dönmezse
        // genel StartPurchaseFlowAsync'e düşüp aylık ödeme ekranı açılmamalı.
        // Eksik plan → "plan kullanılamıyor" gösterilir; StartPurchaseFlowAsync
        // yalnızca mağaza desteklenmediğinde (URI akışı) çağrılır ve tek yerde
        // bulunur.
        var source = File.ReadAllText(FindProjectFile("Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));

        // URI fallback'i korunuyor (mağaza desteklenmeyen konak), ama yalnızca bir kez
        // (açılı parantezli desen yorumlardaki sözü saymaz — yalnızca gerçek çağrıyı).
        Assert.Equal(1, CountOccurrences(source, "StartPurchaseFlowAsync("));

        // Eksik-plan guard'ı StartPurchaseFlowAsync dalından SONRA gelir ve
        // LaunchPurchaseAsync'ten ÖNCE çalışır — yani eksik plan asla farklı
        // bir planın akışına düşmez.
        var unsupportedBranch = source.IndexOf(
            "if (store is not { IsSupported: true })", StringComparison.Ordinal);
        var nullGuard = source.IndexOf("if (product is null)", StringComparison.Ordinal);
        var launch = source.IndexOf("LaunchPurchaseAsync(product)", StringComparison.Ordinal);
        // Plan-kullanılamıyor mesajı dosyada pricing hata yolunda da geçer
        // (Upsell.Plan.Unavailable); guard içindeki spesifik mesaj nullGuard'dan
        // SONRA aranır.
        var unavailableKind = source.IndexOf("Upsell.Plan.UnavailableKind", nullGuard, StringComparison.Ordinal);
        var guardReturn = source.IndexOf("return;", nullGuard, StringComparison.Ordinal);

        Assert.True(unsupportedBranch >= 0, "Store-unsupported branch must exist.");
        Assert.True(nullGuard > unsupportedBranch, "Missing-plan guard must come after the store-unsupported branch.");
        Assert.True(unavailableKind > nullGuard, "Missing-plan guard must show the plan-unavailable message.");
        // Guard erken döner: launch'a asla düşmez (fall-through yok).
        Assert.True(guardReturn > nullGuard && guardReturn < launch,
            "Missing-plan guard must return before the product launch.");
        Assert.True(launch > nullGuard, "Product launch must come after the missing-plan guard.");
    }

    [Fact]
    public void MobileUpsell_LifetimeMonthlyCoexistence_GuardsPresent()
    {
        // Kalıcı paket ile aylık aboneliğin aynı kullanıcıda birlikte
        // var olabilmesi (aylık abonelik Play'de otomatik iptal olmaz):
        //  (1) Kalıcı paket sahibine aylık satın alma sunulmaz.
        //  (2) Aktif aylık abonelik varken kalıcı paket seçimi açık onay ister.
        //  (3) Kalıcı paket tamamlanınca iptal hatırlatması + abonelik yönetimi
        //      eylemi gösterilir; sheet hatırlatmayla açık kalır.
        var source = File.ReadAllText(FindProjectFile("Noctra.Mobile", "Views", "MobileUpsellView.axaml.cs"));
        var xaml = File.ReadAllText(FindProjectFile("Noctra.Mobile", "Views", "MobileUpsellView.axaml"));

        // (1) Lifetime sahibi: plan kartları gizlenir (aylık kart asla sunulmaz).
        Assert.Contains("UpdatePlanCardVisibility", source);
        Assert.Contains("HasLifetimePremium", source);

        // (2) Aylık aktifken kalıcı paket satın alma onay ister ve onay akışı
        //     aktif abonelik bayrağını okur.
        Assert.Contains("ConfirmLifetimeWhileMonthlyActiveAsync", source);
        Assert.Contains("HasActiveStoreSubscription", source);
        Assert.Contains("Upsell.Plan.Lifetime.ConfirmMessage", source);

        // (3) Tamamlanma sonrası: iptal hatırlatması + Google Play abonelik
        //     yönetimi eylemi (aylık plan buradan iptal edilir). Hatırlatma
        //     dalı TryClose'tan ÖNCE gelir — sheet hatırlatmayı göstermek
        //     yerine yanlışlıkla kapanamaz.
        var reminder = source.IndexOf("Upsell.Plan.Lifetime.CancelMonthlyReminder", StringComparison.Ordinal);
        var close = source.IndexOf("TryClose()", reminder, StringComparison.Ordinal);
        Assert.True(reminder >= 0 && close > reminder,
            "Cancel-monthly reminder must be shown before the sheet closes.");
        Assert.Contains("https://play.google.com/store/account/subscriptions", source);
        Assert.Contains("ManageSubscriptionButton", source);
        Assert.Contains("ManageSubscriptionButton", xaml);
        Assert.Contains("Upsell.Action.ManageSubscription", xaml);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void AssertFeature(JsonElement root, string key, string expectedFragment)
    {
        var value = root.GetProperty(key).GetString();
        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.Contains(expectedFragment, value!);
    }

    private static void AssertMeaningfulFeature(JsonElement root, string key)
    {
        var value = root.GetProperty(key).GetString();
        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.DoesNotContain("more", value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fazlas", value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mehr", value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plus", value!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("más", value!, StringComparison.OrdinalIgnoreCase);
    }

    private static XDocument LoadProjectXaml(params string[] relativeParts) =>
        XDocument.Load(FindProjectFile(relativeParts));

    private static string FindProjectFile(params string[] relativeParts)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(new[] { root }.Concat(relativeParts).ToArray());
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"Could not find project file: {Path.Combine(relativeParts)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
