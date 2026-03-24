using System.IO.Compression;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// EPG (Electronic Program Guide) servisi
/// </summary>
public class EpgService : IEpgService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<EpgService>? _logger;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    
    public bool IsLoaded { get; private set; }
    public DateTime? LastUpdated { get; private set; }
    public string? LastError { get; private set; }

    public EpgService(IDbContextFactory<AppDbContext> contextFactory, HttpClient httpClient, ISettingsService settingsService, ILogger<EpgService>? logger = null)
    {
        _contextFactory = contextFactory;
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
    }

    public void ClearLastError()
    {
        LastError = null;
    }

    public async Task<int> LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null, int daysAhead = 1, IProgress<EpgProgressInfo>? progress = null, bool clearBeforeSave = false)
    {
        if (!await _loadSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            _logger?.LogWarning("Another EPG load is in progress, skipping new request.");
            return -1; // -1 indicates it was skipped due to concurrency
        }

        // Check if EPG is enabled globally
        if (!_settingsService.Settings.EpgEnabled)
        {
            _logger?.LogInformation("EPG is disabled in settings, skipping load.");
            return 0;
        }

        int totalLoaded = 0;
        bool hasCleared = false;
        try
        {
            LastError = null; // Clear previous error
            if (string.IsNullOrEmpty(epgUrl)) return 0;

            progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Downloading, Message = "EPG dosyası indiriliyor...", ProgressPercent = 5 });

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var response = await NetworkRetry.ExecuteAsync(
                () => _httpClient.GetAsync(epgUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token),
                cancellationToken: cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);

            // GZip decompression support (.gz URLs)
            await using Stream dataStream = epgUrl.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                response.Content.Headers.ContentEncoding.Contains("gzip")
                ? new GZipStream(stream, CompressionMode.Decompress)
                : stream;

            if (dataStream is GZipStream)
            {
                progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Decompressing, Message = "Sıkıştırılmış dosya açılıyor...", ProgressPercent = 15 });
            }

            progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Parsing, Message = "EPG içeriği analiz ediliyor...", ProgressPercent = 20 });

            var settings = new System.Xml.XmlReaderSettings 
            { 
                Async = true, 
                DtdProcessing = System.Xml.DtdProcessing.Ignore 
            };
            using var reader = System.Xml.XmlReader.Create(dataStream, settings);

            // Build mapping dictionary for EPG (Name -> list of channel Ids)
            var channelMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var xmlChannelIdToDbTvgIds = new Dictionary<string, List<string>>();
            var tvgIdToInternalIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var allowedPrimaryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (channelsForMapping != null)
            {
                progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Matching, Message = "Kanallar eşleştiriliyor...", ProgressPercent = 25 });
                foreach (var channel in channelsForMapping)
                {
                    var idStr = channel.Id.ToString();
                    if (!string.IsNullOrWhiteSpace(channel.TvgId))
                    {
                        allowedPrimaryIds.Add(channel.TvgId!);
                        if (!tvgIdToInternalIds.TryGetValue(channel.TvgId!, out var list))
                        {
                            list = new List<string>();
                            tvgIdToInternalIds[channel.TvgId!] = list;
                        }
                        if (!list.Contains(idStr)) list.Add(idStr);
                    }

                    allowedPrimaryIds.Add(idStr);

                    foreach (var variant in GetNameVariants(channel.Name))
                    {
                        if (string.IsNullOrEmpty(variant)) continue;
                        if (!channelMap.TryGetValue(variant, out var list))
                        {
                            list = new List<string>();
                            channelMap[variant] = list;
                        }
                        if (!list.Contains(idStr)) list.Add(idStr);
                    }
                    if (!string.IsNullOrEmpty(channel.TvgName))
                    {
                        foreach (var variant in GetNameVariants(channel.TvgName))
                        {
                            if (string.IsNullOrEmpty(variant)) continue;
                            if (!channelMap.TryGetValue(variant, out var list))
                            {
                                list = new List<string>();
                                channelMap[variant] = list;
                            }
                            if (!list.Contains(idStr)) list.Add(idStr);
                        }
                    }
                }
            }

            var programs = new List<EpgProgram>();
            var batchSize = 2500; // Slightly larger batch for better performance
            var windowStartUtc = DateTime.UtcNow.Date;
            var windowEndUtc = windowStartUtc.AddDays(Math.Max(1, daysAhead) + 1);

            using var context = await _contextFactory.CreateDbContextAsync();
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element)
                    {
                        if (reader.Name == "channel") 
                        {
                            if (channelMap.Count > 0)
                            {
                                 var xmlId = reader.GetAttribute("id");
                                if (xmlId != null)
                                {
                                    if (tvgIdToInternalIds.TryGetValue(xmlId, out var byTvgIds))
                                    {
                                        if (!xmlChannelIdToDbTvgIds.TryGetValue(xmlId, out var targetList))
                                        {
                                            targetList = new List<string>();
                                            xmlChannelIdToDbTvgIds[xmlId] = targetList;
                                        }
                                        foreach (var id in byTvgIds)
                                            if (!targetList.Contains(id)) targetList.Add(id);

                                        if (isPrimary) allowedPrimaryIds.Add(xmlId);
                                    }

                                    using var subReader = reader.ReadSubtree();
                                    while (await subReader.ReadAsync().ConfigureAwait(false))
                                    {
                                        if (subReader.NodeType == System.Xml.XmlNodeType.Element && subReader.Name == "display-name")
                                        {
                                            var displayName = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                            foreach (var variant in GetNameVariants(displayName))
                                            {
                                                var dbChannelIds = ResolveMappedChannelIds(variant, channelMap);
                                                if (dbChannelIds != null && dbChannelIds.Any())
                                                {
                                                    if (!xmlChannelIdToDbTvgIds.TryGetValue(xmlId, out var targetList))
                                                    {
                                                        targetList = new List<string>();
                                                        xmlChannelIdToDbTvgIds[xmlId] = targetList;
                                                    }
                                                    foreach (var id in dbChannelIds)
                                                        if (!targetList.Contains(id)) targetList.Add(id);
                                                    
                                                    if (isPrimary) allowedPrimaryIds.Add(xmlId);
                                                    break; 
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else if (reader.Name == "programme")
                        {
                            var start = reader.GetAttribute("start");
                            var stop = reader.GetAttribute("stop");
                             var channel = reader.GetAttribute("channel");

                            if (string.IsNullOrEmpty(channel)) continue;

                            var targetIds = new List<string>();
                            if (xmlChannelIdToDbTvgIds.TryGetValue(channel, out var mappedIds))
                            {
                                targetIds.AddRange(mappedIds);
                            }
                            else if (isPrimary)
                            {
                                if (allowedPrimaryIds.Count > 0 && !allowedPrimaryIds.Contains(channel))
                                {
                                    continue;
                                }
                                targetIds.Add(channel);
                            }
                            else
                            {
                                continue;
                            }

                            var startTime = ParseXmlTvDate(start);
                            var endTime = ParseXmlTvDate(stop);

                             if (endTime <= windowStartUtc || startTime >= windowEndUtc)
                                continue;

                            var programTemplate = new EpgProgram
                            {
                                StartTime = startTime,
                                EndTime = endTime
                            };

                            using var subReader = reader.ReadSubtree();
                            while (await subReader.ReadAsync().ConfigureAwait(false))
                            {
                                if (subReader.NodeType == System.Xml.XmlNodeType.Element)
                                {
                                    switch (subReader.Name)
                                    {
                                        case "title":
                                            programTemplate.Title = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                            break;
                                        case "desc":
                                            if (string.IsNullOrWhiteSpace(programTemplate.Description))
                                            {
                                                var desc = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                                if (!string.IsNullOrWhiteSpace(desc))
                                                {
                                                    programTemplate.Description = desc;
                                                }
                                            }
                                            break;
                                    }
                                }
                            }

                            // If this is the FIRST program we are about to save, and clearBeforeSave is true,
                            // we clear the database NOW. This ensures we don't clear if download fails.
                            if (clearBeforeSave && !hasCleared)
                            {
                                await context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms").ConfigureAwait(false);
                                hasCleared = true;
                                _logger?.LogDebug("[EpgService] EPG data cleared just before saving new records (Atomic).");
                            }

                            foreach (var tId in targetIds)
                            {
                                programs.Add(new EpgProgram
                                {
                                    ChannelId = tId,
                                    StartTime = programTemplate.StartTime,
                                    EndTime = programTemplate.EndTime,
                                    Title = programTemplate.Title,
                                    Description = programTemplate.Description
                                });
                                totalLoaded++;
                            }

                            if (programs.Count >= batchSize)
                            {
                                progress?.Report(new EpgProgressInfo 
                                { 
                                    Status = EpgLoadStatus.Saving, 
                                    Message = $"{totalLoaded:N0} program kaydediliyor...", 
                                    ProgressPercent = Math.Min(98, 30 + (totalLoaded / 5000.0 * 5.0)),
                                    LoadedCount = totalLoaded
                                });

                                await context.EpgPrograms.AddRangeAsync(programs).ConfigureAwait(false);
                                await context.SaveChangesAsync().ConfigureAwait(false);
                                programs.Clear();
                            }
                        }
                    }
                }

                if (programs.Any())
                {
                    // Handle case where we have small EPG and never reached batchSize
                    if (clearBeforeSave && !hasCleared)
                    {
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms").ConfigureAwait(false);
                        hasCleared = true;
                    }

                    await context.EpgPrograms.AddRangeAsync(programs).ConfigureAwait(false);
                    await context.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                context.ChangeTracker.AutoDetectChangesEnabled = true;
            }


            IsLoaded = true;
            LastUpdated = DateTime.UtcNow;
            progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Completed, Message = "Tamamlandı", ProgressPercent = 100, LoadedCount = totalLoaded });
            return totalLoaded;
        }
        catch (Exception ex)
        {
            LastError = UserFriendlyErrorMessage.FromException(ex);
            _logger?.LogError(ex, "EPG load failed for URL {EpgUrl}", epgUrl);
            progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Failed, Message = LastError, ProgressPercent = 100 });
            if (isPrimary) throw;
            return 0;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    private static IEnumerable<string> GetNameVariants(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Full normalized name
        var normalizedFull = NormalizeName(name);
        if (normalizedFull.Length >= 3 && seen.Add(normalizedFull))
            yield return normalizedFull;

        // 2. Strip leading country code prefix: "TR - Show TV" → "Show TV"
        if (TryStripLeadingCountryCode(name, out var stripped))
        {
            var v = NormalizeName(stripped);
            if (v.Length >= 3 && seen.Add(v))
                yield return v;
        }

        // 3. Strip parenthesized suffix: "Star TV (TR)" → "Star TV"
        var noParens = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)\s*$", "").Trim();
        if (noParens != name)
        {
            var v = NormalizeName(noParens);
            if (v.Length >= 3 && seen.Add(v))
                yield return v;
        }

        // 4. Strip pipe/slash separators: "TR | Kanal D" → "Kanal D"
        var separators = new[] { '|', '/', '\\' };
        foreach (var sep in separators)
        {
            var idx = name.IndexOf(sep);
            if (idx > 0 && idx < name.Length - 1)
            {
                var after = name[(idx + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(after))
                {
                    var v = NormalizeName(after);
                    if (v.Length >= 3 && seen.Add(v))
                        yield return v;
                }
            }
        }

        // 5. Strip trailing dot-suffix: "KanalD.tr" → "KanalD"
        var dotIdx = name.LastIndexOf('.');
        if (dotIdx > 0)
        {
            var suffix = name[(dotIdx + 1)..];
            if (suffix.Length <= 3 && suffix.All(char.IsLetter))
            {
                var v = NormalizeName(name[..dotIdx]);
                if (v.Length >= 3 && seen.Add(v))
                    yield return v;
            }
        }

        // 6. Strip trailing country names
        var trailingCountries = new[] { "turkey", "turkiye", "türkiye", "tr", "de", "uk", "us", "fr", "it", "es", "nl", "ru" };
        var lowerName = name.Trim().ToLowerInvariant();
        foreach (var country in trailingCountries)
        {
            if (lowerName.EndsWith(" " + country, StringComparison.Ordinal))
            {
                var trimmed = name.Trim()[..^(country.Length + 1)].Trim();
                var v = NormalizeName(trimmed);
                if (v.Length >= 3 && seen.Add(v))
                    yield return v;
            }
        }
    }

    private static bool TryStripLeadingCountryCode(string input, out string stripped)
    {
        stripped = input.Trim();
        if (stripped.Length < 5)
            return false;

        var i = 0;
        while (i < stripped.Length && char.IsLetter(stripped[i]) && i < 3) i++;
        if (i < 2 || i > 3)
            return false;

        // Must look like code token (all upper-ish letters).
        var code = stripped[..i];
        if (!code.All(char.IsLetter))
            return false;

        var j = i;
        while (j < stripped.Length && char.IsWhiteSpace(stripped[j])) j++;
        if (j < stripped.Length && (stripped[j] == '-' || stripped[j] == ':' || stripped[j] == '|' || stripped[j] == '/' || stripped[j] == '\\'))
        {
            j++;
            while (j < stripped.Length && char.IsWhiteSpace(stripped[j])) j++;
        }
        else if (j == i) // no whitespace/separator after code
        {
            return false;
        }

        if (j >= stripped.Length)
            return false;

        stripped = stripped[j..];
        return true;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var s = name.ToLowerInvariant();

        // beIN Sports De-obfuscation (Şifreli isimleri çözme)
        // be*n, b*in, be!n, b.e.i.n, be-in gibi varyasyonları 'bein'e çevir
        if (s.Contains('b') && (s.Contains('i') || s.Contains('n')))
        {
            // Regex: b followed by any non-alphanumeric or digit, then i, n, etc.
            s = System.Text.RegularExpressions.Regex.Replace(s, @"b[e\*!1\.\-\s]*[i\*!1\.\-\s]*n", "bein");
            // Eğer hala 'be n' gibi boşluklu kalmışsa birleştir
            if (s.Contains("be n")) s = s.Replace("be n", "bein");
        }

        s = s.Replace('ı', 'i')
            .Replace('İ', 'i')
            .Replace('ş', 's')
            .Replace('Ş', 's')
            .Replace('ğ', 'g')
            .Replace('Ğ', 'g')
            .Replace('ü', 'u')
            .Replace('Ü', 'u')
            .Replace('ö', 'o')
            .Replace('Ö', 'o')
            .Replace('ç', 'c')
            .Replace('Ç', 'c');

        var chars = new List<char>(s.Length);
        foreach (var ch in s)
        {
            chars.Add(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        var noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hd", "fhd", "uhd", "sd", "hevc", "h265", "h264", "4k", "fullhd", "qhd", "hdr", "10bit", "av1", "raw", "1080i", "720i",
            "feed", "ticari", "50fps", "60fps", "mobie", "mobile", "web", "app", "ios", "android", "iptv", "ts", "m3u8",
            "1080p", "720p", "480p", "2160p", "1080", "720", "576", "live", "vip",
            "backup", "bkp", "multi", "sub", "ace", "plus", "extra",
            "turkey", "turkiye", "türkiye", "tr", "s",
        };

        var tokens = new string(chars.ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !noise.Contains(t));

        return string.Concat(tokens);
    }
    private static List<string>? ResolveMappedChannelIds(string normalizedDisplayName, Dictionary<string, List<string>> channelMap)
    {
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || normalizedDisplayName.Length < 3)
            return null;

        // Fast path: Exact match (O(1))
        if (channelMap.TryGetValue(normalizedDisplayName, out var exactList))
        {
            return exactList;
        }

        // Optimization: Skip fuzzy matching for very short names to avoid false positives.
        if (normalizedDisplayName.Length < 4)
            return null;

        // Dynamic threshold: shorter names need less strict matching
        var maxLen = Math.Max(normalizedDisplayName.Length, 3);
        var threshold = maxLen <= 6 ? 0.70 : maxLen <= 10 ? 0.75 : 0.82;

        List<string>? bestIds = null;
        double bestScore = 0;

        foreach (var kvp in channelMap)
        {
            // Preliminary check: if first characters don't match, similarity is likely low
            if (kvp.Key[0] != normalizedDisplayName[0]) continue;

            var score = Similarity(normalizedDisplayName, kvp.Key);
            if (score > bestScore)
            {
                bestScore = score;
                bestIds = kvp.Value;
            }
            
            // If we found a very high confidence match, stop searching
            if (bestScore > 0.95) break;
        }

        return bestScore >= threshold ? bestIds : null;
    }

    private static double Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        if (a == b) return 1;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            var min = Math.Min(a.Length, b.Length);
            var max = Math.Max(a.Length, b.Length);
            return 0.88 * (double)min / max;
        }

        return BigramDice(a, b);
    }

    private static double BigramDice(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return a == b ? 1 : 0;

        var aBigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < a.Length - 1; i++)
        {
            var bg = a.Substring(i, 2);
            aBigrams[bg] = aBigrams.TryGetValue(bg, out var count) ? count + 1 : 1;
        }

        var intersection = 0;
        var bCount = 0;
        for (var i = 0; i < b.Length - 1; i++)
        {
            var bg = b.Substring(i, 2);
            bCount++;
            if (aBigrams.TryGetValue(bg, out var count) && count > 0)
            {
                intersection++;
                aBigrams[bg] = count - 1;
            }
        }

        var aCount = a.Length - 1;
        return (2.0 * intersection) / (aCount + bCount);
    }
    private void ApplyTimeOffset(EpgProgram? program)
    {
        if (program == null) return;
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        if (offset == TimeSpan.Zero) return;

        program.StartTime = program.StartTime.Add(offset);
        program.EndTime = program.EndTime.Add(offset);
    }

    private void ApplyTimeOffset(IEnumerable<EpgProgram> programs)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        if (offset == TimeSpan.Zero) return;

        foreach (var program in programs)
        {
            program.StartTime = program.StartTime.Add(offset);
            program.EndTime = program.EndTime.Add(offset);
        }
    }

    public async Task<EpgProgram?> GetCurrentProgramAsync(Channel channel)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var now = DateTime.UtcNow.Add(-offset);
        using var context = await _contextFactory.CreateDbContextAsync();

        // Level 1: Try Primary TvgId
        if (!string.IsNullOrEmpty(channel.TvgId))
        {
            var program = await context.EpgPrograms
                .AsNoTracking()
                .Where(p => p.ChannelId == channel.TvgId && p.StartTime <= now && p.EndTime > now)
                .FirstOrDefaultAsync();

            if (program != null) 
            {
                ApplyTimeOffset(program);
                return program;
            }
        }

        // Level 2: Try Internal Id (Secondary EPG mapped by Name)
        var internalId = channel.Id.ToString();
        var programByInternalId = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == internalId && p.StartTime <= now && p.EndTime > now)
            .FirstOrDefaultAsync();

        ApplyTimeOffset(programByInternalId);
        return programByInternalId;
    }

    public async Task<Dictionary<int, EpgProgram?>> GetCurrentProgramsAsync(IEnumerable<Channel> channels)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var now = DateTime.UtcNow.Add(-offset);
        using var context = await _contextFactory.CreateDbContextAsync();

        var liveChannels = channels.Where(c => c.Type == ChannelType.Live).ToList();
        var results = new Dictionary<int, EpgProgram?>();

        if (liveChannels.Count == 0) return results;

        // Collect all possible search IDs (TvgId and ChannelId as string)
        var tvgIds = liveChannels.Select(c => c.TvgId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var internalIds = liveChannels.Select(c => c.Id.ToString()).Distinct().ToList();
        var allSearchIds = tvgIds.Concat(internalIds).Distinct().ToList();

        // Optimized bulk query: fetch all current programs for these IDs in one go
        var matchingPrograms = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => allSearchIds.Contains(p.ChannelId) && p.StartTime <= now && p.EndTime > now)
            .ToListAsync();

        foreach (var channel in liveChannels)
        {
            EpgProgram? program = null;

            // Priority 1: Primary TvgId
            if (!string.IsNullOrEmpty(channel.TvgId))
            {
                program = matchingPrograms.FirstOrDefault(p => p.ChannelId == channel.TvgId);
            }

            // Priority 2: Internal Id mapping
            if (program == null)
            {
                var internalId = channel.Id.ToString();
                program = matchingPrograms.FirstOrDefault(p => p.ChannelId == internalId);
            }

            if (program != null)
            {
                ApplyTimeOffset(program);
            }
            results[channel.Id] = program;
        }

        return results;
    }

    public async Task ClearEpgAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms");
        IsLoaded = false;
    }

    public async Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        // Ensure we compare in UTC if stored in UTC
        var fromUtc = from.ToUniversalTime().Add(-offset);
        var toUtc = to.ToUniversalTime().Add(-offset);

        using var context = await _contextFactory.CreateDbContextAsync();
        var programs = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime >= fromUtc && p.StartTime <= toUtc)
            .OrderBy(p => p.StartTime)
            .ToListAsync();

        ApplyTimeOffset(programs);
        return programs;
    }

    public async Task<List<EpgProgram>> GetUpcomingProgramsAsync(string channelId, int count = 5)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var now = DateTime.UtcNow.Add(-offset);
        using var context = await _contextFactory.CreateDbContextAsync();
        var programs = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime > now)
            .OrderBy(p => p.StartTime)
            .Take(count)
            .ToListAsync();

        ApplyTimeOffset(programs);
        return programs;
    }

    public async Task<List<EpgProgram>> GetTodayProgramsAsync(string channelId)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var todayStart = DateTime.UtcNow.Date.Add(-offset);
        var todayEnd = todayStart.AddDays(1);
        using var context = await _contextFactory.CreateDbContextAsync();
        var programs = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime >= todayStart && p.StartTime < todayEnd)
            .OrderBy(p => p.StartTime)
            .ToListAsync();

        ApplyTimeOffset(programs);
        return programs;
    }

    public async Task<int> GetTotalProgramCountAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpgPrograms.CountAsync();
    }

    public async Task<int> GetDistinctChannelCountAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpgPrograms
            .Select(p => p.ChannelId)
            .Distinct()
            .CountAsync();
    }

    /// <summary>
    /// XMLTV tarih formatını parse eder (yyyyMMddHHmmss +HHMM)
    /// </summary>
    private static DateTime ParseXmlTvDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return DateTime.MinValue;

        // XMLTV formats: 
        // 20240101120000 +0300
        // 20240101120000
        
        try
        {
            dateStr = dateStr.Trim();
            var normalizedOffsetDate = NormalizeXmlTvOffset(dateStr);

            // With offset
            if (DateTimeOffset.TryParseExact(
                normalizedOffsetDate,
                new[] { "yyyyMMddHHmmss zzz", "yyyyMMddHHmm zzz" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var dto))
            {
                return dto.UtcDateTime;
            }

            // Without offset
            if (DateTime.TryParseExact(
                datePart(normalizedOffsetDate),
                new[] { "yyyyMMddHHmmss", "yyyyMMddHHmm" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var dt))
            {
                return dt.ToUniversalTime();
            }
        }
        catch { }

        return DateTime.MinValue;
    }

    private static string NormalizeXmlTvOffset(string value)
    {
        // Supports:
        // 20240101120000 +0300
        // 20240101120000+0300
        // 20240101120000 +03:00
        var trimmed = value.Trim();
        var compact = trimmed.Replace(" ", "");

        var signIndex = compact.IndexOf('+');
        if (signIndex < 0)
        {
            signIndex = compact.IndexOf('-', 1); // ignore leading minus on malformed date
        }

        if (signIndex <= 0)
        {
            return trimmed;
        }

        var d = compact[..signIndex];
        var offset = compact[signIndex..];

        if (offset.Length == 5 && (offset[0] == '+' || offset[0] == '-'))
        {
            offset = $"{offset[..3]}:{offset[3..]}";
        }

        return $"{d} {offset}";
    }

    private static string datePart(string fullDate)
    {
        return fullDate.Split(' ')[0];
    }
}


