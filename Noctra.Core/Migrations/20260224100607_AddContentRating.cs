using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noctra.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddContentRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EpgPrograms_ChannelId",
                table: "EpgPrograms");

            migrationBuilder.DropIndex(
                name: "IX_EpgPrograms_StartTime_EndTime",
                table: "EpgPrograms");

            migrationBuilder.AddColumn<string>(
                name: "ContentRating",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EpgLastError",
                table: "Playlists",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceContentLength",
                table: "Playlists",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEtag",
                table: "Playlists",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceLastModified",
                table: "Playlists",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CreditsStartSec",
                table: "Episodes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "IntroEndSec",
                table: "Episodes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "IntroStartSec",
                table: "Episodes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentRating",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DownloadItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChannelType = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PosterUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    LocalFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    TempFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    AudioTracksJson = table.Column<string>(type: "TEXT", nullable: true),
                    SubtitleTracksJson = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    BytesDownloaded = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesTotal = table.Column<long>(type: "INTEGER", nullable: true),
                    SpeedBytesPerSecond = table.Column<double>(type: "REAL", nullable: false),
                    EstimatedSecondsRemaining = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeriesEpisodeProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SeriesTitle = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LastWatchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StoppedAt = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesEpisodeProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesEpisodeProgresses_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_ChannelId_StartTime_EndTime",
                table: "EpgPrograms",
                columns: new[] { "ChannelId", "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadItems_ProfileId",
                table: "DownloadItems",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadItems_ProfileId_Status_CreatedAt",
                table: "DownloadItems",
                columns: new[] { "ProfileId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadItems_Status",
                table: "DownloadItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesEpisodeProgresses_ProfileId",
                table: "SeriesEpisodeProgresses",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesEpisodeProgresses_ProfileId_SeriesKey_SeasonNumber_EpisodeNumber",
                table: "SeriesEpisodeProgresses",
                columns: new[] { "ProfileId", "SeriesKey", "SeasonNumber", "EpisodeNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadItems");

            migrationBuilder.DropTable(
                name: "SeriesEpisodeProgresses");

            migrationBuilder.DropIndex(
                name: "IX_EpgPrograms_ChannelId_StartTime_EndTime",
                table: "EpgPrograms");

            migrationBuilder.DropColumn(
                name: "ContentRating",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "EpgLastError",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SourceContentLength",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SourceEtag",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SourceLastModified",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "CreditsStartSec",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "IntroEndSec",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "IntroStartSec",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "ContentRating",
                table: "Channels");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_ChannelId",
                table: "EpgPrograms",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_StartTime_EndTime",
                table: "EpgPrograms",
                columns: new[] { "StartTime", "EndTime" });
        }
    }
}
