using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Services.Interfaces;
using System.Data;

namespace Noctra.Core.Services;

public sealed class DatabaseSchemaFixupService : IDatabaseSchemaFixupService
{
    public async Task ApplyAsync(
        AppDbContext context,
        DatabaseSchemaFixupProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await AddColumnIfMissingAsync(context, "ProviderAccounts", "ExpirationDate", "TEXT", cancellationToken).ConfigureAwait(false);

        await AddColumnIfMissingAsync(context, "Profiles", "CreatedAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Profiles", "PinHash", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Profiles", "PendingDeletionAt", "TEXT", cancellationToken).ConfigureAwait(false);

        await AddColumnIfMissingAsync(context, "Playlists", "EpgUrl", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Playlists", "DetectedCountry", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Playlists", "EpgLastUpdated", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Playlists", "EpgLastError", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Playlists", "SourceEtag", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Playlists", "SourceLastModified", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Playlists", "SourceContentLength", "INTEGER", cancellationToken).ConfigureAwait(false);

        await AddColumnIfMissingAsync(context, "Channels", "IsCompleted", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "CurrentProgramId", "INTEGER", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "Genre", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "ReleaseYear", "INTEGER", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "Rating", "REAL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "ContentRating", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "TmdbId", "INTEGER", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "LastTmdbSync", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "IsInMyList", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "IsFavorite", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "WatchedPosition", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "Country", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "Language", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "LastWatched", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "Plot", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "BackdropUrl", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "Cast", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "Director", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Channels", "Duration", "TEXT", cancellationToken).ConfigureAwait(false);

        await TryExecuteAsync(context, "CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Type_Group_Id ON Channels(PlaylistId, Type, GroupTitle, Id DESC);", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Type_Id ON Channels(PlaylistId, Type, Id DESC);", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Group_Id ON Channels(PlaylistId, GroupTitle, Id DESC);", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "CREATE INDEX IF NOT EXISTS IX_Channels_Playlist_Favorite_Id ON Channels(PlaylistId, IsFavorite, Id DESC);", cancellationToken).ConfigureAwait(false);

        await AddColumnIfMissingAsync(context, "Episodes", "IsCompleted", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Episodes", "IntroStartSec", "REAL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Episodes", "IntroEndSec", "REAL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Episodes", "CreditsStartSec", "REAL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Episodes", "AirDate", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Episodes", "TmdbEpisodeName", "TEXT", cancellationToken).ConfigureAwait(false);

        await AddColumnIfMissingAsync(context, "Series", "IsFavorite", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "IsInMyList", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "Genre", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "Plot", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "ReleaseYear", "INTEGER", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "Rating", "REAL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "ContentRating", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "PlaylistId", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "TmdbId", "INTEGER", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "TmdbTitle", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "LastTmdbSync", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "Cast", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "Director", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "BackdropUrl", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "TrailerUrl", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "MetadataFetchedAt", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "GroupTitle", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "NetworkName", "TEXT", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Series", "NetworkLogoUrl", "TEXT", cancellationToken).ConfigureAwait(false);

        await TryExecuteAsync(
            context,
            """
            UPDATE Series
            SET Plot = NULL, Cast = NULL, BackdropUrl = NULL, TrailerUrl = NULL, ContentRating = NULL, MetadataFetchedAt = NULL
            WHERE GroupTitle LIKE 'EU %' OR GroupTitle LIKE 'EU|%' OR GroupTitle = 'EU'
            """,
            cancellationToken).ConfigureAwait(false);

        await AddColumnIfMissingAsync(context, "Seasons", "TmdbSeasonId", "INTEGER", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "Seasons", "Plot", "TEXT", cancellationToken).ConfigureAwait(false);

        await TryExecuteAsync(
            context,
            """
            CREATE TABLE IF NOT EXISTS SeriesEpisodeProgresses (
                Id INTEGER NOT NULL CONSTRAINT PK_SeriesEpisodeProgresses PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                SeriesKey TEXT NOT NULL,
                SeriesTitle TEXT NOT NULL,
                SeasonNumber INTEGER NOT NULL,
                EpisodeNumber INTEGER NOT NULL,
                LastWatchedAt TEXT NOT NULL,
                StoppedAt TEXT NOT NULL,
                Duration TEXT NULL,
                Completed INTEGER NOT NULL DEFAULT 0,
                TmdbId INTEGER NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(context, "SeriesEpisodeProgresses", "TmdbId", "INTEGER", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "CREATE INDEX IF NOT EXISTS IX_SeriesEpisodeProgresses_ProfileId ON SeriesEpisodeProgresses(ProfileId);", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "CREATE UNIQUE INDEX IF NOT EXISTS IX_SeriesEpisodeProgresses_UniqueEpisode ON SeriesEpisodeProgresses(ProfileId, SeriesKey, SeasonNumber, EpisodeNumber);", cancellationToken).ConfigureAwait(false);

        await TryExecuteAsync(
            context,
            """
            CREATE TABLE IF NOT EXISTS DownloadItems (
                Id INTEGER NOT NULL CONSTRAINT PK_DownloadItems PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                PlaylistId INTEGER NOT NULL DEFAULT 0,
                ChannelId INTEGER NULL,
                EpisodeId INTEGER NULL,
                ChannelType INTEGER NOT NULL DEFAULT 1,
                DisplayName TEXT NOT NULL,
                PosterUrl TEXT NULL,
                SourceUrl TEXT NOT NULL,
                LocalFilePath TEXT NULL,
                TempFilePath TEXT NULL,
                AudioTracksJson TEXT NULL,
                SubtitleTracksJson TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                BytesDownloaded INTEGER NOT NULL DEFAULT 0,
                BytesTotal INTEGER NULL,
                SpeedBytesPerSecond REAL NOT NULL DEFAULT 0,
                EstimatedSecondsRemaining INTEGER NULL,
                ErrorMessage TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CompletedAt TEXT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "CREATE INDEX IF NOT EXISTS IX_DownloadItems_ProfileId ON DownloadItems(ProfileId);", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "CREATE INDEX IF NOT EXISTS IX_DownloadItems_Status ON DownloadItems(Status);", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "CREATE INDEX IF NOT EXISTS IX_DownloadItems_ProfileStatusCreated ON DownloadItems(ProfileId, Status, CreatedAt);", cancellationToken).ConfigureAwait(false);
        await RenameColumnIfPresentAsync(context, "DownloadItems", "LocalEncryptedPath", "LocalFilePath", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(
            context,
            "UPDATE DownloadItems SET Status = 4, ErrorMessage = 'Eski format. Lütfen tekrar indirin.' WHERE LocalFilePath LIKE '%.nctra' AND Status = 3;",
            cancellationToken).ConfigureAwait(false);

        await TryExecuteAsync(context, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, profile == DatabaseSchemaFixupProfile.Mobile ? "PRAGMA cache_size=-32000;" : "PRAGMA cache_size=-64000;", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, "PRAGMA temp_store=MEMORY;", cancellationToken).ConfigureAwait(false);
        await TryExecuteAsync(context, profile == DatabaseSchemaFixupProfile.Mobile ? "PRAGMA mmap_size=134217728;" : "PRAGMA mmap_size=268435456;", cancellationToken).ConfigureAwait(false);
    }

    private static async Task TryExecuteAsync(AppDbContext context, string sql, CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task AddColumnIfMissingAsync(
        AppDbContext context,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(context, tableName, cancellationToken).ConfigureAwait(false) ||
            await ColumnExistsAsync(context, tableName, columnName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var sql = $"ALTER TABLE {QuoteIdentifier(tableName)} ADD COLUMN {QuoteIdentifier(columnName)} {columnDefinition};";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RenameColumnIfPresentAsync(
        AppDbContext context,
        string tableName,
        string oldColumnName,
        string newColumnName,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(context, tableName, cancellationToken).ConfigureAwait(false) ||
            !await ColumnExistsAsync(context, tableName, oldColumnName, cancellationToken).ConfigureAwait(false) ||
            await ColumnExistsAsync(context, tableName, newColumnName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var sql = $"ALTER TABLE {QuoteIdentifier(tableName)} RENAME COLUMN {QuoteIdentifier(oldColumnName)} TO {QuoteIdentifier(newColumnName)};";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(AppDbContext context, string tableName, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $tableName LIMIT 1;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext context, string tableName, string columnName, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
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
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
