using System.Text;
using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Services;

partial class NoctraProviderTester
{
    async Task RunPlaylistAuditAsync(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.WriteLine("KULLANIM: dotnet run -- --audit-playlist <m3u-path-or-url> [--out rapor.html]");
            return;
        }

        var source = args[1];
        var outputPath = "playlist_audit_report.html";
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length)
            {
                outputPath = args[++i];
            }
        }

        Console.WriteLine($"🔎 Playlist audit başlıyor: {source}");

        var parser = new M3UParser(Http);
        var channels = IsHttpSource(source)
            ? await parser.ParseFromUrlAsync(source)
            : await parser.ParseFromFileAsync(source);

        var report = BuildPlaylistAuditReport(source, channels);
        PrintPlaylistAuditReport(report);
        WritePlaylistAuditHtml(report, outputPath);

        Console.WriteLine($"\n📄 Playlist audit raporu: {outputPath}");
    }

    static PlaylistAuditReport BuildPlaylistAuditReport(string source, List<Channel> channels)
    {
        var report = new PlaylistAuditReport
        {
            Source = source,
            GeneratedAt = DateTime.Now,
            Total = channels.Count,
            LiveCount = channels.Count(c => c.Type == ChannelType.Live),
            VodCount = channels.Count(c => c.Type == ChannelType.VOD),
            SeriesCount = channels.Count(c => c.Type == ChannelType.Series),
            GroupCount = channels.Select(c => NormalizeGroup(c.GroupTitle)).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        };

        report.TopGroups = channels
            .GroupBy(c => NormalizeGroup(c.GroupTitle), StringComparer.OrdinalIgnoreCase)
            .Select(g => new PlaylistAuditGroupSummary
            {
                Group = g.Key,
                Count = g.Count(),
                LiveCount = g.Count(c => c.Type == ChannelType.Live),
                VodCount = g.Count(c => c.Type == ChannelType.VOD),
                SeriesCount = g.Count(c => c.Type == ChannelType.Series)
            })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Group)
            .Take(50)
            .ToList();

        report.OthersFindings = channels
            .Where(IsOthersGroup)
            .Select(c => new PlaylistAuditFinding
            {
                Severity = "Info",
                Reason = ExplainOthersReason(c),
                Type = c.Type,
                Group = NormalizeGroup(c.GroupTitle),
                Country = c.Country ?? "",
                Name = c.Name ?? "",
                Url = c.StreamUrl ?? ""
            })
            .ToList();

        report.OthersReasonCounts = report.OthersFindings
            .GroupBy(f => f.Reason)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        report.SuspiciousFindings = channels
            .SelectMany(BuildSuspiciousFindings)
            .OrderBy(f => f.Severity == "High" ? 0 : f.Severity == "Medium" ? 1 : 2)
            .ThenBy(f => f.Group)
            .ThenBy(f => f.Name)
            .Take(300)
            .ToList();

        report.MissingGroupSamples = channels
            .Where(c => string.IsNullOrWhiteSpace(c.GroupTitle) || IsUndefinedGroup(c.GroupTitle))
            .Take(100)
            .Select(c => new PlaylistAuditFinding
            {
                Severity = "Medium",
                Reason = "Provider group-title boş veya undefined.",
                Type = c.Type,
                Group = NormalizeGroup(c.GroupTitle),
                Country = c.Country ?? "",
                Name = c.Name ?? "",
                Url = c.StreamUrl ?? ""
            })
            .ToList();

        return report;
    }

    static IEnumerable<PlaylistAuditFinding> BuildSuspiciousFindings(Channel channel)
    {
        var group = NormalizeGroup(channel.GroupTitle);
        var name = channel.Name ?? "";
        var haystack = $"{group} {name}";
        var isAdult = LooksAdultLike(group);
        var hasExplicitProviderType = HasExplicitProviderTypeSignal(channel.StreamUrl);

        if (!hasExplicitProviderType && channel.Type == ChannelType.VOD && LooksLinearLiveLike(group) && !LooksMovieLike(group))
        {
            yield return CreateFinding("High", "Canlı yayın çağrışımlı grup veya isim VOD olarak sınıflanmış.", channel);
        }

        if (!hasExplicitProviderType && !isAdult && channel.Type != ChannelType.Series && SeriesInfoParser.IsSeries(name) && !LooksBareNumericChannelName(name))
        {
            yield return CreateFinding("High", "İsim sezon/bölüm paterni taşıyor ama Series değil.", channel);
        }

        if (IsLowInformationGroup(group) && !IsOthersGroup(channel) && !IsTrustedFallbackGroup(group))
        {
            yield return CreateFinding("Low", "Grup adı çok düşük bilgi taşıyor; provider kategorisi fazla genel olabilir.", channel);
        }
    }

    static PlaylistAuditFinding CreateFinding(string severity, string reason, Channel channel) => new()
    {
        Severity = severity,
        Reason = reason,
        Type = channel.Type,
        Group = NormalizeGroup(channel.GroupTitle),
        Country = channel.Country ?? "",
        Name = channel.Name ?? "",
        Url = channel.StreamUrl ?? ""
    };

    static string ExplainOthersReason(Channel channel)
    {
        var group = NormalizeGroup(channel.GroupTitle);
        var name = channel.Name ?? "";
        var url = channel.StreamUrl ?? "";

        if (string.IsNullOrWhiteSpace(channel.GroupTitle) || IsUndefinedGroup(channel.GroupTitle))
        {
            return "Provider group-title vermedi; parser isim/URL üzerinden güvenli grup çıkaramadı.";
        }

        if (channel.Type == ChannelType.Series)
        {
            return "Series olarak algılandı ama dizi adı güvenli çıkarılamadığı için Series / Others altında kaldı.";
        }

        if (channel.Type == ChannelType.VOD && HasQualityPrefix(name))
        {
            return "VOD kaydında kalite prefix'i grup kabul edilmedi; gerçek provider grubu olmadığı için Movies / Others altında kaldı.";
        }

        if (channel.Type == ChannelType.VOD && LooksNumericProxyUrl(url))
        {
            return "VOD tipi var ancak URL numeric/proxy formatında ve provider grubu yetersiz; Movies / Others altında kaldı.";
        }

        if (channel.Type == ChannelType.Live && group.Equals("Live / Others", StringComparison.OrdinalIgnoreCase))
        {
            return "Live kayıt için güvenli ülke grubu çıkarılamadı.";
        }

        return "Parser genel fallback grubu kullandı; provider group-title yoktu, boştu veya güvenli kategori çıkarılamadı.";
    }

    static void PrintPlaylistAuditReport(PlaylistAuditReport report)
    {
        Console.WriteLine("\n" + new string('═', 80));
        Console.WriteLine("  PLAYLIST AUDIT RAPORU");
        Console.WriteLine(new string('═', 80));
        Console.WriteLine($"Kaynak : {report.Source}");
        Console.WriteLine($"Toplam : {report.Total:N0}");
        Console.WriteLine($"Live   : {report.LiveCount:N0}");
        Console.WriteLine($"VOD    : {report.VodCount:N0}");
        Console.WriteLine($"Series : {report.SeriesCount:N0}");
        Console.WriteLine($"Grup   : {report.GroupCount:N0}");

        Console.WriteLine("\nEn Büyük Gruplar:");
        foreach (var group in report.TopGroups.Take(20))
        {
            Console.WriteLine($"  {group.Count,6:N0}  {group.Group}  (L:{group.LiveCount:N0} V:{group.VodCount:N0} S:{group.SeriesCount:N0})");
        }

        Console.WriteLine("\nOthers Analizi:");
        if (report.OthersFindings.Count == 0)
        {
            Console.WriteLine("  Others bucket'a düşen kayıt yok.");
        }
        else
        {
            foreach (var item in report.OthersReasonCounts)
            {
                Console.WriteLine($"  {item.Value,6:N0}  {item.Key}");
            }

            Console.WriteLine("\nOthers Örnekleri:");
            foreach (var finding in report.OthersFindings.Take(20))
            {
                Console.WriteLine($"  [{finding.Type}] {finding.Group} | {finding.Name}");
                Console.WriteLine($"      → {finding.Reason}");
            }
        }

        Console.WriteLine("\nŞüpheli Kayıtlar:");
        if (report.SuspiciousFindings.Count == 0)
        {
            Console.WriteLine("  Şüpheli tip/grup eşleşmesi bulunmadı.");
        }
        else
        {
            foreach (var finding in report.SuspiciousFindings.Take(30))
            {
                Console.WriteLine($"  [{finding.Severity}] [{finding.Type}] {finding.Group} | {finding.Name}");
                Console.WriteLine($"      → {finding.Reason}");
            }
        }
    }

    static void WritePlaylistAuditHtml(PlaylistAuditReport report, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='tr'><head><meta charset='UTF-8'>");
        sb.AppendLine("<title>Noctra Playlist Audit</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;background:#101114;color:#e8e8ea;margin:0;padding:24px}");
        sb.AppendLine("h1{margin:0 0 8px;color:#f2f2f3}h2{margin-top:28px;color:#8bd3dd}");
        sb.AppendLine(".muted{color:#9ca3af}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin:18px 0}");
        sb.AppendLine(".stat{background:#1b1d24;border:1px solid #303442;border-radius:8px;padding:14px}.num{font-size:24px;font-weight:700;color:#f6c85f}.label{font-size:12px;color:#a6adbb}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin:10px 0 22px}th{background:#242838;color:#8bd3dd;text-align:left}td,th{padding:8px;border-bottom:1px solid #2c3140;vertical-align:top}");
        sb.AppendLine(".sev-High{color:#ff6b6b}.sev-Medium{color:#f6c85f}.sev-Low,.sev-Info{color:#8bd3dd}.reason{color:#c7ccd6}.url{color:#8f98aa;font-size:12px;word-break:break-all}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>Noctra Playlist Audit</h1>");
        sb.AppendLine($"<div class='muted'>{Html(report.Source)} | {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}</div>");

        sb.AppendLine("<div class='grid'>");
        AppendAuditStat(sb, report.Total, "Toplam");
        AppendAuditStat(sb, report.LiveCount, "Live");
        AppendAuditStat(sb, report.VodCount, "VOD");
        AppendAuditStat(sb, report.SeriesCount, "Series");
        AppendAuditStat(sb, report.GroupCount, "Grup");
        AppendAuditStat(sb, report.OthersFindings.Count, "Others");
        AppendAuditStat(sb, report.SuspiciousFindings.Count, "Şüpheli");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>En Büyük Gruplar</h2>");
        sb.AppendLine("<table><tr><th>Grup</th><th>Toplam</th><th>Live</th><th>VOD</th><th>Series</th></tr>");
        foreach (var group in report.TopGroups)
        {
            sb.AppendLine($"<tr><td>{Html(group.Group)}</td><td>{group.Count:N0}</td><td>{group.LiveCount:N0}</td><td>{group.VodCount:N0}</td><td>{group.SeriesCount:N0}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Others Nedenleri</h2>");
        if (report.OthersReasonCounts.Count == 0)
        {
            sb.AppendLine("<p class='muted'>Others bucket'a düşen kayıt yok.</p>");
        }
        else
        {
            sb.AppendLine("<table><tr><th>Neden</th><th>Adet</th></tr>");
            foreach (var item in report.OthersReasonCounts)
            {
                sb.AppendLine($"<tr><td>{Html(item.Key)}</td><td>{item.Value:N0}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        AppendFindingsTable(sb, "Others Örnekleri", report.OthersFindings.Take(100));
        AppendFindingsTable(sb, "Şüpheli Kayıtlar", report.SuspiciousFindings);
        AppendFindingsTable(sb, "Boş/Undefined GroupTitle Örnekleri", report.MissingGroupSamples);

        sb.AppendLine("</body></html>");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static void AppendFindingsTable(StringBuilder sb, string title, IEnumerable<PlaylistAuditFinding> findings)
    {
        var list = findings.ToList();
        sb.AppendLine($"<h2>{Html(title)}</h2>");
        if (list.Count == 0)
        {
            sb.AppendLine("<p class='muted'>Kayıt yok.</p>");
            return;
        }

        sb.AppendLine("<table><tr><th>Seviye</th><th>Tip</th><th>Grup</th><th>Country</th><th>Ad</th><th>Neden</th><th>URL</th></tr>");
        foreach (var finding in list)
        {
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td class='sev-{Html(finding.Severity)}'>{Html(finding.Severity)}</td>");
            sb.AppendLine($"<td>{finding.Type}</td>");
            sb.AppendLine($"<td>{Html(finding.Group)}</td>");
            sb.AppendLine($"<td>{Html(finding.Country)}</td>");
            sb.AppendLine($"<td>{Html(finding.Name)}</td>");
            sb.AppendLine($"<td class='reason'>{Html(finding.Reason)}</td>");
            sb.AppendLine($"<td class='url'>{Html(finding.Url)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
    }

    static void AppendAuditStat(StringBuilder sb, int value, string label)
    {
        sb.AppendLine($"<div class='stat'><div class='num'>{value:N0}</div><div class='label'>{Html(label)}</div></div>");
    }

    static bool IsHttpSource(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    static string NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? "<empty>" : group.Trim();

    static bool IsUndefinedGroup(string? group) =>
        string.Equals(group?.Trim(), "undefined", StringComparison.OrdinalIgnoreCase);

    static bool IsOthersGroup(Channel channel)
    {
        var group = NormalizeGroup(channel.GroupTitle);
        return group.Equals("Live / Others", StringComparison.OrdinalIgnoreCase) ||
               group.Equals("Movies / Others", StringComparison.OrdinalIgnoreCase) ||
               group.Equals("Series / Others", StringComparison.OrdinalIgnoreCase) ||
               group.EndsWith(" / Others", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsLowInformationGroup(string group)
    {
        if (group == "<empty>")
        {
            return true;
        }

        var normalized = group.Trim();
        return normalized.Length <= 2 ||
               Regex.IsMatch(normalized, @"^(?:tv|live|vod|movie|movies|series|other|others|general|misc|uncategorized)$", RegexOptions.IgnoreCase);
    }

    static bool IsTrustedFallbackGroup(string group)
    {
        var normalized = group.Trim();
        return normalized.Equals("AR", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksMovieLike(string text) =>
        Regex.IsMatch(text, @"\b(movie|movies|film|filme|filmes|cinema|cine|pelicula|peliculas|kino|boxset|box\s*set|vod)\b", RegexOptions.IgnoreCase);

    static bool LooksLinearLiveLike(string text) =>
        Regex.IsMatch(text, @"\b(live|canli|canlı|tv|24/7|7/24|channel|sport|news|haber|deportes|sky|bein|doku|documentary|belgesel)\b", RegexOptions.IgnoreCase);

    static bool LooksAdultLike(string text) =>
        Regex.IsMatch(text, @"\b(adult|adulti|xxx|porn)\b", RegexOptions.IgnoreCase);

    static bool HasExplicitProviderTypeSignal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var lowerUrl = url.ToLowerInvariant();
        return lowerUrl.Contains("/live/") ||
               lowerUrl.Contains("/movie/") ||
               lowerUrl.Contains("/movies/") ||
               lowerUrl.Contains("/vod/") ||
               lowerUrl.Contains("/series/") ||
               lowerUrl.Contains("/serie/") ||
               lowerUrl.Contains("type=live") ||
               lowerUrl.Contains("type=movie") ||
               lowerUrl.Contains("type=vod") ||
               lowerUrl.Contains("type=series");
    }

    static bool HasQualityPrefix(string name) =>
        Regex.IsMatch(name.Trim(), @"^(?:4k|uhd|2160p|1080p|720p|576p|480p|fhd|hd|sd|hevc|raw|h265|h\.?265|x265)\s*:", RegexOptions.IgnoreCase);

    static bool LooksNumericProxyUrl(string url)
    {
        var path = url;
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        var slashIndex = path.LastIndexOf('/');
        return slashIndex >= 0 &&
               slashIndex < path.Length - 1 &&
               path[(slashIndex + 1)..].All(char.IsDigit);
    }

    static bool LooksBareNumericChannelName(string name)
    {
        var candidate = name;
        var delimiterIndex = candidate.IndexOf(':');
        if (delimiterIndex > 0 && delimiterIndex < candidate.Length - 1)
        {
            candidate = candidate[(delimiterIndex + 1)..].Trim();
        }

        return Regex.IsMatch(candidate.Trim(), @"^\d{1,3}\s*x\s*\d{1,3}$", RegexOptions.IgnoreCase);
    }

    static string Html(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
}

class PlaylistAuditReport
{
    public string Source { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public int Total { get; set; }
    public int LiveCount { get; set; }
    public int VodCount { get; set; }
    public int SeriesCount { get; set; }
    public int GroupCount { get; set; }
    public List<PlaylistAuditGroupSummary> TopGroups { get; set; } = new();
    public List<PlaylistAuditFinding> OthersFindings { get; set; } = new();
    public Dictionary<string, int> OthersReasonCounts { get; set; } = new();
    public List<PlaylistAuditFinding> SuspiciousFindings { get; set; } = new();
    public List<PlaylistAuditFinding> MissingGroupSamples { get; set; } = new();
}

class PlaylistAuditGroupSummary
{
    public string Group { get; set; } = "";
    public int Count { get; set; }
    public int LiveCount { get; set; }
    public int VodCount { get; set; }
    public int SeriesCount { get; set; }
}

class PlaylistAuditFinding
{
    public string Severity { get; set; } = "";
    public string Reason { get; set; } = "";
    public ChannelType Type { get; set; }
    public string Group { get; set; } = "";
    public string Country { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}
