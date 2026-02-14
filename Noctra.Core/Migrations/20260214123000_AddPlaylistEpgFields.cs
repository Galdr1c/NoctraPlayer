using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Noctra.Data;

#nullable disable

namespace Noctra.Core.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260214123000_AddPlaylistEpgFields")]
    public partial class AddPlaylistEpgFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetectedCountry",
                table: "Playlists",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EpgLastUpdated",
                table: "Playlists",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedCountry",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "EpgLastUpdated",
                table: "Playlists");
        }
    }
}
