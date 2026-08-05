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
    /// Şema düzeltmelerini uygular. Dönen değer: eski PIN formatı
    /// (PBKDF2/legacy SHA-256) nedeniyle sıfırlanan profil PIN'i sayısı.
    /// PIN'ler PIN2 (salt'lı SHA-256) formatına geçildiğinde bu değer 0'dır.
    /// </summary>
    Task<int> ApplyAsync(
        AppDbContext context,
        DatabaseSchemaFixupProfile profile,
        CancellationToken cancellationToken = default);
}
