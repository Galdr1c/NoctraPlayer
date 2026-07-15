using Microsoft.EntityFrameworkCore;
using Noctra.Models;

namespace Noctra.Data;

/// <summary>
/// Uygulama veritabanı context'i
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<Channel> Channels { get; set; }
    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<Series> Series { get; set; }
    public DbSet<Season> Seasons { get; set; }
    public DbSet<Episode> Episodes { get; set; }
    public DbSet<EpgProgram> EpgPrograms { get; set; }
    public DbSet<Profile> Profiles { get; set; }
    public DbSet<ProviderAccount> ProviderAccounts { get; set; }
    public DbSet<WatchHistory> WatchHistories { get; set; }
    public DbSet<SeriesEpisodeProgress> SeriesEpisodeProgresses { get; set; }
    public DbSet<DownloadItem> DownloadItems { get; set; }
    public DbSet<ImportJob> ImportJobs { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Channel yapılandırması
        modelBuilder.Entity<Channel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.StreamUrl).IsRequired();
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.GroupTitle);
            entity.HasIndex(e => e.IsFavorite);
            entity.HasIndex(e => e.TmdbId);
            entity.HasIndex(e => new { e.PlaylistId, e.StreamUrl });
            entity.HasIndex(e => new { e.PlaylistId, e.Type, e.GroupTitle, e.Id });
            entity.HasIndex(e => new { e.PlaylistId, e.Type, e.Id });
            entity.HasIndex(e => new { e.PlaylistId, e.GroupTitle, e.Id });
            entity.HasIndex(e => new { e.PlaylistId, e.IsFavorite, e.Id });
        });

        // Playlist yapılandırması
        modelBuilder.Entity<Playlist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DetectedCountry).HasMaxLength(10);
            entity.HasMany(e => e.Channels)
                  .WithOne(e => e.Playlist)
                  .HasForeignKey(e => e.PlaylistId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Series>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.TmdbId);
            entity.HasIndex(e => e.PlaylistId);
            entity.HasMany(e => e.Seasons)
                  .WithOne(e => e.Series)
                  .HasForeignKey(e => e.SeriesId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Playlist)
                  .WithMany()
                  .HasForeignKey(e => e.PlaylistId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Season yapılandırması
        modelBuilder.Entity<Season>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TmdbSeasonId);
            entity.HasMany(e => e.Episodes)
                  .WithOne(e => e.Season)
                  .HasForeignKey(e => e.SeasonId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Episode yapılandırması
        modelBuilder.Entity<Episode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.StreamUrl).IsRequired();
        });

        // EpgProgram yapılandırması
        modelBuilder.Entity<EpgProgram>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ChannelId).IsRequired();
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => new { e.ChannelId, e.StartTime, e.EndTime });
        });

        // ProviderAccount Configuration
        modelBuilder.Entity<ProviderAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Url).IsRequired();
        });

        // Profile Configuration
        modelBuilder.Entity<Profile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            
            entity.HasOne(e => e.ProviderAccount)
                  .WithMany(p => p.Profiles)
                  .HasForeignKey(e => e.ProviderAccountId)
                  .OnDelete(DeleteBehavior.Cascade); // Delete account -> delete profiles

            entity.HasMany<Playlist>()
                  .WithOne(e => e.Profile)
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // WatchHistory Configuration
        modelBuilder.Entity<WatchHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Channel)
                  .WithMany()
                  .HasForeignKey(e => e.ChannelId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Episode)
                  .WithMany()
                  .HasForeignKey(e => e.EpisodeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SeriesEpisodeProgress>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SeriesKey).IsRequired().HasMaxLength(512);
            entity.Property(e => e.SeriesTitle).IsRequired().HasMaxLength(255);

            entity.HasOne(e => e.Profile)
                  .WithMany()
                  .HasForeignKey(e => e.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ProfileId);
            entity.HasIndex(e => new { e.ProfileId, e.SeriesKey, e.SeasonNumber, e.EpisodeNumber })
                  .IsUnique();
        });

        modelBuilder.Entity<DownloadItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.SourceUrl).IsRequired();
            entity.HasIndex(e => e.ProfileId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.ProfileId, e.Status, e.CreatedAt });
        });

        modelBuilder.Entity<ImportJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Stage).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => e.ProfileId);
            entity.HasIndex(e => e.PlaylistId);
            entity.HasIndex(e => new { e.ProfileId, e.Status, e.CreatedAt });
            entity.HasIndex(e => e.ProfileId)
                .HasDatabaseName("IX_ImportJobs_OneActivePerProfile")
                .IsUnique()
                .HasFilter("Status IN (0, 1) AND ProfileId IS NOT NULL");
        });
    }
}


