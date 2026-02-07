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

    public async Task LoadEpgAsync(string epgUrl)
    {
        try
        {
            var content = await _httpClient.GetStringAsync(epgUrl);
            var doc = XDocument.Parse(content);

            // Mevcut EPG verilerini sil
            var existingPrograms = await _context.EpgPrograms.ToListAsync();
            _context.EpgPrograms.RemoveRange(existingPrograms);

            // Programları parse et
            var programs = doc.Descendants("programme")
                .Select(p => new EpgProgram
                {
                    ChannelId = p.Attribute("channel")?.Value ?? "",
                    Title = p.Element("title")?.Value ?? "Bilinmeyen Program",
                    Description = p.Element("desc")?.Value,
                    StartTime = ParseXmlTvDate(p.Attribute("start")?.Value),
                    EndTime = ParseXmlTvDate(p.Attribute("stop")?.Value),
                    Category = p.Element("category")?.Value,
                    IconUrl = p.Element("icon")?.Attribute("src")?.Value
                })
                .Where(p => p.EndTime > DateTime.Now.AddDays(-1)) // Sadece güncel programlar
                .ToList();

            _context.EpgPrograms.AddRange(programs);
            await _context.SaveChangesAsync();

            IsLoaded = true;
            LastUpdated = DateTime.Now;
        }
        catch (System.Xml.XmlException ex)
        {
             throw new InvalidOperationException($"EPG verisi hatalı formatta: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"EPG yüklenemedi: {ex.Message}", ex);
        }
    }

    public async Task<EpgProgram?> GetCurrentProgramAsync(string channelId)
    {
        var now = DateTime.Now;
        return await _context.EpgPrograms
            .Where(p => p.ChannelId == channelId && p.StartTime <= now && p.EndTime > now)
            .FirstOrDefaultAsync();
    }

    public async Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to)
    {
        return await _context.EpgPrograms
            .Where(p => p.ChannelId == channelId && p.StartTime >= from && p.StartTime <= to)
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

        // "20240101120000 +0300" formatı
        var parts = dateStr.Split(' ');
        var datePart = parts[0];

        if (datePart.Length >= 14)
        {
            if (DateTime.TryParseExact(datePart[..14], "yyyyMMddHHmmss",
                null, System.Globalization.DateTimeStyles.None, out var result))
            {
                return result;
            }
        }

        return DateTime.MinValue;
    }
}
