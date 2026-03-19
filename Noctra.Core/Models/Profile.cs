using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Noctra.Models;

public enum ProfileType
{
    M3U,
    XtreamCodes,
    StalkerPortal
}

public class Profile
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int ProviderAccountId { get; set; }
    public ProviderAccount? ProviderAccount { get; set; }

    // Visuals
    public string Avatar { get; set; } = "default"; // Asset name or path
    public string Color { get; set; } = "#FF6B00"; // Default accent color

    public bool IsChild { get; set; }

    public DateTime LastUsed { get; set; } = DateTime.MinValue;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 4 haneli PIN'in SHA256 hash'i. Null = PIN yok.
    /// </summary>
    public string? PinHash { get; set; }

    /// <summary>
    /// PIN unuttum akışı başlatıldığında set edilir.
    /// Null ise hesap normal durumda.
    /// </summary>
    public DateTime? PendingDeletionAt { get; set; }

    /// <summary>
    /// Hesap silinme tarihi (PendingDeletionAt + 3 gün)
    /// </summary>
    [NotMapped]
    public DateTime? ScheduledDeletionAt =>
        PendingDeletionAt?.AddDays(3);

    /// <summary>
    /// Geri sayım aktif mi
    /// </summary>
    [NotMapped]
    public bool IsPendingDeletion => PendingDeletionAt.HasValue;

    /// <summary>
    /// Kalan süre — UI için
    /// </summary>
    [NotMapped]
    public TimeSpan? TimeUntilDeletion =>
        ScheduledDeletionAt.HasValue
            ? ScheduledDeletionAt.Value - DateTime.UtcNow
            : null;

    /// <summary>
    /// Renk geçişi için: 0.0 (tam kırmızı) - 1.0 (normal)
    /// Son 12 saat kırmızıya, son 24 saat turuncuya döner
    /// </summary>
    [NotMapped]
    public double DeletionUrgency
    {
        get
        {
            if (!IsPendingDeletion || TimeUntilDeletion == null) return 1.0;
            var totalHours = TimeUntilDeletion.Value.TotalHours;
            if (totalHours <= 0) return 0.0;
            if (totalHours <= 12) return totalHours / 12.0 * 0.3;       // 0.0–0.3
            if (totalHours <= 24) return 0.3 + (totalHours - 12) / 12.0 * 0.3; // 0.3–0.6
            return 1.0;
        }
    }

    public string Initials => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?";
}

