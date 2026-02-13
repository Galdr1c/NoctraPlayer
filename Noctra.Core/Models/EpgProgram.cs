namespace Noctra.Models;

/// <summary>
/// EPG (Electronic Program Guide) program bilgilerini tutar
/// </summary>
public class EpgProgram
{
    public int Id { get; set; }
    public string ChannelId { get; set; } = string.Empty; // tvg-id ile eşleşir
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Category { get; set; }
    public string? IconUrl { get; set; }

    public string TimeRange => $"{StartTime.ToLocalTime():HH:mm} - {EndTime.ToLocalTime():HH:mm}";
    
    /// <summary>
    /// Programın şu an yayında olup olmadığını kontrol eder
    /// </summary>
    public bool IsNowPlaying => DateTime.UtcNow >= StartTime && DateTime.UtcNow <= EndTime;
    
    /// <summary>
    /// Programın ilerleme yüzdesini hesaplar
    /// </summary>
    public double ProgressPercentage
    {
        get
        {
            if (!IsNowPlaying) return 0;
            var total = (EndTime - StartTime).TotalMinutes;
            var elapsed = (DateTime.UtcNow - StartTime).TotalMinutes;
            return Math.Min(100, (elapsed / total) * 100);
        }
    }
}


