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

    /// <summary>
    /// Çocuk profili özelliği kaldırıldı. Bu kolon yalnızca eski kayıtları
    /// standarta çeviren ResetChildModeAsync migration'ı için korunuyor;
    /// uygulama kodu artık bu değeri yazmıyor (yeni profiller her zaman
    /// standarttır, düzenleme IsChild'a dokunmaz). İleride kolonla birlikte
    /// silinebilir.
    /// </summary>
    public bool IsChild { get; set; }

    public DateTime LastUsed { get; set; } = DateTime.MinValue;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 4 haneli PIN'in hash'i. Null = PIN yok.
    /// </summary>
    public string? PinHash { get; set; }

    /// <summary>
    /// Art arda yapılan başarısız PIN denemelerinin sayısı.
    /// Başarılı doğrulamada veya kilit süresi dolunca sıfırlanır.
    /// </summary>
    public int FailedPinAttempts { get; set; }

    /// <summary>
    /// PIN girişinin kilitli olduğu sürenin bittiği UTC zaman. Null = kilit yok.
    /// Uygulama yeniden başlatılsa bile korunur.
    /// </summary>
    public DateTime? PinLockedUntilUtc { get; set; }

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
    /// Sürekli eğri: 0s → 0.0, 12s → 0.3, 24s → 0.6, 72s → 1.0.
    /// 24 saat sınırında ani sıçrama olmaz (eski davranış 24s üstünde
    /// doğrudan 1.0 dönerdi; 0.6 → 1.0 atlaması görsel kesintiye yol açardı).
    /// </summary>
    [NotMapped]
    public double DeletionUrgency
    {
        get
        {
            if (!IsPendingDeletion || TimeUntilDeletion == null) return 1.0;
            var totalHours = TimeUntilDeletion.Value.TotalHours;
            if (totalHours <= 0) return 0.0;
            if (totalHours <= 12) return totalHours / 12.0 * 0.3;              // 0.0–0.3
            if (totalHours <= 24) return 0.3 + (totalHours - 12) / 12.0 * 0.3; // 0.3–0.6
            if (totalHours <= 72) return 0.6 + (totalHours - 24) / 48.0 * 0.4; // 0.6–1.0
            return 1.0;
        }
    }

    public string Initials => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?";
}

