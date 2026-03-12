// =============================================================================
// Noctra Provider Test Aracı
// M3U · Xtream Codes · Stalker Portal
// Kullanım: dotnet run -- --help
// =============================================================================

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Noctra.Diagnostics;

var app = new NoctraProviderTester();
await app.RunAsync(args);

// =============================================================================
// ANA UYGULAMA
// =============================================================================

partial class NoctraProviderTester
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true, // SSL bypass for test
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static NoctraProviderTester()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        PrintBanner();

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintHelp();
            return;
        }

        // Config dosyasından toplu test
        if (args[0] == "--batch" || args[0] == "-b")
        {
            bool batchDeep = args.Contains("--deep-test") || args.Contains("-d");
        var batchArgs = args.Where(a => a != "--deep-test" && a != "-d").ToArray();
        var configFile = batchArgs.Length > 1 ? batchArgs[1] : "providers.json";
            await RunBatchTestAsync(configFile, batchDeep, args);
            return;
        }

        // Deep playback test modu
        bool deepTest = args.Contains("--deep-test") || args.Contains("-d");
        var filteredArgs = args.Where(a => a != "--deep-test" && a != "-d").ToArray();

        // Tek provider testi
        var config = ParseArgs(filteredArgs);
        if (config == null) { PrintHelp(); return; }

        var result = await TestProviderAsync(config);
        
        // Deep test isteniyorsa oynatma testi yap
        if (deepTest && result.ConnectionOk && result.TotalChannels > 0)
        {
            var deepSampling = ParseDeepTestArgs(args);
            result.DeepTestReport = await RunDeepTestAsync(result.Channels ?? new(), deepSampling);
            PrintDeepTestReport(result.DeepTestReport);
        }

        PrintDetailedReport(result);

        GenerateHtmlReport(new List<TestResult> { result }, "report.html");
        Console.WriteLine("\n📄 HTML raporu: report.html");
    }

    // =========================================================================
    // TOPLU TEST
    // =========================================================================

    async Task RunBatchTestAsync(string configFile, bool batchDeep, string[] args)
    {
        if (!File.Exists(configFile))
        {
            // Örnek config oluştur
            var sample = new BatchConfig
            {
                Providers = new List<ProviderConfig>
                {
                    new() { Name = "Örnek M3U", Type = "m3u", Url = "http://example.com/playlist.m3u" },
                    new() { Name = "Örnek Xtream", Type = "xtream", Host = "http://example.com:8080", Username = "user", Password = "pass" },
                    new() { Name = "Örnek Stalker", Type = "stalker", Host = "http://example.com:8080", MacAddress = "00:1A:79:AA:BB:CC" }
                }
            };
            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, json);
            Console.WriteLine($"⚠️  Config dosyası bulunamadı. Örnek oluşturuldu: {configFile}");
            Console.WriteLine("Doldurup tekrar çalıştırın.");
            return;
        }

        var config = JsonSerializer.Deserialize<BatchConfig>(File.ReadAllText(configFile));
        if (config?.Providers == null || config.Providers.Count == 0)
        {
            Console.WriteLine("❌ Config dosyası boş veya geçersiz.");
            return;
        }

        Console.WriteLine($"🚀 {config.Providers.Count} provider test ediliyor...\n");
        var results = new List<TestResult>();

        foreach (var provider in config.Providers)
        {
            Console.Write($"  ⏳ {provider.Name,-30}");
            var sw = Stopwatch.StartNew();
            var result = await TestProviderAsync(provider);
            sw.Stop();
            results.Add(result);

            var icon = result.ConnectionOk ? "✅" : "❌";
            Console.WriteLine($" {icon}  {result.TotalChannels,6} kanal  ({sw.ElapsedMilliseconds}ms)");

            if (!result.ConnectionOk)
                Console.WriteLine($"         └─ {result.ConnectionError}");
        }

        Console.WriteLine();
        PrintBatchSummary(results);

        if (batchDeep)
        {
            Console.WriteLine("\n🎬 Toplu Deep Test modu aktif — her provider için oynatma testi yapılıyor...");
            var deepSampling = ParseDeepTestArgs(args);
            foreach (var r in results.Where(r => r.ConnectionOk && r.TotalChannels > 0))
            {
                r.DeepTestReport = await RunDeepTestAsync(r.Channels ?? new(), deepSampling);
            }
        }

        GenerateHtmlReport(results, "batch_report.html");
        Console.WriteLine("\n📄 HTML raporu: batch_report.html");
    }

    // =========================================================================
    // PROVIDER TEST MOTORU
    // =========================================================================

    async Task<TestResult> TestProviderAsync(ProviderConfig config)
    {
        var result = new TestResult { ProviderName = config.Name ?? "Bilinmiyor", ProviderType = config.Type };
        var sw = Stopwatch.StartNew();

        try
        {
            List<ParsedChannel>? channels = config.Type.ToLower() switch
            {
                "m3u"     => await TestM3UAsync(config, result),
                "xtream"  => await TestXtreamAsync(config, result),
                "stalker" => await TestStalkerAsync(config, result),
                _         => throw new Exception($"Bilinmeyen tip: {config.Type}")
            };

            if (channels != null)
            {
                result.ConnectionOk = true;
                result.Channels = channels;
                AnalyzeChannels(channels, result);
                AnalyzeSeriesParsing(channels, result);
                AnalyzeLanguageDetection(channels, result);
            }
        }
        catch (Exception ex)
        {
            result.ConnectionOk = false;
            result.ConnectionError = ex.Message;
        }

        result.ElapsedMs = (int)sw.ElapsedMilliseconds;
        return result;
    }

    // =========================================================================
    // M3U TEST
    // =========================================================================

    async Task<List<ParsedChannel>?> TestM3UAsync(ProviderConfig config, TestResult result)
    {
        if (string.IsNullOrWhiteSpace(config.Url))
            throw new Exception("M3U için URL zorunludur.");

        result.EndpointUrl = config.Url;

        // Bağlantı testi
        var sw = Stopwatch.StartNew();
        var head = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, config.Url));
        result.HttpStatusCode = (int)head.StatusCode;
        result.ConnectionLatencyMs = (int)sw.ElapsedMilliseconds;

        if (!head.IsSuccessStatusCode)
        {
            // HEAD başarısızsa GET dene (bazı serverlar HEAD'i desteklemez)
            var getResponse = await Http.GetAsync(config.Url);
            result.HttpStatusCode = (int)getResponse.StatusCode;
            if (!getResponse.IsSuccessStatusCode)
                throw new Exception($"HTTP {result.HttpStatusCode} — sunucu erişilemiyor");
        }

        result.ConnectionOk = true;
        Console.WriteLine($"\n  → Bağlantı OK ({result.ConnectionLatencyMs}ms), içerik indiriliyor...");

        var content = await Http.GetStringAsync(config.Url);
        result.RawContentSizeKb = content.Length / 1024;

        Console.WriteLine($"  → {result.RawContentSizeKb:N0} KB indirildi, parse ediliyor...");
        return ParseM3U(content, result);
    }

    List<ParsedChannel> ParseM3U(string content, TestResult result)
    {
        var channels = new List<ParsedChannel>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        string? currentInfo = null;
        int lineNum = 0;
        int parseErrors = 0;

        foreach (var rawLine in lines)
        {
            lineNum++;
            var line = rawLine.Trim();

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                currentInfo = line;
                continue;
            }

            if (line.StartsWith("#"))
                continue;

            if (!line.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    parseErrors++;
                continue;
            }

            var ch = new ParsedChannel { StreamUrl = line };

            if (currentInfo != null)
            {
                ch.Name = ExtractAttribute(currentInfo, "tvg-name") ?? ExtractAfterComma(currentInfo) ?? "?";
                ch.GroupTitle = ExtractAttribute(currentInfo, "group-title") ?? "";
                ch.LogoUrl = ExtractAttribute(currentInfo, "tvg-logo") ?? "";
                ch.TvgId = ExtractAttribute(currentInfo, "tvg-id") ?? "";
                ch.TvgCountry = ExtractAttribute(currentInfo, "tvg-country") ?? "";
            }

            ch.Type = DetermineType(ch.Name, ch.GroupTitle, line);
            channels.Add(ch);
            currentInfo = null;
        }

        result.ParseErrors = parseErrors;
        return channels;
    }

    // =========================================================================
    // XTREAM TEST
    // =========================================================================

    async Task<List<ParsedChannel>?> TestXtreamAsync(ProviderConfig config, TestResult result)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
            throw new Exception("Xtream için Host zorunludur.");

        var baseUrl = config.Host.TrimEnd('/');
        result.EndpointUrl = $"{baseUrl}/player_api.php";

        // 1. Kimlik doğrulama testi
        var authUrl = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(config.Username ?? "")}&password={Uri.EscapeDataString(config.Password ?? "")}";
        
        var sw = Stopwatch.StartNew();
        var authResponse = await Http.GetAsync(authUrl);
        result.ConnectionLatencyMs = (int)sw.ElapsedMilliseconds;
        result.HttpStatusCode = (int)authResponse.StatusCode;

        if (!authResponse.IsSuccessStatusCode)
            throw new Exception($"HTTP {result.HttpStatusCode} — Auth başarısız");

        var authJson = await authResponse.Content.ReadAsStringAsync();
        var authData = TryParseXtreamAuth(authJson);
        
        if (authData == null)
            throw new Exception("Auth yanıtı parse edilemedi");

        if (authData.UserInfo?.Auth == 0)
            throw new Exception("Kullanıcı adı veya şifre hatalı (auth=0)");

        result.ConnectionOk = true;
        result.XtreamServerInfo = authData.ServerInfo;
        result.XtreamUserInfo = authData.UserInfo;

        Console.WriteLine($"\n  → Auth OK ({result.ConnectionLatencyMs}ms)");
        Console.WriteLine($"  → Sunucu: {authData.ServerInfo?.Url} | Kullanıcı: {authData.UserInfo?.Username}");
        Console.WriteLine($"  → Abonelik: {authData.UserInfo?.ExpirationDate}");

        var channels = new List<ParsedChannel>();

        // 2. Canlı kanalları çek
        Console.Write("  → Canlı kanallar indiriliyor...");
        try
        {
            var liveUrl = $"{baseUrl}/player_api.php?username={config.Username}&password={config.Password}&action=get_live_streams";
            var liveJson = await Http.GetStringAsync(liveUrl);
            var liveChannels = ParseXtreamStreams(liveJson, ChannelTypeEnum.Live, baseUrl, config.Username!, config.Password!);
            channels.AddRange(liveChannels);
            Console.WriteLine($" {liveChannels.Count} kanal");
        }
        catch (Exception ex) { Console.WriteLine($" HATA: {ex.Message}"); }

        // 3. VOD çek
        Console.Write("  → VOD (film) indiriliyor...");
        try
        {
            var vodUrl = $"{baseUrl}/player_api.php?username={config.Username}&password={config.Password}&action=get_vod_streams";
            var vodJson = await Http.GetStringAsync(vodUrl);
            var vodChannels = ParseXtreamStreams(vodJson, ChannelTypeEnum.VOD, baseUrl, config.Username!, config.Password!);
            channels.AddRange(vodChannels);
            Console.WriteLine($" {vodChannels.Count} içerik");
        }
        catch (Exception ex) { Console.WriteLine($" HATA: {ex.Message}"); }

        // 4. Diziler çek
        Console.Write("  → Diziler indiriliyor...");
        try
        {
            var seriesUrl = $"{baseUrl}/player_api.php?username={config.Username}&password={config.Password}&action=get_series";
            var seriesJson = await Http.GetStringAsync(seriesUrl);
            var seriesChannels = ParseXtreamSeries(seriesJson);
            channels.AddRange(seriesChannels);
            Console.WriteLine($" {seriesChannels.Count} dizi");
        }
        catch (Exception ex) { Console.WriteLine($" HATA: {ex.Message}"); }

        return channels;
    }

    // =========================================================================
    // STALKER PORTAL TEST
    // =========================================================================

    async Task<List<ParsedChannel>?> TestStalkerAsync(ProviderConfig config, TestResult result)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
            throw new Exception("Stalker için Host zorunludur.");
        if (string.IsNullOrWhiteSpace(config.MacAddress))
            throw new Exception("Stalker için MAC adresi zorunludur.");

        var baseUrl = config.Host.TrimEnd('/');
        
        // API path tespiti
        string apiBase = await DetectStalkerApiPathAsync(baseUrl);
        result.EndpointUrl = apiBase;

        Console.WriteLine($"\n  → Stalker API: {apiBase}");

        // 1. Token al
        var sw = Stopwatch.StartNew();
        var handshakeUrl = $"{apiBase}?action=handshake&type=stb&token=&JsHttpRequest=1-xml";
        var handshakeReq = CreateStalkerRequest(handshakeUrl, config.MacAddress, "");
        var handshakeResp = await Http.SendAsync(handshakeReq);
        result.ConnectionLatencyMs = (int)sw.ElapsedMilliseconds;
        result.HttpStatusCode = (int)handshakeResp.StatusCode;

        if (!handshakeResp.IsSuccessStatusCode)
            throw new Exception($"HTTP {result.HttpStatusCode} — Handshake başarısız");

        var handshakeJson = await handshakeResp.Content.ReadAsStringAsync();
        var token = ExtractStalkerToken(handshakeJson);

        if (string.IsNullOrEmpty(token))
            throw new Exception("Token alınamadı — MAC adresi geçersiz olabilir");

        result.ConnectionOk = true;
        Console.WriteLine($"  → Token alındı ({result.ConnectionLatencyMs}ms): {token[..Math.Min(20, token.Length)]}...");

        // 2. Profil bilgisi
        try
        {
            var profileUrl = $"{apiBase}?action=get_profile&JsHttpRequest=1-xml";
            var profileReq = CreateStalkerRequest(profileUrl, config.MacAddress, token);
            var profileResp = await Http.SendAsync(profileReq);
            var profileJson = await profileResp.Content.ReadAsStringAsync();
            result.StalkerProfile = ExtractStalkerField(profileJson, "fname");
        }
        catch { /* profil isteğe bağlı */ }

        var channels = new List<ParsedChannel>();

        // 3. Canlı kategoriler + kanallar
        Console.Write("  → Canlı kategoriler yükleniyor...");
        try
        {
            var catUrl = $"{apiBase}?action=get_genres&type=itv&JsHttpRequest=1-xml";
            var catReq = CreateStalkerRequest(catUrl, config.MacAddress, token);
            var catResp = await Http.SendAsync(catReq);
            var catJson = await catResp.Content.ReadAsStringAsync();
            var categoryIds = ExtractStalkerCategoryIds(catJson);
            Console.WriteLine($" {categoryIds.Count} kategori");

            int totalLive = 0;
            foreach (var catId in categoryIds.Take(20)) // İlk 20 kategori (hız için)
            {
                try
                {
                    var chUrl = $"{apiBase}?action=get_ordered_list&type=itv&genre={catId}&p=1&JsHttpRequest=1-xml";
                    var chReq = CreateStalkerRequest(chUrl, config.MacAddress, token);
                    var chResp = await Http.SendAsync(chReq);
                    var chJson = await chResp.Content.ReadAsStringAsync();
                    var liveChannels = ParseStalkerChannels(chJson, ChannelTypeEnum.Live);
                    channels.AddRange(liveChannels);
                    totalLive += liveChannels.Count;
                }
                catch { /* kategori başarısız olursa devam et */ }
            }
            Console.WriteLine($"  → {totalLive} canlı kanal");
        }
        catch (Exception ex) { Console.WriteLine($" HATA: {ex.Message}"); }

        // 4. VOD
        Console.Write("  → VOD kategoriler yükleniyor...");
        try
        {
            var catUrl = $"{apiBase}?action=get_genres&type=vod&JsHttpRequest=1-xml";
            var catReq = CreateStalkerRequest(catUrl, config.MacAddress, token);
            var catResp = await Http.SendAsync(catReq);
            var catJson = await catResp.Content.ReadAsStringAsync();
            var categoryIds = ExtractStalkerCategoryIds(catJson);
            Console.WriteLine($" {categoryIds.Count} kategori");

            int totalVod = 0;
            foreach (var catId in categoryIds.Take(10))
            {
                try
                {
                    var chUrl = $"{apiBase}?action=get_ordered_list&type=vod&category={catId}&p=1&JsHttpRequest=1-xml";
                    var chReq = CreateStalkerRequest(chUrl, config.MacAddress, token);
                    var chResp = await Http.SendAsync(chReq);
                    var chJson = await chResp.Content.ReadAsStringAsync();
                    var vodChannels = ParseStalkerChannels(chJson, ChannelTypeEnum.VOD);
                    channels.AddRange(vodChannels);
                    totalVod += vodChannels.Count;
                }
                catch { }
            }
            Console.WriteLine($"  → {totalVod} VOD içerik");
        }
        catch (Exception ex) { Console.WriteLine($" HATA: {ex.Message}"); }

        return channels;
    }

    // =========================================================================
    // KANAL ANALİZİ
    // =========================================================================

    void AnalyzeChannels(List<ParsedChannel> channels, TestResult result)
    {
        result.TotalChannels = channels.Count;
        result.LiveCount = channels.Count(c => c.Type == ChannelTypeEnum.Live);
        result.VodCount = channels.Count(c => c.Type == ChannelTypeEnum.VOD);
        result.SeriesCount = channels.Count(c => c.Type == ChannelTypeEnum.Series);
        result.UnknownCount = channels.Count(c => c.Type == ChannelTypeEnum.Unknown);

        // Kategoriler
        result.UniqueGroups = channels
            .Where(c => !string.IsNullOrWhiteSpace(c.GroupTitle))
            .Select(c => c.GroupTitle!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToList();

        // Logo eksikliği
        result.ChannelsWithoutLogo = channels.Count(c => string.IsNullOrWhiteSpace(c.LogoUrl));
        result.ChannelsWithoutGroup = channels.Count(c => string.IsNullOrWhiteSpace(c.GroupTitle));

        // URL kalitesi
        result.DuplicateUrls = channels
            .GroupBy(c => c.StreamUrl)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Take(10)
            .ToList();

        // HTTP vs HTTPS dağılımı
        result.HttpsUrlCount = channels.Count(c => c.StreamUrl?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true);
        result.HttpUrlCount = channels.Count(c => c.StreamUrl?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true);
    }

    void AnalyzeSeriesParsing(List<ParsedChannel> channels, TestResult result)
    {
        var seriesChannels = channels.Where(c => c.Type == ChannelTypeEnum.Series).ToList();
        if (seriesChannels.Count == 0) return;

        var parseResults = new List<SeriesParseResult>();
        int parseOk = 0, parseFail = 0;
        var nameDistribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var problematicTitles = new List<string>();

        foreach (var ch in seriesChannels)
        {
            var parsed = SeriesInfoParser.Parse(ch.Name);
            var parseResult = new SeriesParseResult
            {
                OriginalTitle = ch.Name ?? "",
                ParsedName = parsed.SeriesName,
                Season = parsed.Season,
                Episode = parsed.Episode,
                IsOk = parsed.SeriesName != "Bilinmeyen Dizi" && parsed.Season >= 0 && parsed.Episode >= 0
            };

            if (parseResult.IsOk)
            {
                parseOk++;
                var key = SeriesInfoParser.NormalizeKey(parsed.SeriesName);
                if (!string.IsNullOrWhiteSpace(key))
                    nameDistribution[key] = nameDistribution.GetValueOrDefault(key) + 1;
            }
            else
            {
                parseFail++;
                problematicTitles.Add(ch.Name ?? "");
            }

            parseResults.Add(parseResult);
        }

        result.SeriesParseOk = parseOk;
        result.SeriesParseFail = parseFail;
        result.SeriesParseRate = seriesChannels.Count > 0 ? (double)parseOk / seriesChannels.Count * 100 : 0;

        // En çok bölümü olan diziler
        result.TopSeries = nameDistribution
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => $"{kv.Key} ({kv.Value} bölüm)")
            .ToList();

        // Sorunlu başlıklar (en kötü 20)
        result.ProblematicTitles = problematicTitles.Take(20).ToList();

        // Canonical key çakışmaları (farklı isim ama aynı key → iyi, gruplama çalışıyor)
        var canonicalGroups = seriesChannels
            .GroupBy(c => SeriesInfoParser.NormalizeKey(c.Name))
            .Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new { Key = g.Key, Names = g.Select(c => c.Name).Distinct().Take(3).ToList(), Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(10)
            .ToList();

        result.CanonicalGroupings = canonicalGroups
            .Select(g => $"[{g.Key}] → {string.Join(" / ", g.Names)} ({g.Count} bölüm)")
            .ToList();

        // Yanlış Live olarak işaretlenen (IsLiveSeries = true ama Series kategorisinde)
        result.FalsePositiveLive = seriesChannels
            .Where(c => SeriesInfoParser.IsLiveSeries(c.Name))
            .Select(c => c.Name ?? "")
            .Take(10)
            .ToList();
    }

    void AnalyzeLanguageDetection(List<ParsedChannel> channels, TestResult result)
    {
        var langDistribution = new Dictionary<string, int>();

        foreach (var ch in channels)
        {
            var lang = SeriesInfoParser.ExtractLanguageCode(ch.GroupTitle ?? ch.Name);
            langDistribution[lang] = langDistribution.GetValueOrDefault(lang) + 1;
        }

        result.LanguageDistribution = langDistribution
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // "UK" false-positive kontrolü
        result.UkFalsePositives = channels
            .Where(c =>
            {
                var name = (c.GroupTitle ?? c.Name ?? "").ToUpperInvariant();
                // "UK" var ama gerçekten İngilizce değil (Türkçe kelimede UK geçiyor)
                return name.Contains("UK") &&
                       SeriesInfoParser.ExtractLanguageCode(name) == "en-US" &&
                       !Regex.IsMatch(name, @"\bUK\b") && // word boundary yok
                       (name.Contains("TUR") || name.Contains("TR"));
            })
            .Select(c => c.GroupTitle ?? c.Name ?? "")
            .Take(5)
            .ToList();
    }

    // =========================================================================
    // RAPORLAMA
    // =========================================================================

    void PrintDetailedReport(TestResult r)
    {
        Console.WriteLine("\n" + new string('═', 70));
        Console.WriteLine($"  📊 TEST RAPORU — {r.ProviderName}");
        Console.WriteLine(new string('═', 70));

        Console.WriteLine($"\n🔌 BAĞLANTI");
        Console.WriteLine($"   Durum    : {(r.ConnectionOk ? "✅ BAŞARILI" : "❌ BAŞARISIZ")}");
        Console.WriteLine($"   Endpoint : {r.EndpointUrl}");
        Console.WriteLine($"   HTTP     : {r.HttpStatusCode}");
        Console.WriteLine($"   Gecikme  : {r.ConnectionLatencyMs}ms");
        Console.WriteLine($"   Süre     : {r.ElapsedMs}ms toplam");

        if (!r.ConnectionOk)
        {
            Console.WriteLine($"\n❌ HATA: {r.ConnectionError}");
            return;
        }

        if (r.ProviderType == "xtream" && r.XtreamUserInfo != null)
        {
            Console.WriteLine($"\n👤 XTREAM HESAP BİLGİSİ");
            Console.WriteLine($"   Kullanıcı  : {r.XtreamUserInfo.Username}");
            Console.WriteLine($"   Paket      : {r.XtreamUserInfo.MaxConnections} bağlantı");
            Console.WriteLine($"   Bitiş      : {r.XtreamUserInfo.ExpirationDate}");
            Console.WriteLine($"   Durum      : {r.XtreamUserInfo.Status}");
        }

        if (r.ProviderType == "m3u")
            Console.WriteLine($"\n   İçerik boyutu: {r.RawContentSizeKb:N0} KB");

        Console.WriteLine($"\n📺 KANAL DAĞILIMI");
        Console.WriteLine($"   Toplam   : {r.TotalChannels:N0}");
        Console.WriteLine($"   Canlı TV : {r.LiveCount:N0}");
        Console.WriteLine($"   Film/VOD : {r.VodCount:N0}");
        Console.WriteLine($"   Dizi     : {r.SeriesCount:N0}");
        Console.WriteLine($"   Bilinmiyor: {r.UnknownCount:N0}");

        if (r.ParseErrors > 0)
            Console.WriteLine($"   ⚠️ Parse hatası: {r.ParseErrors}");

        Console.WriteLine($"\n🗂️ KATEGORİ ANALİZİ");
        Console.WriteLine($"   Toplam grup     : {r.UniqueGroups?.Count ?? 0}");
        Console.WriteLine($"   Grupsu kanal    : {r.ChannelsWithoutGroup}");
        Console.WriteLine($"   Logosuz kanal   : {r.ChannelsWithoutLogo}");
        Console.WriteLine($"   Tekrarlı URL    : {r.DuplicateUrls?.Count ?? 0}");
        Console.WriteLine($"   HTTPS kanallar  : {r.HttpsUrlCount} / {r.TotalChannels}");

        if (r.UniqueGroups?.Count > 0)
        {
            Console.WriteLine($"\n   İlk 20 grup:");
            foreach (var g in r.UniqueGroups.Take(20))
                Console.WriteLine($"     • {g}");
        }

        if (r.SeriesCount > 0)
        {
            Console.WriteLine($"\n🎬 DİZİ PARSE ANALİZİ");
            Console.WriteLine($"   Parse başarı oranı : %{r.SeriesParseRate:F1}");
            Console.WriteLine($"   Başarılı           : {r.SeriesParseOk}");
            Console.WriteLine($"   Başarısız          : {r.SeriesParseFail}");

            if (r.TopSeries?.Count > 0)
            {
                Console.WriteLine($"\n   🏆 En çok bölümlü diziler:");
                foreach (var s in r.TopSeries)
                    Console.WriteLine($"     • {s}");
            }

            if (r.CanonicalGroupings?.Count > 0)
            {
                Console.WriteLine($"\n   🔗 Çapraz-provider canonical gruplamalar (iyi):");
                foreach (var g in r.CanonicalGroupings)
                    Console.WriteLine($"     • {g}");
            }

            if (r.FalsePositiveLive?.Count > 0)
            {
                Console.WriteLine($"\n   ⚠️ Yanlış 'Canlı' olarak işaretlenen diziler:");
                foreach (var t in r.FalsePositiveLive)
                    Console.WriteLine($"     • {t}");
            }

            if (r.ProblematicTitles?.Count > 0)
            {
                Console.WriteLine($"\n   ❌ Parse edilemeyen başlıklar:");
                foreach (var t in r.ProblematicTitles.Take(10))
                    Console.WriteLine($"     • {t}");
            }
        }

        Console.WriteLine($"\n🌍 DİL TESPİT DAĞILIMI");
        if (r.LanguageDistribution?.Count > 0)
        {
            foreach (var kv in r.LanguageDistribution.Take(10))
                Console.WriteLine($"   {kv.Key,-10}: {kv.Value,5} kanal");
        }

        if (r.UkFalsePositives?.Count > 0)
        {
            Console.WriteLine($"\n   ⚠️ 'UK' false-positive tespiti:");
            foreach (var fp in r.UkFalsePositives)
                Console.WriteLine($"     • {fp}");
        }

        if (r.DuplicateUrls?.Count > 0)
        {
            Console.WriteLine($"\n⚠️  TEKRARLAYAN URL'LER");
            foreach (var url in r.DuplicateUrls.Take(5))
                Console.WriteLine($"   • {url}");
        }

        // Sağlık skoru
        var score = CalculateHealthScore(r);
        Console.WriteLine($"\n{'─',0}{'─',0}{'─',0}");
        Console.WriteLine($"  📈 GENEL SAĞLIK SKORU: {score}/100  {GetScoreEmoji(score)}");
        Console.WriteLine(new string('═', 70));
    }

    void PrintBatchSummary(List<TestResult> results)
    {
        Console.WriteLine(new string('═', 70));
        Console.WriteLine("  📊 TOPLU TEST SONUÇLARI");
        Console.WriteLine(new string('═', 70));
        Console.WriteLine($"  {"Provider",-28} {"Tip",-8} {"Durum",-6} {"Kanal",-8} {"Dizi%",-8} {"Skor",-6}");
        Console.WriteLine(new string('─', 70));

        foreach (var r in results)
        {
            var status = r.ConnectionOk ? "✅" : "❌";
            var parseRate = r.SeriesCount > 0 ? $"%{r.SeriesParseRate:F0}" : "  —  ";
            var score = r.ConnectionOk ? $"{CalculateHealthScore(r)}" : "—";
            Console.WriteLine($"  {r.ProviderName,-28} {r.ProviderType,-8} {status,-6} {r.TotalChannels,-8:N0} {parseRate,-8} {score,-6}");
        }

        Console.WriteLine(new string('─', 70));
        var totalChannels = results.Sum(r => r.TotalChannels);
        var successCount = results.Count(r => r.ConnectionOk);
        Console.WriteLine($"\n  ✅ {successCount}/{results.Count} provider başarılı");
        Console.WriteLine($"  📺 Toplam: {totalChannels:N0} kanal");
    }

    void GenerateHtmlReport(List<TestResult> results, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='tr'><head><meta charset='UTF-8'>");
        sb.AppendLine("<title>Noctra Provider Test Raporu</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;background:#0f0f0f;color:#e0e0e0;margin:0;padding:20px}");
        sb.AppendLine("h1{color:#a855f7;border-bottom:2px solid #a855f7;padding-bottom:10px}");
        sb.AppendLine("h2{color:#c084fc;margin-top:30px}");
        sb.AppendLine(".card{background:#1a1a2e;border:1px solid #2d2d4e;border-radius:12px;padding:20px;margin:15px 0}");
        sb.AppendLine(".ok{color:#22c55e}.fail{color:#ef4444}.warn{color:#f59e0b}");
        sb.AppendLine(".badge{display:inline-block;padding:2px 8px;border-radius:9999px;font-size:12px;font-weight:bold}");
        sb.AppendLine(".badge-ok{background:#14532d;color:#22c55e}.badge-fail{background:#450a0a;color:#ef4444}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin-top:10px}");
        sb.AppendLine("th{background:#2d2d4e;padding:8px;text-align:left;color:#a855f7}");
        sb.AppendLine("td{padding:6px 8px;border-bottom:1px solid #2d2d4e}");
        sb.AppendLine(".score-bar{height:8px;background:#2d2d4e;border-radius:4px;margin-top:4px}");
        sb.AppendLine(".score-fill{height:100%;border-radius:4px;background:linear-gradient(90deg,#a855f7,#ec4899)}");
        sb.AppendLine(".stat{display:inline-block;background:#2d2d4e;border-radius:8px;padding:10px 16px;margin:5px;text-align:center}");
        sb.AppendLine(".stat-num{font-size:22px;font-weight:bold;color:#a855f7}.stat-label{font-size:11px;color:#888;margin-top:2px}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>🎬 Noctra Provider Test Raporu</h1>");
        sb.AppendLine($"<p style='color:#666'>Oluşturulma: {DateTime.Now:dd.MM.yyyy HH:mm:ss} | {results.Count} provider</p>");

        foreach (var r in results)
        {
            var score = r.ConnectionOk ? CalculateHealthScore(r) : 0;
            var badgeClass = r.ConnectionOk ? "badge-ok" : "badge-fail";
            var badgeText = r.ConnectionOk ? "✅ BAŞARILI" : "❌ BAŞARISIZ";

            sb.AppendLine($"<div class='card'>");
            sb.AppendLine($"<h2>{r.ProviderName} <span class='badge {badgeClass}'>{badgeText}</span> &nbsp; <small style='color:#888'>{r.ProviderType.ToUpper()}</small></h2>");

            if (!r.ConnectionOk)
            {
                sb.AppendLine($"<p class='fail'>Hata: {r.ConnectionError}</p></div>");
                continue;
            }

            // Skor
            sb.AppendLine($"<p>Sağlık Skoru: <strong style='color:#a855f7'>{score}/100</strong></p>");
            sb.AppendLine($"<div class='score-bar'><div class='score-fill' style='width:{score}%'></div></div>");

            // Bağlantı bilgisi
            sb.AppendLine($"<p style='color:#666;font-size:13px'>🔗 {r.EndpointUrl} &nbsp;|&nbsp; HTTP {r.HttpStatusCode} &nbsp;|&nbsp; {r.ConnectionLatencyMs}ms</p>");

            // İstatistikler
            sb.AppendLine("<div style='margin:15px 0'>");
            AppendStat(sb, r.TotalChannels.ToString("N0"), "Toplam Kanal");
            AppendStat(sb, r.LiveCount.ToString("N0"), "Canlı TV");
            AppendStat(sb, r.VodCount.ToString("N0"), "VOD/Film");
            AppendStat(sb, r.SeriesCount.ToString("N0"), "Dizi");
            AppendStat(sb, $"{r.UniqueGroups?.Count ?? 0}", "Kategori");
            AppendStat(sb, $"%{r.SeriesParseRate:F1}", "Parse Oranı");
            sb.AppendLine("</div>");

            // Dizi analizi
            if (r.SeriesCount > 0 && r.TopSeries?.Count > 0)
            {
                sb.AppendLine("<h3 style='color:#c084fc'>🏆 En Çok Bölümlü Diziler</h3><ul>");
                foreach (var s in r.TopSeries)
                    sb.AppendLine($"<li>{s}</li>");
                sb.AppendLine("</ul>");
            }

            // Sorunlu başlıklar
            if (r.ProblematicTitles?.Count > 0)
            {
                sb.AppendLine("<h3 style='color:#f59e0b'>⚠️ Parse Edilemeyen Başlıklar</h3><ul>");
                foreach (var t in r.ProblematicTitles)
                    sb.AppendLine($"<li class='warn'>{System.Web.HttpUtility.HtmlEncode(t)}</li>");
                sb.AppendLine("</ul>");
            }

            // Dil dağılımı
            if (r.LanguageDistribution?.Count > 0)
            {
                sb.AppendLine("<h3 style='color:#c084fc'>🌍 Dil Dağılımı</h3>");
                sb.AppendLine("<table><tr><th>Dil Kodu</th><th>Kanal Sayısı</th></tr>");
                foreach (var kv in r.LanguageDistribution)
                    sb.AppendLine($"<tr><td>{kv.Key}</td><td>{kv.Value:N0}</td></tr>");
                sb.AppendLine("</table>");
            }

            // Kategoriler
            if (r.UniqueGroups?.Count > 0)
            {
                sb.AppendLine("<h3 style='color:#c084fc'>🗂️ Kategoriler</h3><div style='display:flex;flex-wrap:wrap;gap:5px'>");
                foreach (var g in r.UniqueGroups.Take(50))
                    sb.AppendLine($"<span style='background:#2d2d4e;padding:2px 8px;border-radius:4px;font-size:12px'>{System.Web.HttpUtility.HtmlEncode(g)}</span>");
                sb.AppendLine("</div>");
            }

            // Deep Test Raporu (Eğer varsa)
            if (r.DeepTestReport != null)
            {
                AppendDeepTestHtml(sb, r.DeepTestReport);
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    string GetScoreColor(int score) => score switch { >= 80 => "#22c55e", >= 60 => "#f59e0b", >= 40 => "#f97316", _ => "#ef4444" };

    void AppendStat(StringBuilder sb, string value, string label)
    {
        sb.AppendLine($"<div class='stat'><div class='stat-num'>{value}</div><div class='stat-label'>{label}</div></div>");
    }

    int CalculateHealthScore(TestResult r)
    {
        if (!r.ConnectionOk) return 0;
        int score = 40; // bağlantı başarılı

        // Kanal sayısı
        if (r.TotalChannels > 10000) score += 15;
        else if (r.TotalChannels > 1000) score += 10;
        else if (r.TotalChannels > 100) score += 5;

        // Parse oranı
        if (r.SeriesCount > 0)
        {
            if (r.SeriesParseRate >= 95) score += 20;
            else if (r.SeriesParseRate >= 80) score += 12;
            else if (r.SeriesParseRate >= 60) score += 6;
        }
        else score += 10;

        // Kategorilendirme
        if (r.ChannelsWithoutGroup < r.TotalChannels * 0.05) score += 10;
        else if (r.ChannelsWithoutGroup < r.TotalChannels * 0.2) score += 5;

        // Hız
        if (r.ConnectionLatencyMs < 300) score += 5;
        else if (r.ConnectionLatencyMs < 1000) score += 2;

        // Duplicate
        if ((r.DuplicateUrls?.Count ?? 0) == 0) score += 5;

        return Math.Min(score, 100);
    }

    string GetScoreEmoji(int score) => score switch
    {
        >= 90 => "🏆 Mükemmel",
        >= 75 => "✅ İyi",
        >= 55 => "⚠️ Orta",
        >= 35 => "🔶 Zayıf",
        _ => "❌ Başarısız"
    };

    // =========================================================================
    // YARDIMCI METODLAR
    // =========================================================================

    ProviderConfig? ParseArgs(string[] args)
    {
        var config = new ProviderConfig();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--type":   config.Type = args[++i]; break;
                case "--url":    config.Url = args[++i]; break;
                case "--host":   config.Host = args[++i]; break;
                case "--user":   config.Username = args[++i]; break;
                case "--pass":   config.Password = args[++i]; break;
                case "--mac":    config.MacAddress = args[++i]; break;
                case "--name":   config.Name = args[++i]; break;
            }
        }
        if (string.IsNullOrEmpty(config.Type)) return null;
        config.Name ??= $"Test ({config.Type})";
        return config;
    }

    string? ExtractAttribute(string extinf, string attr)
    {
        var pattern = $@"{attr}=""([^""]*)""";
        var m = Regex.Match(extinf, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    string? ExtractAfterComma(string extinf)
    {
        var idx = extinf.LastIndexOf(',');
        return idx >= 0 ? extinf[(idx + 1)..].Trim() : null;
    }

    ChannelTypeEnum DetermineType(string? name, string? group, string url)
    {
        var urlLower = url.ToLowerInvariant();
        var groupLower = (group ?? "").ToLowerInvariant();
        var nameLower = (name ?? "").ToLowerInvariant();

        // URL öncelikli
        if (urlLower.Contains("/series/")) return ChannelTypeEnum.Series;
        if (urlLower.Contains("/movie/")) return ChannelTypeEnum.VOD;
        if (urlLower.Contains("/live/")) return ChannelTypeEnum.Live;

        // Grup
        if (groupLower.Contains("series") || groupLower.Contains("dizi")) return ChannelTypeEnum.Series;
        if (groupLower.Contains("movie") || groupLower.Contains("film") || groupLower.Contains("vod")) return ChannelTypeEnum.VOD;

        // İsim pattern'leri
        if (SeriesInfoParser.IsSeries(name)) return ChannelTypeEnum.Series;

        // URL uzantısı
        if (urlLower.EndsWith(".mp4") || urlLower.EndsWith(".mkv") || urlLower.EndsWith(".avi"))
            return ChannelTypeEnum.VOD;
        if (urlLower.EndsWith(".ts") || urlLower.EndsWith(".m3u8"))
            return ChannelTypeEnum.Live;

        return ChannelTypeEnum.Live; // default
    }

    XtreamAuthResponse? TryParseXtreamAuth(string json)
    {
        try { return JsonSerializer.Deserialize<XtreamAuthResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return null; }
    }

    List<ParsedChannel> ParseXtreamStreams(string json, ChannelTypeEnum type, string baseUrl, string user, string pass)
    {
        using var doc = JsonDocument.Parse(json);
        var channels = new List<ParsedChannel>();

        if (doc.RootElement.ValueKind != JsonValueKind.Array) return channels;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var ch = new ParsedChannel
            {
                Name = item.TryGetProperty("name", out var n) ? n.GetString() : "",
                GroupTitle = item.TryGetProperty("category_name", out var g) ? g.GetString() : "",
                LogoUrl = item.TryGetProperty("stream_icon", out var l) ? l.GetString() : "",
                Type = type
            };
            var streamId = item.TryGetProperty("stream_id", out var sid) ? sid.GetInt32().ToString() : "0";
            var ext = type == ChannelTypeEnum.VOD ? "mp4" : "ts";
            ch.StreamUrl = $"{baseUrl}/{(type == ChannelTypeEnum.Live ? "live" : "movie")}/{user}/{pass}/{streamId}.{ext}";
            channels.Add(ch);
        }
        return channels;
    }

    List<ParsedChannel> ParseXtreamSeries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var channels = new List<ParsedChannel>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return channels;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            channels.Add(new ParsedChannel
            {
                Name = item.TryGetProperty("name", out var n) ? n.GetString() : "",
                GroupTitle = item.TryGetProperty("category_name", out var g) ? g.GetString() : "",
                LogoUrl = item.TryGetProperty("cover", out var c) ? c.GetString() : "",
                Type = ChannelTypeEnum.Series,
                StreamUrl = item.TryGetProperty("series_id", out var sid) ? $"series://{sid}" : ""
            });
        }
        return channels;
    }

    async Task<string> DetectStalkerApiPathAsync(string baseUrl)
    {
        var candidates = new[]
        {
            $"{baseUrl}/server/load.php",
            $"{baseUrl}/stalker_portal/server/load.php",
            $"{baseUrl}/c/server/load.php",
            $"{baseUrl}/portal.php"
        };
        foreach (var url in candidates)
        {
            try
            {
                var resp = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
                if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                    return url;
            }
            catch { }
        }
        return $"{baseUrl}/server/load.php";
    }

    HttpRequestMessage CreateStalkerRequest(string url, string mac, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", $"mac={mac}; stb_lang=en; timezone=Europe/Istanbul");
        req.Headers.Add("X-User-Agent", "Model: MAG250; Link: WiFi");
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    string? ExtractStalkerToken(string json)
    {
        var m = Regex.Match(json, @"""token""\s*:\s*""([^""]+)""");
        return m.Success ? m.Groups[1].Value : null;
    }

    string? ExtractStalkerField(string json, string field)
    {
        var m = Regex.Match(json, $@"""{field}""\s*:\s*""([^""]*)""");
        return m.Success ? m.Groups[1].Value : null;
    }

    List<string> ExtractStalkerCategoryIds(string json)
    {
        var ids = new List<string>();
        foreach (Match m in Regex.Matches(json, @"""id""\s*:\s*""?(\d+)""?"))
            ids.Add(m.Groups[1].Value);
        return ids.Distinct().ToList();
    }

    List<ParsedChannel> ParseStalkerChannels(string json, ChannelTypeEnum type)
    {
        var channels = new List<ParsedChannel>();
        foreach (Match m in Regex.Matches(json, @"""name""\s*:\s*""([^""]+)"""))
            channels.Add(new ParsedChannel { Name = m.Groups[1].Value, Type = type, StreamUrl = "stalker://" + m.Groups[1].Value });
        return channels;
    }

    void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(@"
  ███╗   ██╗ ██████╗  ██████╗████████╗██████╗  █████╗ 
  ████╗  ██║██╔═══██╗██╔════╝╚══██╔══╝██╔══██╗██╔══██╗
  ██╔██╗ ██║██║   ██║██║        ██║   ██████╔╝███████║
  ██║╚██╗██║██║   ██║██║        ██║   ██╔══██╗██╔══██║
  ██║ ╚████║╚██████╔╝╚██████╗   ██║   ██║  ██║██║  ██║
  ╚═╝  ╚═══╝ ╚═════╝  ╚═════╝   ╚═╝   ╚═╝  ╚═╝╚═╝  ╚═╝
             Provider Test Aracı v1.0");
        Console.ResetColor();
        Console.WriteLine();
    }

    void PrintHelp()
    {
        Console.WriteLine("KULLANIM:");
        Console.WriteLine("  M3U      : dotnet run -- --type m3u --url <url> [--name <isim>]");
        Console.WriteLine("  Xtream   : dotnet run -- --type xtream --host <url> --user <u> --pass <p>");
        Console.WriteLine("  Stalker  : dotnet run -- --type stalker --host <url> --mac <mac>");
        Console.WriteLine("  Toplu    : dotnet run -- --batch [providers.json]");
        Console.WriteLine();
        Console.WriteLine("ÖRNEKLER:");
        Console.WriteLine("  dotnet run -- --type m3u --url http://example.com/playlist.m3u");
        Console.WriteLine("  dotnet run -- --type xtream --host http://srv.com:8080 --user abc --pass 123");
        Console.WriteLine("  dotnet run -- --type stalker --host http://portal.com --mac 00:1A:79:AA:BB:CC");
        Console.WriteLine("  dotnet run -- --batch  (providers.json dosyasından toplu test)");
    }
}

// =============================================================================
// SERİES INFO PARSER — Noctra.Core referansından bağımsız inline versiyon
// =============================================================================
static class SeriesInfoParser
{
    public record SeriesInfo(string SeriesName, int Season, int Episode);

    private static readonly Regex SxeRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss](?<season>\d{1,2})\s*[-._ ]*\s*[Ee](?<episode>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TurkishRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss]ezon\s*(?<season>\d{1,2}).*?[Bb](?:o|ö)l(?:u|ü)m\s*(?<episode>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TurkishEpOnlyRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)\b(?<episode>\d{1,3})\.?\s*[Bb](?:o|ö)l(?:u|ü)m\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EnglishRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)\b[Ss]eason\s*(?<season>\d{1,2}).*?[Ee]pisode\s*(?<episode>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonOnlyRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)\b(?:[Ss]eason|[Ss]ezon)\s*(?<season>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EpisodeTokenRegex = new(
        @"\b(?:[Ss]\d{1,2}\s*[Ee]\d{1,3}|\d{1,2}\s*[Xx]\s*\d{1,3}|[Ss]ezon\s*\d{1,2}|[Ss]eason\s*\d{1,2}|\d{1,3}\.?\s*[Bb](?:o|ö)l(?:u|ü)m|[Ee]p(?:isode)?\s*\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LiveSportsRegex = new(
        @"\b(beIN|be\*IN|be-IN|SPOR|EUROSPORT|TIVIBU|EXXENSPOR)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LiveKeywordRegex = new(
        @"\b(CANLI|LIVE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NoiseRegex = new(
        @"\b(?:4k|2160p|1080p|720p|480p|x264|x265|h264|hevc|webrip|webdl|bluray|hdrip|fhd|uhd|hd|sd)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SeriesInfo Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return new("Bilinmeyen Dizi", 1, 1);
        var t = title.Trim();

        if (IsLiveSeries(t)) return new(CleanName(t), 0, 0);

        foreach (var (rx, hasSeason) in new[] {
            (SxeRegex, true), (TurkishRegex, true), (EnglishRegex, true) })
        {
            var m = rx.Match(t);
            if (!m.Success) continue;
            return new(
                CleanName(m.Groups["name"].Value),
                int.TryParse(m.Groups["season"].Value, out var s) ? s : 1,
                int.TryParse(m.Groups["episode"].Value, out var e) ? e : 1);
        }

        var epOnly = TurkishEpOnlyRegex.Match(t);
        if (epOnly.Success)
            return new(CleanName(epOnly.Groups["name"].Value), 1,
                int.TryParse(epOnly.Groups["episode"].Value, out var e) ? e : 1);

        var sOnly = SeasonOnlyRegex.Match(t);
        if (sOnly.Success)
            return new(CleanName(sOnly.Groups["name"].Value),
                int.TryParse(sOnly.Groups["season"].Value, out var s) ? s : 1, 1);

        return new(CleanName(EpisodeTokenRegex.Replace(t, " ")), 1, 1);
    }

    public static bool IsSeries(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (IsLiveSeries(title)) return false;
        return SxeRegex.IsMatch(title) || TurkishRegex.IsMatch(title) ||
               TurkishEpOnlyRegex.IsMatch(title) || EnglishRegex.IsMatch(title) ||
               SeasonOnlyRegex.IsMatch(title) ||
               title.Contains("Staffel ", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Temporada ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLiveSeries(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (LiveSportsRegex.IsMatch(title) && !SxeRegex.IsMatch(title)) return true;
        if (title.Contains("7/24", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("24/7", StringComparison.OrdinalIgnoreCase)) return true;
        return LiveKeywordRegex.IsMatch(title) && !EpisodeTokenRegex.IsMatch(title);
    }

    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var n = value.Trim().ToLowerInvariant();
        // Türkçe normalize (Bug #5 düzeltmesi)
        n = n.Replace('ş', 's').Replace('ç', 'c').Replace('ğ', 'g')
             .Replace('ü', 'u').Replace('ö', 'o').Replace('ı', 'i');
        n = EpisodeTokenRegex.Replace(n, " ");
        n = NoiseRegex.Replace(n, " ");
        n = Regex.Replace(n, @"[^\w\s]", " ");
        return Regex.Replace(n, @"\s+", " ").Trim();
    }

    public static string ExtractLanguageCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "tr-TR";
        var t = text.Trim().ToUpperInvariant();

        // Word-boundary ile UK tespiti (Bug #6 düzeltmesi)
        var words = Regex.Split(t, @"[\s|/:,\[\]()]").Where(w => w.Length > 0).ToHashSet();

        if (t.Contains("TURKEY") || t.Contains("TÜRKİYE") || t.Contains("TURKIYE")) return "tr-TR";
        if (words.Contains("TR") || t.StartsWith("TR|") || t.StartsWith("TR/") || t.StartsWith("TR:")) return "tr-TR";
        if (words.Contains("UK") || words.Contains("USA") || words.Contains("US") || words.Contains("AU") ||
            t.Contains("UNITED STATES") || t.Contains("UNITED KINGDOM") || t.Contains("MULTI") ||
            words.Contains("EN") || words.Contains("ENG")) return "en-US";
        if (t.Contains("FRANCE") || t.Contains("FRENCH") || words.Contains("FR")) return "fr-FR";
        if (t.Contains("GERMANY") || t.Contains("GERMAN") || words.Contains("DE")) return "de-DE";
        if (t.Contains("SPAIN") || t.Contains("SPANISH") || words.Contains("ES")) return "es-ES";
        if (t.Contains("RUSSIA") || t.Contains("RUSSIAN") || words.Contains("RU")) return "ru-RU";
        if (t.Contains("ARABIC") || words.Contains("AR")) return "ar-SA";
        if (t.Contains("ITALY") || t.Contains("ITALIAN") || words.Contains("IT")) return "it-IT";

        return "tr-TR";
    }

    static string CleanName(string raw)
    {
        var cleaned = raw.Trim().TrimEnd('-', '.', '|', ':', '_', ' ');
        cleaned = NoiseRegex.Replace(cleaned, " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Bilinmeyen Dizi" : cleaned;
    }
}

// =============================================================================
// MODELler
// =============================================================================

class ProviderConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "m3u";
    public string? Url { get; set; }
    public string? Host { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? MacAddress { get; set; }
}

class BatchConfig
{
    public List<ProviderConfig> Providers { get; set; } = new();
}

enum ChannelTypeEnum { Live, VOD, Series, Unknown }

class ParsedChannel
{
    public string? Name { get; set; }
    public string? GroupTitle { get; set; }
    public string? LogoUrl { get; set; }
    public string? TvgId { get; set; }
    public string? TvgCountry { get; set; }
    public string? StreamUrl { get; set; }
    public ChannelTypeEnum Type { get; set; }
}

class SeriesParseResult
{
    public string OriginalTitle { get; set; } = "";
    public string ParsedName { get; set; } = "";
    public int Season { get; set; }
    public int Episode { get; set; }
    public bool IsOk { get; set; }
}

class TestResult
{
    public string ProviderName { get; set; } = "";
    public string ProviderType { get; set; } = "";
    public string? EndpointUrl { get; set; }
    public bool ConnectionOk { get; set; }
    public string? ConnectionError { get; set; }
    public int HttpStatusCode { get; set; }
    public int ConnectionLatencyMs { get; set; }
    public int ElapsedMs { get; set; }
    public int RawContentSizeKb { get; set; }
    public int TotalChannels { get; set; }
    public int LiveCount { get; set; }
    public int VodCount { get; set; }
    public int SeriesCount { get; set; }
    public int UnknownCount { get; set; }
    public int ParseErrors { get; set; }
    public int ChannelsWithoutLogo { get; set; }
    public int ChannelsWithoutGroup { get; set; }
    public int HttpsUrlCount { get; set; }
    public int HttpUrlCount { get; set; }
    public List<string>? UniqueGroups { get; set; }
    public List<string>? DuplicateUrls { get; set; }
    public int SeriesParseOk { get; set; }
    public int SeriesParseFail { get; set; }
    public double SeriesParseRate { get; set; }
    public List<string>? TopSeries { get; set; }
    public List<string>? ProblematicTitles { get; set; }
    public List<string>? CanonicalGroupings { get; set; }
    public List<string>? FalsePositiveLive { get; set; }
    public Dictionary<string, int>? LanguageDistribution { get; set; }
    public List<string>? UkFalsePositives { get; set; }
    public XtreamUserInfo? XtreamUserInfo { get; set; }
    public XtreamServerInfo? XtreamServerInfo { get; set; }
    public string? StalkerProfile { get; set; }
    public List<ParsedChannel>? Channels { get; set; }
    public DeepSamplingResult? DeepTestReport { get; set; }
    public bool DeepTestSkipped { get; set; }
}

class XtreamAuthResponse
{
    [JsonPropertyName("user_info")] public XtreamUserInfo? UserInfo { get; set; }
    [JsonPropertyName("server_info")] public XtreamServerInfo? ServerInfo { get; set; }
}

class XtreamUserInfo
{
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("exp_date")] public string? ExpirationDate { get; set; }
    [JsonPropertyName("max_connections")] public string? MaxConnections { get; set; }
    [JsonPropertyName("auth")] public int Auth { get; set; }
}

class XtreamServerInfo
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("server_protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("port")] public string? Port { get; set; }
}

// End of Program.cs
