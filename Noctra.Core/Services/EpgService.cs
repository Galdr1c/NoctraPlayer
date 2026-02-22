using System.IO.Compression;
using System.Net.Http;
using System.Xml.Linq;
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
    private readonly ILogger<EpgService>? _logger;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    
    public bool IsLoaded { get; private set; }
    public DateTime? LastUpdated { get; private set; }
    public string? LastError { get; private set; }

    public EpgService(IDbContextFactory<AppDbContext> contextFactory, HttpClient httpClient, ILogger<EpgService>? logger = null)
    {
        _contextFactory = contextFactory;
        _httpClient = httpClient;
        _logger = logger;
    }

    public void ClearLastError()
    {
        LastError = null;
    }

    public async Task<int> LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null, int daysAhead = 1)
    {
        if (!await _loadSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            _logger?.LogWarning("Another EPG load is in progress, skipping new request.");
            return 0;
        }

        int totalLoaded = 0;
        try
        {
            LastError = null; // Clear previous error
            if (string.IsNullOrEmpty(epgUrl)) return 0;

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var response = await NetworkRetry.ExecuteAsync(
                () => _httpClient.GetAsync(epgUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token),
                cancellationToken: cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);

            // GZip decompression support (.gz URLs)
            Stream dataStream = stream;
            if (epgUrl.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                response.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                dataStream = new GZipStream(stream, CompressionMode.Decompress);
            }

            var settings = new System.Xml.XmlReaderSettings 
            { 
                Async = true, 
                DtdProcessing = System.Xml.DtdProcessing.Ignore 
            };
            using var reader = System.Xml.XmlReader.Create(dataStream, settings);

            // NOTE: Clearing is handled by MainViewModel via ClearBeforeLoad flag.
            // Do NOT clear here — multiple sources may be loaded sequentially,
            // and clearing on every isPrimary source would wipe previously loaded data.

            // Build mapping dictionary for EPG (Name -> channel Id)
            var channelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var xmlChannelIdToDbTvgId = new Dictionary<string, string>();
            var tvgIdToInternalId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allowedPrimaryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (channelsForMapping != null)
            {
                foreach (var channel in channelsForMapping)
                {
                    if (!string.IsNullOrWhiteSpace(channel.TvgId))
                    {
                        allowedPrimaryIds.Add(channel.TvgId!);
                        if (!tvgIdToInternalId.ContainsKey(channel.TvgId!))
                            tvgIdToInternalId[channel.TvgId!] = channel.Id.ToString();
                    }

                    // Some providers use internal numeric IDs; keep this fallback.
                    allowedPrimaryIds.Add(channel.Id.ToString());

                    // Build name map for fuzzy matching (used by both primary and secondary)
                    foreach (var variant in GetNameVariants(channel.Name))
                    {
                        if (!string.IsNullOrEmpty(variant) && !channelMap.ContainsKey(variant))
                            channelMap[variant] = channel.Id.ToString();
                    }
                    if (!string.IsNullOrEmpty(channel.TvgName))
                    {
                        foreach (var variant in GetNameVariants(channel.TvgName))
                        {
                            if (!string.IsNullOrEmpty(variant) && !channelMap.ContainsKey(variant))
                                channelMap[variant] = channel.Id.ToString();
                        }
                    }
                }
            }

            // (Name map built above for both primary and secondary)

            var programs = new List<EpgProgram>();
            var batchSize = 2000;
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
                            // Map XML channel ID to DB channel for BOTH primary and secondary EPG
                            if (channelMap.Count > 0)
                            {
                                var xmlId = reader.GetAttribute("id");
                                if (xmlId != null)
                                {
                                    // Strongest mapping: XML channel id == playlist tvg-id
                                    if (tvgIdToInternalId.TryGetValue(xmlId, out var byTvgId))
                                    {
                                        xmlChannelIdToDbTvgId[xmlId] = byTvgId;
                                        // For primary EPG, also mark the xmlId as allowed
                                        if (isPrimary) allowedPrimaryIds.Add(xmlId);
                                        continue;
                                    }

                                    // If xmlId is already in allowedPrimaryIds (exact match), skip fuzzy
                                    if (isPrimary && allowedPrimaryIds.Contains(xmlId))
                                        continue;

                                    // Fuzzy: Read display-name and match against channel names
                                    using var subReader = reader.ReadSubtree();
                                    while (await subReader.ReadAsync().ConfigureAwait(false))
                                    {
                                        if (subReader.NodeType == System.Xml.XmlNodeType.Element && subReader.Name == "display-name")
                                        {
                                            var displayName = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                            string? dbChannelId = null;
                                            foreach (var variant in GetNameVariants(displayName))
                                            {
                                                dbChannelId = ResolveMappedChannelId(variant, channelMap);
                                                if (!string.IsNullOrEmpty(dbChannelId))
                                                    break;
                                            }
                                            if (!string.IsNullOrEmpty(dbChannelId))
                                            {
                                                xmlChannelIdToDbTvgId[xmlId] = dbChannelId!;
                                                if (isPrimary) allowedPrimaryIds.Add(xmlId);
                                                break; // Found match
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

                            // Determine target ChannelId
                            string targetChannelId = channel;
                            if (xmlChannelIdToDbTvgId.TryGetValue(channel, out var mappedId))
                            {
                                // Use the mapped internal channel ID (works for both primary and secondary)
                                targetChannelId = mappedId;
                            }
                            else if (isPrimary)
                            {
                                // Keep only channels present in current playlist.
                                if (allowedPrimaryIds.Count > 0 && !allowedPrimaryIds.Contains(channel))
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                // Secondary: No match found for this channel in our DB, skip
                                continue;
                            }

                            var startTime = ParseXmlTvDate(start);
                            var endTime = ParseXmlTvDate(stop);

                            // Keep only the requested time window (today + N days)
                            if (endTime <= windowStartUtc || startTime >= windowEndUtc)
                                continue;

                            var program = new EpgProgram
                            {
                                ChannelId = targetChannelId,
                                StartTime = startTime,
                                EndTime = endTime
                            };

                            // Read inner elements
                            using var subReader = reader.ReadSubtree();
                            while (await subReader.ReadAsync().ConfigureAwait(false))
                            {
                                if (subReader.NodeType == System.Xml.XmlNodeType.Element)
                                {
                                    switch (subReader.Name)
                                    {
                                        case "title":
                                            program.Title = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                            break;
                                        case "desc":
                                            // Keep first non-empty description.
                                            if (string.IsNullOrWhiteSpace(program.Description))
                                            {
                                                var desc = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                                if (!string.IsNullOrWhiteSpace(desc))
                                                {
                                                    program.Description = desc;
                                                }
                                            }
                                            break;
                                    }
                                }
                            }

                            programs.Add(program);
                            totalLoaded++;

                            if (programs.Count >= batchSize)
                            {
                                await context.EpgPrograms.AddRangeAsync(programs).ConfigureAwait(false);
                                await context.SaveChangesAsync().ConfigureAwait(false);
                                programs.Clear();
                            }
                        }
                    }
                }

                // Final batch
                if (programs.Any())
                {
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
            return totalLoaded;
        }
        catch (Exception ex)
        {
            LastError = UserFriendlyErrorMessage.FromException(ex);
            _logger?.LogError(ex, "EPG load failed for URL {EpgUrl}", epgUrl);
            // Don't throw if secondary
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

        var s = name.ToLowerInvariant()
            .Replace('ı', 'i')
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
            "hd", "fhd", "uhd", "sd", "hevc", "h265", "h264", "4k",
            "1080p", "720p", "480p", "2160p", "live", "vip",
            "backup", "bkp", "multi", "sub", "ace", "plus",
            "turkey", "turkiye", "tr", "s"
        };

        var tokens = new string(chars.ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !noise.Contains(t));

        return string.Concat(tokens);
    }
    private static string? ResolveMappedChannelId(string normalizedDisplayName, Dictionary<string, string> channelMap)
    {
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || normalizedDisplayName.Length < 3)
            return null;

        if (channelMap.TryGetValue(normalizedDisplayName, out var exact))
        {
            return exact;
        }

        // Dynamic threshold: shorter names need less strict matching
        var maxLen = Math.Max(normalizedDisplayName.Length, 3);
        var threshold = maxLen <= 6 ? 0.65 : maxLen <= 10 ? 0.72 : 0.78;

        string? bestId = null;
        double bestScore = 0;

        foreach (var kvp in channelMap)
        {
            if (kvp.Key.Length < 3)
                continue;
            var score = Similarity(normalizedDisplayName, kvp.Key);
            if (score > bestScore)
            {
                bestScore = score;
                bestId = kvp.Value;
            }
        }

        return bestScore >= threshold ? bestId : null;
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
    public async Task<EpgProgram?> GetCurrentProgramAsync(Channel channel)
    {
        var now = DateTime.UtcNow;
        using var context = await _contextFactory.CreateDbContextAsync();
        
        // Level 1: Try Primary TvgId
        if (!string.IsNullOrEmpty(channel.TvgId))
        {
            var program = await context.EpgPrograms
                .AsNoTracking()
                .Where(p => p.ChannelId == channel.TvgId && p.StartTime <= now && p.EndTime > now)
                .FirstOrDefaultAsync();

            if (program != null) return program;
        }

        // Level 2: Try Internal Id (Secondary EPG mapped by Name)
        var internalId = channel.Id.ToString();
        var programByInternalId = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == internalId && p.StartTime <= now && p.EndTime > now)
            .FirstOrDefaultAsync();

        if (programByInternalId != null) return programByInternalId;

        return null; 
    }

    public async Task ClearEpgAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms");
    }

    public async Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to)
    {
        // Ensure we compare in UTC if stored in UTC
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime >= fromUtc && p.StartTime <= toUtc)
            .OrderBy(p => p.StartTime)
            .ToListAsync();
    }

    public async Task<List<EpgProgram>> GetUpcomingProgramsAsync(string channelId, int count = 5)
    {
        var now = DateTime.UtcNow;
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime > now)
            .OrderBy(p => p.StartTime)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<EpgProgram>> GetTodayProgramsAsync(string channelId)
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime >= todayStart && p.StartTime < todayEnd)
            .OrderBy(p => p.StartTime)
            .ToListAsync();
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


