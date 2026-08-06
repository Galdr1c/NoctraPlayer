using Noctra.Data;

namespace Noctra.Services.Interfaces;

public enum DatabaseSchemaFixupProfile
{
    Desktop,
    Mobile
}

public interface IDatabaseSchemaFixupService
{
    /// <summary>
    /// Şema düzeltmelerini uygular. Dönen değer: açılamaz durumdaki PIN
    /// kayıtları (eski PBKDF2/legacy SHA-256 formatı veya PIN2$ önekli bozuk
    /// salt/hash) nedeniyle sıfırlanan profil PIN'i sayısı. Tüm PIN'ler
    /// PIN2 (salt'lı SHA-256) formatında ve sağlamsa bu değer 0'dır.
    /// </summary>
    Task<int> ApplyAsync(
        AppDbContext context,
        DatabaseSchemaFixupProfile profile,
        CancellationToken cancellationToken = default);
}
