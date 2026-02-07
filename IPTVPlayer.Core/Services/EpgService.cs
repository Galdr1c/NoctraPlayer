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
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HttpClient _httpClient;
    
    public bool IsLoaded { get; private set; }
    public DateTime? LastUpdated { get; private set; }

    public EpgService(IDbContextFactory<AppDbContext> contextFactory, HttpClient httpClient)
    {
        _contextFactory = contextFactory;
        _httpClient = httpClient;
    }

    public async Task LoadEpgAsync(string epgUrl)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var response = await _httpClient.GetAsync(epgUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { Async = true });

            using var context = await _contextFactory.CreateDbContextAsync();
            
            // Clear existing programs (consider optimization if this is too slow for very large DBs)
            await context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms");

            var programs = new List<EpgProgram>();
            var batchSize = 500;
            var now = DateTime.UtcNow;
            var cutoffDate = now.AddDays(-1);
            var maxFutureDate = now.AddDays(14); // Limit storage to 14 days

            while (await reader.ReadAsync())
            {
                if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.Name == "programme")
                {
                    var start = reader.GetAttribute("start");
                    var stop = reader.GetAttribute("stop");
                    var channel = reader.GetAttribute("channel");

                    var startTime = ParseXmlTvDate(start);
                    var endTime = ParseXmlTvDate(stop);

                    // Skip old or too far future programs
                    if (endTime < cutoffDate || startTime > maxFutureDate)
                        continue;

                    var program = new EpgProgram
                    {
                        ChannelId = channel ?? "",
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
                        await context.EpgPrograms.AddRangeAsync(programs);
                        await context.SaveChangesAsync();
                        programs.Clear();
                    }
                }
            }

            // Final batch
            if (programs.Any())
            {
                await context.EpgPrograms.AddRangeAsync(programs);
                await context.SaveChangesAsync();
            }

            IsLoaded = true;
            LastUpdated = DateTime.Now;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EPG Load Error: {ex}");
            throw;
        }
    }

    public async Task<EpgProgram?> GetCurrentProgramAsync(string channelId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        return await context.EpgPrograms
            .Where(p => p.ChannelId == channelId && p.StartTime <= now && p.EndTime > now)
            .FirstOrDefaultAsync();
    }

    public async Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        // Ensure we compare in UTC if stored in UTC
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        return await context.EpgPrograms
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
