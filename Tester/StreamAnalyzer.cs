// =============================================================================
// StreamAnalyzer.cs — LibVLCSharp tabanlı headless stream kalite ölçüm motoru
//
// TestParser projesine eklemek için:
//   1. Bu dosyayı TestParser/ klasörüne kopyala
//   2. LibVLCSharp.Full zaten projende mevcut (Noctra.Core referansından)
//
// Ölçülen metrikler:
//   TTFF            : Time To First Frame (Play → ilk Playing eventi, ms)
//   TTFC            : Time To First Content (Play → Buffer %100, ms)
//   Donma           : Buffering event sayısı + toplam donma süresi (ms)
//   LostPictures    : VLC atlanan kare sayısı
//   DemuxCorrupted  : Bozuk demux paketi sayısı
//   DemuxDiscontinuity : Stream kesintisi sayısı (HLS/TS sıçramaları)
//   Bitrate         : DemuxBitrate polling → ortalama/peak/min/StdDev
//   TotalBytesRead  : Gerçek indirilen byte miktarı
//   Çözünürlük/FPS/Codec: Track bilgisi
//   HttpLatencyMs   : VLC'den bağımsız HTTP HEAD gecikme ölçümü
// =============================================================================

using LibVLCSharp.Shared;
using Noctra.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Diagnostics
{
    public record StreamProbeConfig
    {
        public int ProbeDurationSeconds { get; init; } = 15;
        public int TtffTimeoutSeconds   { get; init; } = 20;
        public int StatsIntervalMs      { get; init; } = 500;
        public int NetworkCachingMs     { get; init; } = 4000;
        public bool ProbeAudio          { get; init; } = false;
        public bool MeasureHttpLatency  { get; init; } = true;
    }

    public class StreamProbeResult
    {
        public string  Url            { get; set; } = "";
        public string  ChannelName    { get; set; } = "";
        public string  ChannelType    { get; set; } = "";
        public bool    Success        { get; set; }
        public string? ErrorMessage   { get; set; }
        public int     ProbeDurationSeconds { get; set; }

        // Bağlantı
        public int HttpLatencyMs  { get; set; } = -1;
        public int HttpStatusCode { get; set; }

        // Açılma hızı
        public int TtffMs { get; set; } = -1;
        public int TtfcMs { get; set; } = -1;

        // Buffer / donma
        public int   FreezCount      { get; set; }
        public int   TotalFreezeMs   { get; set; }
        public double AvgBufferLevel  { get; set; }
        public float MinBufferLevel  { get; set; } = 100f;

        // Frame/paket kalitesi
        public long LostPictures       { get; set; }
        public long DecodedFrames      { get; set; }
        public long DisplayedFrames    { get; set; }
        public long DemuxCorrupted     { get; set; }
        public long DemuxDiscontinuity { get; set; }
        public double FrameDropRate =>
            DecodedFrames + LostPictures > 0
                ? (double)LostPictures / (DecodedFrames + LostPictures)
                : 0;

        // Bitrate
        public double AvgBitrateKbps     { get; set; }
        public double PeakBitrateKbps    { get; set; }
        public double MinBitrateKbps     { get; set; }
        public long   TotalBytesRead     { get; set; }
        public double BitrateStdDevRatio { get; set; }

        // Video kalitesi
        public int    VideoWidth    { get; set; }
        public int    VideoHeight   { get; set; }
        public int    Fps           { get; set; }
        public string VideoCodec    { get; set; } = "";
        public string AudioCodec    { get; set; } = "";
        public int    AudioChannels { get; set; }

        // Skor
        public int    QualityScore { get; set; }
        public string QualityLabel { get; set; } = "";

        // Ham veri (grafik için)
        public List<float>  BufferSamples  { get; set; } = new();
        public List<double> BitrateSamples { get; set; } = new();
    }

    public sealed class StreamAnalyzer : IDisposable
    {
        private static LibVLC?              _sharedLibVLC;
        private static readonly object     _initLock = new();
        private static readonly SemaphoreSlim _vlcLock  = new(1, 1);

        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AllowAutoRedirect = true
        }) { Timeout = TimeSpan.FromSeconds(10) };

        static StreamAnalyzer()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        private static LibVLC GetOrCreateLibVLC()
        {
            lock (_initLock)
            {
                if (_sharedLibVLC != null) return _sharedLibVLC;
                _sharedLibVLC = new LibVLC(
                    "--vout=dummy",
                    "--aout=dummy",
                    "--no-video-title-show",
                    "--no-osd",
                    "--quiet",
                    "--clock-jitter=0",
                    "--clock-synchro=0"
                );
                return _sharedLibVLC;
            }
        }

        // ── Ana probe metodu ──────────────────────────────────────────────

        public static async Task<StreamProbeResult> ProbeAsync(
            Channel channel,
            StreamProbeConfig? config = null)
        {
            config ??= new StreamProbeConfig();
            var result = new StreamProbeResult
            {
                Url               = channel.StreamUrl ?? "",
                ChannelName       = channel.Name ?? "Bilinmiyor",
                ChannelType       = channel.Type.ToString(),
                ProbeDurationSeconds = config.ProbeDurationSeconds
            };

            await _vlcLock.WaitAsync();
            try
            {
                if (config.MeasureHttpLatency)
                    await MeasureHttpLatencyAsync(channel.StreamUrl ?? "", result);

                await RunVlcProbeAsync(channel.StreamUrl ?? "", config, result);

                result.QualityScore = CalculateQualityScore(result);
                result.QualityLabel = BuildQualityLabel(result);
            }
            catch (Exception ex)
            {
                result.Success      = false;
                result.ErrorMessage = ex.Message;
                result.QualityScore = 0;
                result.QualityLabel = $"Hata: {ex.Message}";
            }
            finally
            {
                _vlcLock.Release();
            }
            return result;
        }

        // ── HTTP gecikme ölçümü ───────────────────────────────────────────

        private static async Task MeasureHttpLatencyAsync(string url, StreamProbeResult result)
        {
            try
            {
                var sw  = Stopwatch.StartNew();
                var req = new HttpRequestMessage(HttpMethod.Head, url);
                var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                sw.Stop();
                result.HttpLatencyMs  = (int)sw.ElapsedMilliseconds;
                result.HttpStatusCode = (int)res.StatusCode;

                if (!res.IsSuccessStatusCode)
                {
                    var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                    sw.Restart();
                    var getRes = await _http.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
                    sw.Stop();
                    result.HttpLatencyMs  = (int)sw.ElapsedMilliseconds;
                    result.HttpStatusCode = (int)getRes.StatusCode;
                }
            }
            catch (Exception ex)
            {
                result.HttpLatencyMs  = -1;
                result.ErrorMessage   = $"HTTP: {ex.Message}";
            }
        }

        // ── VLC headless probe ────────────────────────────────────────────

        private static async Task RunVlcProbeAsync(
            string url, StreamProbeConfig config, StreamProbeResult result)
        {
            var libVLC = GetOrCreateLibVLC();
            var media  = new Media(libVLC, new Uri(url));
            media.AddOption($":network-caching={config.NetworkCachingMs}");
            media.AddOption(":http-reconnect=true");
            media.AddOption(":live-caching=3000");
            if (!config.ProbeAudio) media.AddOption(":no-audio");

            var ttffSw    = Stopwatch.StartNew();
            var ttffTcs   = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ttfcTcs   = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            int   freezeCount  = 0;
            long  freezeStart  = 0;
            long  totalFreeze  = 0;
            bool  isFreezing   = false;
            float minBuf       = 100f;
            var   bufSamples   = new List<float>();
            var   bwSamples    = new List<double>();
            double bwSum       = 0;
            int   bwCount      = 0;

            using var player = new MediaPlayer(media);
            player.Volume = 0;

            player.Playing  += (_, _) => ttffTcs.TrySetResult((int)ttffSw.ElapsedMilliseconds);
            player.EncounteredError += (_, _) =>
            {
                ttffTcs.TrySetException(new Exception("VLC oynatma hatası"));
                ttfcTcs.TrySetException(new Exception("VLC oynatma hatası"));
            };

            player.Buffering += (_, e) =>
            {
                var now = ttffSw.ElapsedMilliseconds;
                bufSamples.Add(e.Cache);
                if (e.Cache < minBuf) minBuf = e.Cache;

                if (e.Cache >= 99.9f)
                {
                    ttfcTcs.TrySetResult((int)now);
                    if (isFreezing && freezeStart > 0)
                    {
                        totalFreeze += now - freezeStart;
                        isFreezing   = false;
                    }
                }
                else if (e.Cache < 30f && !isFreezing)
                {
                    isFreezing  = true;
                    freezeStart = now;
                    freezeCount++;
                }
            };

            ttffSw.Restart();
            player.Play(media);

            var ttffTimeout = Task.Delay(TimeSpan.FromSeconds(config.TtffTimeoutSeconds));
            var winner      = await Task.WhenAny(ttffTcs.Task, ttffTimeout);

            if (winner == ttffTimeout || !ttffTcs.Task.IsCompletedSuccessfully)
            {
                player.Stop();
                result.Success      = false;
                result.ErrorMessage ??= $"Stream {config.TtffTimeoutSeconds}s içinde açılmadı";
                return;
            }

            result.TtffMs  = ttffTcs.Task.Result;
            result.Success = true;

            await Task.WhenAny(ttfcTcs.Task, Task.Delay(5000));
            result.TtfcMs  = ttfcTcs.Task.IsCompletedSuccessfully ? ttfcTcs.Task.Result : result.TtffMs;

            // Stats polling
            var probeEnd = DateTime.UtcNow.AddSeconds(config.ProbeDurationSeconds);
            while (DateTime.UtcNow < probeEnd && player.IsPlaying)
            {
                await Task.Delay(config.StatsIntervalMs);
                try
                {
                    var stats  = media.Statistics;
                    var bitrate = stats.DemuxBitrate * 8.0 / 1000.0; // bytes/s → kbps
                    if (bitrate > 0)
                    {
                        bwSamples.Add(bitrate);
                        bwSum  += bitrate;
                        bwCount++;
                        if (bitrate > result.PeakBitrateKbps) result.PeakBitrateKbps = bitrate;
                        if (result.MinBitrateKbps == 0 || bitrate < result.MinBitrateKbps)
                            result.MinBitrateKbps = bitrate;
                    }

                    result.LostPictures       = Math.Max(result.LostPictures,       stats.LostPictures);
                    result.DecodedFrames      = stats.DecodedVideo;
                    result.DisplayedFrames    = stats.DisplayedPictures;
                    result.DemuxCorrupted     = Math.Max(result.DemuxCorrupted,     stats.DemuxCorrupted);
                    result.DemuxDiscontinuity = Math.Max(result.DemuxDiscontinuity, stats.DemuxDiscontinuity);
                    result.TotalBytesRead     = stats.DemuxReadBytes;

                    if (result.VideoWidth == 0)
                    {
                        uint w = 0, h = 0;
                        player.Size(0, ref w, ref h);
                        if (w > 0) { result.VideoWidth = (int)w; result.VideoHeight = (int)h; }
                        var fps = player.Fps;
                        if (fps > 0) result.Fps = (int)Math.Round(fps);

                        if (player.Media?.Tracks != null)
                        {
                            foreach (var track in player.Media.Tracks)
                            {
                                if (track.TrackType == TrackType.Video && result.VideoCodec == "")
                                    result.VideoCodec = FourCC(track.Codec);
                                else if (track.TrackType == TrackType.Audio)
                                {
                                    if (result.AudioCodec == "") result.AudioCodec = FourCC(track.Codec);
                                    result.AudioChannels = (int)track.Data.Audio.Channels;
                                }
                            }
                        }
                    }
                }
                catch { /* stats yok, devam */ }
            }

            player.Stop();
            media.Dispose();

            result.FreezCount      = freezeCount;
            result.TotalFreezeMs   = (int)totalFreeze;
            result.BufferSamples   = bufSamples;
            result.BitrateSamples  = bwSamples;
            result.AvgBufferLevel  = bufSamples.Count > 0 ? bufSamples.Average() : 100;
            result.MinBufferLevel  = minBuf;
            result.AvgBitrateKbps  = bwCount > 0 ? bwSum / bwCount : 0;

            if (bwSamples.Count > 1)
            {
                var mean     = result.AvgBitrateKbps;
                var variance = bwSamples.Sum(b => Math.Pow(b - mean, 2)) / bwSamples.Count;
                result.BitrateStdDevRatio = mean > 0 ? Math.Sqrt(variance) / mean : 0;
            }
        }

        // ── Kalite skoru (0-100) ──────────────────────────────────────────

        private static int CalculateQualityScore(StreamProbeResult r)
        {
            if (!r.Success) return 0;
            double s = 100;

            // TTFF
            if      (r.TtffMs > 10000) s -= 25;
            else if (r.TtffMs > 5000)  s -= 15;
            else if (r.TtffMs > 3000)  s -= 8;
            else if (r.TtffMs > 1500)  s -= 3;

            // Donma
            s -= r.FreezCount * 10;
            if      (r.TotalFreezeMs > 5000) s -= 20;
            else if (r.TotalFreezeMs > 2000) s -= 10;
            else if (r.TotalFreezeMs > 500)  s -= 5;

            // Frame drop
            if      (r.FrameDropRate > 0.20) s -= 25;
            else if (r.FrameDropRate > 0.10) s -= 15;
            else if (r.FrameDropRate > 0.05) s -= 8;
            else if (r.FrameDropRate > 0.01) s -= 3;

            // Bozuk paket
            if      (r.DemuxCorrupted > 100) s -= 15;
            else if (r.DemuxCorrupted > 20)  s -= 8;
            else if (r.DemuxCorrupted > 5)   s -= 3;

            // Süreksizlik
            if      (r.DemuxDiscontinuity > 10) s -= 10;
            else if (r.DemuxDiscontinuity > 3)  s -= 5;

            // Bitrate stabilitesi
            if      (r.BitrateStdDevRatio > 0.5) s -= 10;
            else if (r.BitrateStdDevRatio > 0.3) s -= 5;

            // HTTP gecikme
            if (r.HttpLatencyMs > 0)
            {
                if      (r.HttpLatencyMs > 3000) s -= 10;
                else if (r.HttpLatencyMs > 1000) s -= 5;
                else if (r.HttpLatencyMs < 200)  s += 5;
            }

            return Math.Max(0, Math.Min(100, (int)s));
        }

        private static string BuildQualityLabel(StreamProbeResult r)
        {
            if (!r.Success) return r.ErrorMessage ?? "Başarısız";
            var p = new List<string>();
            if (r.TtffMs >= 0)            p.Add($"Açılış:{r.TtffMs}ms");
            if (r.FreezCount > 0)         p.Add($"⚠️{r.FreezCount}x donma({r.TotalFreezeMs}ms)");
            if (r.FrameDropRate > 0.01)   p.Add($"⚠️%{r.FrameDropRate*100:F1}drop");
            if (r.DemuxCorrupted > 5)     p.Add($"⚠️{r.DemuxCorrupted}bozuk");
            if (r.AvgBitrateKbps > 0)     p.Add(FormatBitrate(r.AvgBitrateKbps));
            if (r.VideoWidth > 0)         p.Add($"{r.VideoWidth}x{r.VideoHeight}@{r.Fps}fps");
            if (p.Count == 0)             p.Add(r.QualityScore >= 80 ? "Mükemmel" : "Sorunsuz");
            return string.Join(" | ", p);
        }

        public static string FormatBitrate(double kbps)
            => kbps >= 1000 ? $"{kbps/1000:F1}Mbps" : $"{kbps:F0}kbps";

        private static string FourCC(uint fourcc)
        {
            if (fourcc == 0) return "";
            var b = new byte[]
            {
                (byte)(fourcc & 0xFF), (byte)((fourcc >> 8) & 0xFF),
                (byte)((fourcc >> 16) & 0xFF), (byte)((fourcc >> 24) & 0xFF)
            };
            return Encoding.ASCII.GetString(b).Trim('\0', ' ');
        }

        public void Dispose() { }

        public static void Shutdown()
        {
            lock (_initLock)
            {
                _sharedLibVLC?.Dispose();
                _sharedLibVLC = null;
            }
        }
    }

    // ── Toplu örneklem testi ──────────────────────────────────────────────────

    public class DeepSamplingConfig
    {
        public int    LiveSampleCount   { get; set; } = 5;
        public int    VodSampleCount    { get; set; } = 5;
        public int    SeriesSampleCount { get; set; } = 3;
        public int    ProbeDurationSeconds { get; set; } = 15;
        public int?   RandomSeed        { get; set; }
        public string? FilterGroup      { get; set; }
    }

    public class DeepSamplingResult
    {
        public List<StreamProbeResult> LiveProbes   { get; set; } = new();
        public List<StreamProbeResult> VodProbes    { get; set; } = new();
        public List<StreamProbeResult> SeriesProbes { get; set; } = new();

        public IEnumerable<StreamProbeResult> AllProbes =>
            LiveProbes.Concat(VodProbes).Concat(SeriesProbes);

        public double AvgLiveTtffMs => Avg(LiveProbes.Select(r => r.TtffMs).Where(t => t >= 0).Select(t => (double)t));
        public double AvgVodTtffMs  => Avg(VodProbes.Select(r => r.TtffMs).Where(t => t >= 0).Select(t => (double)t));
        public double AvgBitrateKbps => Avg(AllProbes.Where(r => r.AvgBitrateKbps > 0).Select(r => r.AvgBitrateKbps));
        public int    TotalFreezeCount => AllProbes.Sum(r => r.FreezCount);
        public double AvgQualityScore  => Avg(AllProbes.Where(r => r.Success).Select(r => (double)r.QualityScore));
        public int    SuccessCount => AllProbes.Count(r => r.Success);
        public int    TotalCount   => AllProbes.Count();

        private static double Avg(IEnumerable<double> seq)
        {
            var list = seq.ToList();
            return list.Count > 0 ? list.Average() : -1;
        }
    }

    public static class DeepSampler
    {
        public static async Task<DeepSamplingResult> RunAsync(
            IList<Channel> channels,
            DeepSamplingConfig?  samplingConfig  = null,
            StreamProbeConfig?   probeConfig     = null,
            Action<string>?      progressCallback = null)
        {
            samplingConfig ??= new DeepSamplingConfig();
            probeConfig    ??= new StreamProbeConfig { ProbeDurationSeconds = samplingConfig.ProbeDurationSeconds };

            var rng = samplingConfig.RandomSeed.HasValue
                ? new Random(samplingConfig.RandomSeed.Value)
                : new Random();

            var result = new DeepSamplingResult();

            var live   = Sample(channels.Where(c => c.Type == ChannelType.Live   && IsHttp(c.StreamUrl)).ToList(), samplingConfig.LiveSampleCount, rng);
            var vod    = Sample(channels.Where(c => c.Type == ChannelType.VOD    && IsHttp(c.StreamUrl)).ToList(), samplingConfig.VodSampleCount, rng);
            var series = Sample(channels.Where(c => c.Type == ChannelType.Series && IsHttp(c.StreamUrl)).ToList(), samplingConfig.SeriesSampleCount, rng);

            var all = live.Concat(vod).Concat(series).ToList();

            int i = 0;
            foreach (var ch in all)
            {
                i++;
                var type = ch.Type.ToString();
                progressCallback?.Invoke($"  [{i}/{all.Count}] {type}: {ch.Name}");
                var probe = await StreamAnalyzer.ProbeAsync(ch, probeConfig);

                switch (type)
                {
                    case "Live":   result.LiveProbes.Add(probe);   break;
                    case "VOD":    result.VodProbes.Add(probe);    break;
                    case "Series": result.SeriesProbes.Add(probe); break;
                }

                var icon = probe.Success ? (probe.QualityScore >= 75 ? "✅" : "⚠️") : "❌";
                progressCallback?.Invoke(
                    $"     {icon} TTFF:{(probe.TtffMs >= 0 ? $"{probe.TtffMs}ms" : "—")} " +
                    $"Donma:{probe.FreezCount}x " +
                    $"Bitrate:{StreamAnalyzer.FormatBitrate(probe.AvgBitrateKbps)} " +
                    $"Skor:{probe.QualityScore}/100");
            }

            return result;
        }

        private static List<T> Sample<T>(List<T> src, int n, Random rng)
            => src.Count <= n ? src : src.OrderBy(_ => rng.Next()).Take(n).ToList();

        private static bool IsHttp(string url) =>
            url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true ||
            url?.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase) == true;
    }
}
