using Microsoft.EntityFrameworkCore;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Services;
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
            Assert.True(await IndexExistsAsync(context, "IX_Channels_PlaylistId_StreamUrl"));
            Assert.True(await IndexExistsAsync(context, "IX_Series_PlaylistId"));
            Assert.True(await IndexExistsAsync(context, "IX_ImportJobs_OneActivePerProfile"));
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

    [Fact]
    public async Task ApplyAsync_CancelsDuplicateLegacyActiveJobsBeforeCreatingUniqueIndex()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_ImportJobs_OneActivePerProfile;");
            var now = DateTime.UtcNow;
            context.ImportJobs.AddRange(
                new Noctra.Models.ImportJob
                {
                    ProfileId = 7,
                    PlaylistId = 9,
                    Kind = Noctra.Models.ImportJobKind.Xtream,
                    Status = Noctra.Models.ImportJobStatus.Running,
                    SourceName = "Old",
                    Stage = "Importing",
                    CreatedAt = now.AddMinutes(-1),
                    UpdatedAt = now.AddMinutes(-1)
                },
                new Noctra.Models.ImportJob
                {
                    ProfileId = 7,
                    PlaylistId = 9,
                    Kind = Noctra.Models.ImportJobKind.Xtream,
                    Status = Noctra.Models.ImportJobStatus.Running,
                    SourceName = "New",
                    Stage = "Importing",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            await context.SaveChangesAsync();

            await new DatabaseSchemaFixupService().ApplyAsync(context, DatabaseSchemaFixupProfile.Mobile);

            Assert.Equal(1, await context.ImportJobs.CountAsync(job => job.ProfileId == 7 &&
                (job.Status == Noctra.Models.ImportJobStatus.Queued || job.Status == Noctra.Models.ImportJobStatus.Running)));
            Assert.Equal(1, await context.ImportJobs.CountAsync(job => job.ProfileId == 7 && job.Status == Noctra.Models.ImportJobStatus.Canceled));
            Assert.True(await IndexExistsAsync(context, "IX_ImportJobs_OneActivePerProfile"));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_AddsPersistentPlaylistRepairVersion()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE Playlists (
                    Id INTEGER NOT NULL CONSTRAINT PK_Playlists PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1
                );
                """);

            await new DatabaseSchemaFixupService().ApplyAsync(
                context,
                DatabaseSchemaFixupProfile.Mobile);

            Assert.True(await ColumnExistsAsync(
                context,
                "Playlists",
                "ChannelTypeRepairVersion"));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_ResetsLegacyAndMalformedPin2Hashes_ButKeepsValidOnes()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();

            // Eski PBKDF2 / legacy SHA-256 + PIN2$ önekli fakat gerçek formatı
            // bozuk kayıtlar (kısa salt/hash, geçersiz Base64, eksik parça).
            // LIKE/önek tabanlı eski SQL bunlardan yalnızca ilk ikisini yakalardı.
            var legacyPbkdf2 = await SeedProfileAsync(context, "Legacy PBKDF2", "PBKDF2$SHA256$210000$c2FsdA==$aGFzaA==");
            var legacySha256 = await SeedProfileAsync(context, "Legacy SHA-256", "83D837DD7E939316F5A94A1216FF2E6F2DC9E9859441F333CC12FA2414468B88");
            var malformedShort = await SeedProfileAsync(context, "Malformed Short", "PIN2$AA==$BB==");
            var malformedBase64 = await SeedProfileAsync(context, "Malformed Base64", "PIN2$!!!not-base64!!!$!!!!");
            var malformedParts = await SeedProfileAsync(context, "Malformed Parts", "PIN2$only-two-parts");
            var validPin = await SeedProfileAsync(context, "Valid PIN", ProfilePinVerifier.Create("1234"));
            var pinless = await SeedProfileAsync(context, "Pinless", null);

            // Act — Seçenek A: açılamaz kayıtlar bir defalık sıfırlanır
            var resetCount = await new DatabaseSchemaFixupService()
                .ApplyAsync(context, DatabaseSchemaFixupProfile.Desktop);

            // Assert — 5 açılamaz kayıt (2 legacy + 3 bozuk PIN2) sıfırlandı;
            // sağlam PIN2 ve PIN'siz profil korundu.
            Assert.Equal(5, resetCount);

            await using var verify = CreateContext(databasePath);
            Assert.Null((await verify.Profiles.FindAsync(legacyPbkdf2.Id))!.PinHash);
            Assert.Null((await verify.Profiles.FindAsync(legacySha256.Id))!.PinHash);
            Assert.Null((await verify.Profiles.FindAsync(malformedShort.Id))!.PinHash);
            Assert.Null((await verify.Profiles.FindAsync(malformedBase64.Id))!.PinHash);
            Assert.Null((await verify.Profiles.FindAsync(malformedParts.Id))!.PinHash);

            var preserved = await verify.Profiles.FindAsync(validPin.Id);
            Assert.True(ProfilePinVerifier.IsWellFormed(preserved!.PinHash));
            Assert.True(ProfilePinVerifier.Verify("1234", preserved.PinHash));
            Assert.Null((await verify.Profiles.FindAsync(pinless.Id))!.PinHash);

            // Idempotency — ikinci koşu hiçbir kayıt bulamaz (0), sağlam PIN korunur.
            await using var repeat = CreateContext(databasePath);
            var secondCount = await new DatabaseSchemaFixupService()
                .ApplyAsync(repeat, DatabaseSchemaFixupProfile.Desktop);
            Assert.Equal(0, secondCount);

            await using var verifyAgain = CreateContext(databasePath);
            var preservedAgain = await verifyAgain.Profiles.FindAsync(validPin.Id);
            Assert.True(ProfilePinVerifier.Verify("1234", preservedAgain!.PinHash));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    private static async Task<Noctra.Models.Profile> SeedProfileAsync(
        AppDbContext context,
        string name,
        string? pinHash)
    {
        var account = new Noctra.Models.ProviderAccount
        {
            Name = name + " Account",
            Url = "http://test.com",
            Type = Noctra.Models.ProfileType.M3U
        };
        context.ProviderAccounts.Add(account);
        await context.SaveChangesAsync();

        var profile = new Noctra.Models.Profile
        {
            Name = name,
            ProviderAccount = account,
            PinHash = pinHash,
            LastUsed = DateTime.UtcNow
        };
        context.Profiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    [Fact]
    public async Task ApplyAsync_DoesNotRewriteAlreadyCleanEuSeriesMetadata()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            var playlist = new Noctra.Models.Playlist { Name = "M3U" };
            context.Playlists.Add(playlist);
            await context.SaveChangesAsync();
            context.Series.Add(new Noctra.Models.Series
            {
                Name = "Already Clean",
                PlaylistId = playlist.Id,
                GroupTitle = "EU SERIES"
            });
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE SeriesUpdateAudit (Count INTEGER NOT NULL);");
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO SeriesUpdateAudit (Count) VALUES (0);");
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER CountSeriesUpdates
                AFTER UPDATE ON Series
                BEGIN
                    UPDATE SeriesUpdateAudit SET Count = Count + 1;
                END;
                """);

            await new DatabaseSchemaFixupService().ApplyAsync(
                context,
                DatabaseSchemaFixupProfile.Mobile);

            Assert.Equal(
                0,
                await ExecuteScalarIntAsync(context, "SELECT Count FROM SeriesUpdateAudit;"));
            Assert.True(await IndexExistsAsync(context, "IX_Series_GroupTitle"));
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

    private static async Task<bool> IndexExistsAsync(AppDbContext context, string indexName)
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
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $indexName LIMIT 1;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$indexName";
            parameter.Value = indexName;
            command.Parameters.Add(parameter);
            return await command.ExecuteScalarAsync() is not null;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<int> ExecuteScalarIntAsync(AppDbContext context, string sql)
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
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
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
