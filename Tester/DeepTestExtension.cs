// =============================================================================
// DeepTestExtension.cs
//
// Mevcut NoctraProviderTester/Program.cs'e eklenecek ek metodlar.
//
// KULLANIM:
//   --deep-test                           → Varsayılan: 5 Live + 5 VOD + 3 Dizi
//   --deep-test --live 3 --vod 3          → 3'er kanal
//   --deep-test --duration 20             → Her kanal 20s probe
//   --deep-test --seed 42                 → Tekrarlanabilir örneklem
//   --deep-test --group "ULUSAL"          → Sadece bu gruptan örnekle
//
//   Toplu modda da çalışır:
//   --batch providers.json --deep-test
// =============================================================================

using Noctra.Diagnostics;
using System.Text;
using System.Text.Json;

// ─────────────────────────────────────────────────────────────────────────────
// Bu extension metodları NoctraProviderTester sınıfına eklenecek
// ─────────────────────────────────────────────────────────────────────────────

partial class NoctraProviderTester
{
    // ── --deep-test argümanlarını parse et ───────────────────────────────────

    DeepSamplingConfig ParseDeepTestArgs(string[] args)
    {
        var config = new DeepSamplingConfig();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--live":     config.LiveSampleCount   = int.Parse(args[++i]); break;
                case "--vod":      config.VodSampleCount    = int.Parse(args[++i]); break;
                case "--series":   config.SeriesSampleCount = int.Parse(args[++i]); break;
                case "--duration": config.ProbeDurationSeconds = int.Parse(args[++i]); break;
                case "--seed":     config.RandomSeed        = int.Parse(args[++i]); break;
                case "--group":    config.FilterGroup       = args[++i]; break;
            }
        }
        return config;
    }

    // ── Deep test runner (TestResult içindeki kanallarla çalışır) ────────────

    async Task<DeepSamplingResult> RunDeepTestAsync(
        List<ParsedChannel> channels,
        DeepSamplingConfig samplingConfig,
        StreamProbeConfig? probeConfig = null)
    {
        probeConfig ??= new StreamProbeConfig
        {
            ProbeDurationSeconds = samplingConfig.ProbeDurationSeconds,
            NetworkCachingMs     = 4000,
            MeasureHttpLatency   = true,
            ProbeAudio           = false
        };

        // ParsedChannel → tuple dönüşümü
        var tuples = channels
            .Where(c => c.StreamUrl != null)
            .Select(c => (
                Name:  c.Name ?? "Bilinmiyor",
                Url:   c.StreamUrl!,
                Type:  c.Type switch
                {
                    ChannelTypeEnum.Live   => "Live",
                    ChannelTypeEnum.VOD    => "VOD",
                    ChannelTypeEnum.Series => "Series",
                    _                      => "Live"
                },
                Group: c.GroupTitle ?? ""))
            .ToList();

        Console.WriteLine($"\n🔬 DEEP STREAM TEST başlıyor...");
        Console.WriteLine($"   Örneklem: {samplingConfig.LiveSampleCount} Live, " +
                          $"{samplingConfig.VodSampleCount} VOD, " +
                          $"{samplingConfig.SeriesSampleCount} Dizi");
        Console.WriteLine($"   Probe süresi: {samplingConfig.ProbeDurationSeconds}s/kanal\n");

        return await DeepSampler.RunAsync(
            tuples,
            samplingConfig,
            probeConfig,
            msg => Console.WriteLine(msg));
    }

    // ── Konsol raporu ────────────────────────────────────────────────────────

    void PrintDeepTestReport(DeepSamplingResult deep)
    {
        Console.WriteLine("\n" + new string('═', 70));
        Console.WriteLine("  🔬 DEEP STREAM TEST RAPORU");
        Console.WriteLine(new string('═', 70));

        Console.WriteLine($"\n📊 ÖZET");
        Console.WriteLine($"   Toplam test  : {deep.TotalCount}");
        Console.WriteLine($"   Başarılı     : {deep.SuccessCount}/{deep.TotalCount}");
        Console.WriteLine($"   Ort. Skor    : {deep.AvgQualityScore:F0}/100");
        Console.WriteLine($"   Toplam donma : {deep.TotalFreezeCount}x");
        Console.WriteLine($"   Ort. Bitrate : {StreamAnalyzer.FormatBitrate(deep.AvgBitrateKbps)}");

        if (deep.LiveProbes.Count > 0)
        {
            Console.WriteLine($"\n📡 CANLI TV ({deep.LiveProbes.Count} test)");
            Console.WriteLine($"   Ort. açılış  : {deep.AvgLiveTtffMs:F0}ms");
            PrintProbeTable(deep.LiveProbes);
        }

        if (deep.VodProbes.Count > 0)
        {
            Console.WriteLine($"\n🎬 VOD/FİLM ({deep.VodProbes.Count} test)");
            Console.WriteLine($"   Ort. açılış  : {deep.AvgVodTtffMs:F0}ms");
            PrintProbeTable(deep.VodProbes);
        }

        if (deep.SeriesProbes.Count > 0)
        {
            Console.WriteLine($"\n📺 DİZİ ({deep.SeriesProbes.Count} test)");
            PrintProbeTable(deep.SeriesProbes);
        }

        // Kritik sorunları öne çıkar
        var problemChannels = deep.AllProbes
            .Where(r => !r.Success || r.FreezCount > 1 || r.FrameDropRate > 0.1 || r.DemuxCorrupted > 20)
            .ToList();

        if (problemChannels.Count > 0)
        {
            Console.WriteLine($"\n⚠️  KRİTİK SORUNLU KANALLAR ({problemChannels.Count})");
            foreach (var r in problemChannels)
            {
                Console.WriteLine($"   ❌ {r.ChannelName}");
                Console.WriteLine($"      {r.QualityLabel}");
                if (!r.Success) Console.WriteLine($"      Hata: {r.ErrorMessage}");
            }
        }

        Console.WriteLine(new string('═', 70));
    }

    void PrintProbeTable(List<StreamProbeResult> probes)
    {
        Console.WriteLine($"\n   {"Kanal",-32} {"TTFF",-8} {"Donma",-8} {"Drop%",-8} {"Bitrate",-12} {"Çöz.",-12} {"Skor"}");
        Console.WriteLine($"   {new string('─', 85)}");
        foreach (var r in probes)
        {
            var ttff   = r.TtffMs >= 0 ? $"{r.TtffMs}ms" : "BAŞARISIZ";
            var freeze = r.FreezCount > 0 ? $"⚠️{r.FreezCount}x" : "—";
            var drop   = r.FrameDropRate > 0.001 ? $"⚠️{r.FrameDropRate*100:F1}%" : "—";
            var bitrate = r.AvgBitrateKbps > 0 ? StreamAnalyzer.FormatBitrate(r.AvgBitrateKbps) : "—";
            var res    = r.VideoWidth > 0 ? $"{r.VideoWidth}x{r.VideoHeight}" : "—";
            var scoreEmoji = r.QualityScore >= 80 ? "🟢" : r.QualityScore >= 50 ? "🟡" : "🔴";

            Console.WriteLine($"   {r.ChannelName,-32} {ttff,-8} {freeze,-8} {drop,-8} {bitrate,-12} {res,-12} {scoreEmoji}{r.QualityScore}");
        }
    }

    // ── HTML raporuna deep test bölümü ekle ──────────────────────────────────

    void AppendDeepTestHtml(StringBuilder sb, DeepSamplingResult deep)
    {
        sb.AppendLine("<h2 style='color:#c084fc;margin-top:40px'>🔬 Deep Stream Test</h2>");
        sb.AppendLine("<div class='card'>");

        // Özet stat boxes
        sb.AppendLine("<div style='margin:10px 0'>");
        AppendStat(sb, $"{deep.SuccessCount}/{deep.TotalCount}", "Başarılı");
        AppendStat(sb, $"{deep.AvgQualityScore:F0}/100", "Ort. Skor");
        AppendStat(sb, $"{deep.AvgLiveTtffMs:F0}ms", "Canlı TTFF");
        AppendStat(sb, $"{deep.AvgVodTtffMs:F0}ms", "VOD TTFF");
        AppendStat(sb, $"{deep.TotalFreezeCount}x", "Toplam Donma");
        AppendStat(sb, StreamAnalyzer.FormatBitrate(deep.AvgBitrateKbps), "Ort. Bitrate");
        sb.AppendLine("</div>");

        // Tablo
        sb.AppendLine("<table style='margin-top:15px'>");
        sb.AppendLine("<tr><th>Kanal</th><th>Tip</th><th>TTFF</th><th>HTTP</th><th>Donma</th><th>Frame Drop</th><th>Bitrate</th><th>Çözünürlük</th><th>Codec</th><th>Skor</th></tr>");

        foreach (var r in deep.AllProbes)
        {
            var color = r.QualityScore >= 80 ? "#22c55e" : r.QualityScore >= 50 ? "#f59e0b" : "#ef4444";
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{System.Web.HttpUtility.HtmlEncode(r.ChannelName)}</td>");
            sb.AppendLine($"<td><span style='background:#2d2d4e;padding:2px 6px;border-radius:4px;font-size:11px'>{r.ChannelType}</span></td>");
            sb.AppendLine($"<td>{(r.TtffMs >= 0 ? $"{r.TtffMs}ms" : "<span style='color:#ef4444'>Başarısız</span>")}</td>");
            sb.AppendLine($"<td>{(r.HttpLatencyMs >= 0 ? $"{r.HttpLatencyMs}ms" : "—")}</td>");
            sb.AppendLine($"<td>{(r.FreezCount > 0 ? $"<span style='color:#f59e0b'>{r.FreezCount}x ({r.TotalFreezeMs}ms)</span>" : "—")}</td>");
            sb.AppendLine($"<td>{(r.FrameDropRate > 0.001 ? $"<span style='color:#f59e0b'>%{r.FrameDropRate*100:F1}</span>" : "—")}</td>");
            sb.AppendLine($"<td>{(r.AvgBitrateKbps > 0 ? StreamAnalyzer.FormatBitrate(r.AvgBitrateKbps) : "—")}</td>");
            sb.AppendLine($"<td>{(r.VideoWidth > 0 ? $"{r.VideoWidth}x{r.VideoHeight} @{r.Fps}fps" : "—")}</td>");
            sb.AppendLine($"<td style='font-size:11px'>{r.VideoCodec} {r.AudioCodec}</td>");
            sb.AppendLine($"<td><strong style='color:{color}'>{r.QualityScore}</strong></td>");
            sb.AppendLine("</tr>");

            // Hata satırı
            if (!r.Success && !string.IsNullOrWhiteSpace(r.ErrorMessage))
                sb.AppendLine($"<tr><td colspan='10' style='color:#ef4444;font-size:12px;padding-left:20px'>{System.Web.HttpUtility.HtmlEncode(r.ErrorMessage)}</td></tr>");
        }

        sb.AppendLine("</table>");

        // Bitrate sparkline (basit metin tabanlı)
        foreach (var r in deep.AllProbes.Where(r => r.BitrateSamples.Count > 3))
        {
            sb.AppendLine($"<p style='font-size:12px;color:#888;margin-top:8px'>");
            sb.AppendLine($"  <strong style='color:#a855f7'>{System.Web.HttpUtility.HtmlEncode(r.ChannelName)}</strong> bitrate: ");
            sb.AppendLine($"  min={StreamAnalyzer.FormatBitrate(r.MinBitrateKbps)}, ");
            sb.AppendLine($"  avg={StreamAnalyzer.FormatBitrate(r.AvgBitrateKbps)}, ");
            sb.AppendLine($"  peak={StreamAnalyzer.FormatBitrate(r.PeakBitrateKbps)}, ");
            sb.AppendLine($"  kararlılık={(1 - r.BitrateStdDevRatio) * 100:F0}%");
            sb.AppendLine("</p>");
        }

        sb.AppendLine("</div>");
    }
}
