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

    public EpgService(AppDbContext context, HttpClient httpClient)
    {
        _context = context;
        _httpClient = httpClient;
    }

    public async Task LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null)
    {
        try
        {
            if (string.IsNullOrEmpty(epgUrl)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var response = await _httpClient.GetAsync(epgUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { Async = true });

            if (isPrimary)
            {
                // Clear existing programs only if primary
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms");
            }

            // Build mapping dictionary for secondary EPG (Name -> TvggId)
            var channelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var xmlChannelIdToDbTvgId = new Dictionary<string, string>();

            if (!isPrimary && channelsForMapping != null)
            {
                foreach (var channel in channelsForMapping)
                {
                    if (!string.IsNullOrEmpty(channel.Name))
                    {
                        var normalizedName = NormalizeName(channel.Name);
                        if (!channelMap.ContainsKey(normalizedName))
                        {
                            channelMap[normalizedName] = channel.TvgId ?? channel.Id.ToString(); // Fallback to internal ID if needed? No, EpgProgram needs TvgId usually. 
                            // Actually EpgProgam uses "ChannelId" string which usually matches Channel.TvgId
                            // If Channel.TvgId is missing, we can't Link easily unless we update Channel too.
                            // For now assume matched channel has TvgId or we use its Name as ID?
                            // Better: The UI looks up EPG by Channel.TvgId. 
                            // So we need to store EPG with that TvgId.
                            if (!string.IsNullOrEmpty(channel.TvgId))
                            {
                                channelMap[normalizedName] = channel.TvgId;
                            }
                        }
                    }
                }
            }

            var programs = new List<EpgProgram>();
            var batchSize = 500;
            var now = DateTime.UtcNow;
            var cutoffDate = now.AddDays(-1);
            var maxFutureDate = now.AddDays(7); // Limit storage to 7 days for performance

            while (await reader.ReadAsync())
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
                                // Read display-name
                                using var subReader = reader.ReadSubtree();
                                while (await subReader.ReadAsync())
                                {
                                    if (subReader.NodeType == System.Xml.XmlNodeType.Element && subReader.Name == "display-name")
                                    {
                                        var displayName = await subReader.ReadElementContentAsStringAsync();
                                        var normalized = NormalizeName(displayName);
                                        if (channelMap.TryGetValue(normalized, out var dbTvgId))
                                        {
                                            xmlChannelIdToDbTvgId[xmlId] = dbTvgId;
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
                        if (!isPrimary)
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

                        // Skip old or too far future programs
                        if (endTime < cutoffDate || startTime > maxFutureDate)
                            continue;

                        var program = new EpgProgram
                        {
                            ChannelId = targetChannelId,
                            StartTime = startTime,
                            EndTime = endTime
                        };

                        // Read inner elements
                        using var subReader = reader.ReadSubtree();
                        while (await subReader.ReadAsync())
                        {
                            if (subReader.NodeType == System.Xml.XmlNodeType.Element)
                            {
                                switch (subReader.Name)
                                {
                                    case "title":
                                        program.Title = await subReader.ReadElementContentAsStringAsync();
                                        break;
                                    case "desc":
                                        program.Description = await subReader.ReadElementContentAsStringAsync();
                                        break;
                                    case "category":
                                        program.Category = await subReader.ReadElementContentAsStringAsync();
                                        break;
                                    case "icon":
                                        program.IconUrl = subReader.GetAttribute("src");
                                        break;
                                }
                            }
                        }

                        programs.Add(program);

                        if (programs.Count >= batchSize)
                        {
                            await _context.EpgPrograms.AddRangeAsync(programs);
                            await _context.SaveChangesAsync();
                            programs.Clear();
                        }
                    }
                }
            }

            // Final batch
            if (programs.Any())
            {
                await _context.EpgPrograms.AddRangeAsync(programs);
                await _context.SaveChangesAsync();
            }

            IsLoaded = true;
            LastUpdated = DateTime.Now;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EPG Load Error: {ex}");
            // Don't throw if secondary
            if (isPrimary) throw;
        }
    }

    private string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        return name.ToLowerInvariant()
            .Replace(" ", "")
            .Replace("hd", "")
            .Replace("fhd", "")
            .Replace("4k", "")
            .Replace("tr", "") // Remove country codes? Maybe risky.
            .Replace("-", "")
            .Replace(".", "");
    }

    public async Task<EpgProgram?> GetCurrentProgramAsync(string channelId)
    {
        var now = DateTime.UtcNow;
        return await _context.EpgPrograms
            .Where(p => p.ChannelId == channelId && p.StartTime <= now && p.EndTime > now)
            .FirstOrDefaultAsync();
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
            // Normalize spaces
            dateStr = dateStr.Trim();
            
            if (dateStr.Contains(" +") || dateStr.Contains(" -"))
            {
                // Has offset
                if (DateTimeOffset.TryParseExact(dateStr, 
                    new[] { "yyyyMMddHHmmss zzz", "yyyyMMddHHmm zzz", "yyyyMMddHHmmss zzzz", "yyyyMMddHHmm zzzz" },
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, 
                    out var dto))
                {
                    return dto.UtcDateTime;
                }
            }
            else
            {
                // No offset, assume local or UTC? XMLTV spec varies, but usually it's UTC-like or local.
                // We'll try to parse as local then convert to UTC for consistent storage.
                if (DateTime.TryParseExact(datePart(dateStr), 
                    new[] { "yyyyMMddHHmmss", "yyyyMMddHHmm" },
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, 
                    out var dt))
                {
                    return dt.ToUniversalTime();
                }
            }
        }
        catch { }

        return DateTime.MinValue;
    }

    private static string datePart(string fullDate)
    {
        return fullDate.Split(' ')[0];
    }
}
