using Noctra.Data;

namespace Noctra.Services.Interfaces;

public enum DatabaseSchemaFixupProfile
{
    Desktop,
    Mobile
}

public interface IDatabaseSchemaFixupService
{
    Task ApplyAsync(
        AppDbContext context,
        DatabaseSchemaFixupProfile profile,
        CancellationToken cancellationToken = default);
}
