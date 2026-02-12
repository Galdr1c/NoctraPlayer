using System.IO.Compression;
using System.Net.Http;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Data;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

/// <summary>
/// EPG (Electronic Program Guide) servisi
/// </summary>
public class EpgService : IEpgService
{
    private readonly AppDbContext _context;
    private readonly HttpClient _httpClient;
    
    public bool IsLoaded { get; private set; }
    public DateTime? LastUpdated { get; private set; }
    public string? LastError { get; private set; }

    public EpgService(AppDbContext context, HttpClient httpClient)
    {
        _context = context;
        _httpClient = httpClient;
    }

    public async Task LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null, int daysAhead = 1)
    {
        try
        {
            LastError = null; // Clear previous error
            if (string.IsNullOrEmpty(epgUrl)) return;

            // ... (rest of the logic) ...
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
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

            if (isPrimary)
            {
                // Clear existing programs only if primary
                await ClearEpgAsync().ConfigureAwait(false);
            }

            // Build mapping dictionary for secondary EPG (Name -> TvggId)
            var channelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var xmlChannelIdToDbTvgId = new Dictionary<string, string>();
            var tvgIdToInternalId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allowedPrimaryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (isPrimary && channelsForMapping != null)
            {
                foreach (var channel in channelsForMapping)
                {
                    if (!string.IsNullOrWhiteSpace(channel.TvgId))
                    {
                        allowedPrimaryIds.Add(channel.TvgId!);
                    }

                    // Some providers use internal numeric IDs; keep this fallback.
                    allowedPrimaryIds.Add(channel.Id.ToString());
                }
            }

            if (!isPrimary && channelsForMapping != null)
            {
                foreach (var channel in channelsForMapping)
                {
                    if (!string.IsNullOrWhiteSpace(channel.TvgId) && !tvgIdToInternalId.ContainsKey(channel.TvgId!))
                    {
                        tvgIdToInternalId[channel.TvgId!] = channel.Id.ToString();
                    }

                    if (!string.IsNullOrEmpty(channel.Name))
                    {
                        foreach (var variant in GetNameVariants(channel.Name))
                        {
                            if (!string.IsNullOrEmpty(variant) && !channelMap.ContainsKey(variant))
                            {
                                channelMap[variant] = channel.Id.ToString();
                            }
                        }
                    }

                    // Also index tvg-name when available (often closer to XMLTV display-name)
                    if (!string.IsNullOrEmpty(channel.TvgName))
                    {
                        foreach (var variant in GetNameVariants(channel.TvgName))
                        {
                            if (!string.IsNullOrEmpty(variant) && !channelMap.ContainsKey(variant))
                            {
                                channelMap[variant] = channel.Id.ToString();
                            }
                        }
                    }
                }
            }

            var programs = new List<EpgProgram>();
            var batchSize = 2000;
            var windowStartUtc = DateTime.UtcNow.Date;
            var windowEndUtc = windowStartUtc.AddDays(Math.Max(1, daysAhead) + 1);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                 if (reader.NodeType == System.Xml.XmlNodeType.Element)
                {
                    if (reader.Name == "channel") 
                    {
                        // Map XML channel ID to DB TvgId for secondary EPG
                        if (!isPrimary && channelMap.Count > 0)
                        {
                            var xmlId = reader.GetAttribute("id");
                            if (xmlId != null)
                            {
                                // Strongest mapping: XML channel id == playlist tvg-id
                                if (tvgIdToInternalId.TryGetValue(xmlId, out var byTvgId))
                                {
                                    xmlChannelIdToDbTvgId[xmlId] = byTvgId;
                                    continue;
                                }

                                // Read display-name
                                using var subReader = reader.ReadSubtree();
                                while (await subReader.ReadAsync().ConfigureAwait(false))
                                {
                                    if (subReader.NodeType == System.Xml.XmlNodeType.Element && subReader.Name == "display-name")
                                    {
                                        var displayName = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                        string? dbTvgId = null;
                                        foreach (var variant in GetNameVariants(displayName))
                                        {
                                            dbTvgId = ResolveMappedChannelId(variant, channelMap);
                                            if (!string.IsNullOrEmpty(dbTvgId))
                                                break;
                                        }
                                        if (!string.IsNullOrEmpty(dbTvgId))
                                        {
                                            xmlChannelIdToDbTvgId[xmlId] = dbTvgId!;
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
                        if (isPrimary)
                        {
                            // Keep only channels present in current playlist.
                            if (allowedPrimaryIds.Count > 0 && !allowedPrimaryIds.Contains(channel))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (xmlChannelIdToDbTvgId.TryGetValue(channel, out var mappedId))
                            {
                                targetChannelId = mappedId;
                            }
                            else
                            {
                                // No match found for this channel in our DB, skip it to save space
                                continue;
                            }
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

                        if (programs.Count >= batchSize)
                        {
                            await _context.EpgPrograms.AddRangeAsync(programs).ConfigureAwait(false);
                            await _context.SaveChangesAsync().ConfigureAwait(false);
                            programs.Clear();
                        }
                    }
                }
            }

            // Final batch
            if (programs.Any())
            {
                await _context.EpgPrograms.AddRangeAsync(programs).ConfigureAwait(false);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }

            IsLoaded = true;
            LastUpdated = DateTime.Now;
        }
        catch (Exception ex)
        {
            LastError = ex.Message; // Capture error
            System.Diagnostics.Debug.WriteLine($"EPG Load Error: {ex}");
            // Don't throw if secondary
            if (isPrimary) throw;
        }
    }

    private static IEnumerable<string> GetNameVariants(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        var normalizedFull = NormalizeName(name);
        if (normalizedFull.Length >= 4)
            yield return normalizedFull;

        // Common country prefixes to strip (ISO codes 2-3 chars + separator)
        // e.g., "TR - Show TV", "DE: RTL", "UK| BBC", "TR Show TV"
        if (TryStripLeadingCountryCode(name, out var stripped))
        {
            var normalizedStripped = NormalizeName(stripped);
            if (normalizedStripped.Length >= 4)
                yield return normalizedStripped;
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
            "1080p", "720p", "480p", "2160p", "live", "vip"
        };

        var tokens = new string(chars.ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !noise.Contains(t));

        return string.Concat(tokens);
    }
    private static string? ResolveMappedChannelId(string normalizedDisplayName, Dictionary<string, string> channelMap)
    {
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || normalizedDisplayName.Length < 4)
            return null;

        if (channelMap.TryGetValue(normalizedDisplayName, out var exact))
        {
            return exact;
        }

        const double threshold = 0.78;
        string? bestId = null;
        double bestScore = 0;

        foreach (var kvp in channelMap)
        {
            if (kvp.Key.Length < 4)
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
        
        // Level 1: Try Primary TvgId
        if (!string.IsNullOrEmpty(channel.TvgId))
        {
            var program = await _context.EpgPrograms
                .Where(p => p.ChannelId == channel.TvgId && p.StartTime <= now && p.EndTime > now)
                .FirstOrDefaultAsync();

            if (program != null) return program;
        }

        // Level 2: Try Internal Id (Secondary EPG mapped by Name)
        var internalId = channel.Id.ToString();
        var programByInternalId = await _context.EpgPrograms
            .Where(p => p.ChannelId == internalId && p.StartTime <= now && p.EndTime > now)
            .FirstOrDefaultAsync();

        if (programByInternalId != null) return programByInternalId;

        return null; 
    }

    public async Task ClearEpgAsync()
    {
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms");
    }

    public async Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to)
    {
        // Ensure we compare in UTC if stored in UTC
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        return await _context.EpgPrograms
            .Where(p => p.ChannelId == channelId && p.StartTime >= fromUtc && p.StartTime <= toUtc)
            .OrderBy(p => p.StartTime)
            .ToListAsync();
    }

    public async Task<List<EpgProgram>> GetUpcomingProgramsAsync(string channelId, int count = 5)
    {
        var now = DateTime.UtcNow;
        return await _context.EpgPrograms
            .Where(p => p.ChannelId == channelId && p.StartTime > now)
            .OrderBy(p => p.StartTime)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<EpgProgram>> GetTodayProgramsAsync(string channelId)
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);
        return await _context.EpgPrograms
            .Where(p => p.ChannelId == channelId && p.StartTime >= todayStart && p.StartTime < todayEnd)
            .OrderBy(p => p.StartTime)
            .ToListAsync();
    }

    public async Task<int> GetTotalProgramCountAsync()
    {
        return await _context.EpgPrograms.CountAsync();
    }

    public async Task<int> GetDistinctChannelCountAsync()
    {
        return await _context.EpgPrograms
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

