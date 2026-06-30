using Microsoft.EntityFrameworkCore;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class DatabaseSchemaFixupServiceTests
{
    [Fact]
    public async Task ApplyAsync_CanRunRepeatedlyOnFreshDatabase()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();

            var service = new DatabaseSchemaFixupService();
            await service.ApplyAsync(context, DatabaseSchemaFixupProfile.Mobile);
            await service.ApplyAsync(context, DatabaseSchemaFixupProfile.Mobile);

            Assert.True(await ColumnExistsAsync(context, "Channels", "CurrentProgramId"));
            Assert.True(await ColumnExistsAsync(context, "SeriesEpisodeProgresses", "TmdbId"));
            Assert.True(await ColumnExistsAsync(context, "DownloadItems", "LocalFilePath"));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_AddsTmdbIdToLegacySeriesEpisodeProgressTable()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE SeriesEpisodeProgresses (
                    Id INTEGER NOT NULL CONSTRAINT PK_SeriesEpisodeProgresses PRIMARY KEY AUTOINCREMENT,
                    ProfileId INTEGER NOT NULL,
                    SeriesKey TEXT NOT NULL,
                    SeriesTitle TEXT NOT NULL,
                    SeasonNumber INTEGER NOT NULL,
                    EpisodeNumber INTEGER NOT NULL,
                    LastWatchedAt TEXT NOT NULL,
                    StoppedAt TEXT NOT NULL,
                    Duration TEXT NULL,
                    Completed INTEGER NOT NULL DEFAULT 0
                );
                """);

            var service = new DatabaseSchemaFixupService();
            await service.ApplyAsync(context, DatabaseSchemaFixupProfile.Desktop);

            Assert.True(await ColumnExistsAsync(context, "SeriesEpisodeProgresses", "TmdbId"));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_CreatesSeriesEpisodeProgressTableWithTmdbIdWhenMissing()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);

            var service = new DatabaseSchemaFixupService();
            await service.ApplyAsync(context, DatabaseSchemaFixupProfile.Desktop);

            Assert.True(await ColumnExistsAsync(context, "SeriesEpisodeProgresses", "TmdbId"));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    private static AppDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        return new AppDbContext(options);
    }

    private static string CreateTempDatabasePath()
        => Path.Combine(Path.GetTempPath(), $"noctra-schema-fixup-{Guid.NewGuid():N}.db");

    private static async Task<bool> ColumnExistsAsync(AppDbContext context, string tableName, string columnName)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
