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

        // Series yapılandırması
        modelBuilder.Entity<Series>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasMany(e => e.Seasons)
                  .WithOne(e => e.Series)
                  .HasForeignKey(e => e.SeriesId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Season yapılandırması
        modelBuilder.Entity<Season>(entity =>
        {
            entity.HasKey(e => e.Id);
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
            entity.HasIndex(e => e.ChannelId);
            entity.HasIndex(e => new { e.StartTime, e.EndTime });
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
    }
}


