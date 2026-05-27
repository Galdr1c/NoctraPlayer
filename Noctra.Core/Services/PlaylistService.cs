using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Noctra.Core.Services;

namespace Noctra.Services;

/// <summary>
/// Playlist yönetim servisi
/// </summary>
public partial class PlaylistService : IPlaylistService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AddPlaylistLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, Dictionary<string, ChannelBackupData>> _refreshBackups = new();
    private readonly ConcurrentDictionary<int, byte> _linearStreamRepairCompleted = new();
    private record ChannelBackupData(bool Fav, bool List, TimeSpan? Pos, TimeSpan? Dur, bool Comp, DateTime? LastW);
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IM3UParser _parser;
    private readonly IMediaService _mediaService;
    private readonly IPlaylistOrganizerService _organizer;
    private readonly LanguageDetectionService _languageDetection;
    private readonly EpgSourceResolver _epgSourceResolver;
    private readonly IEpgService _epgService;
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;

    public PlaylistService(
        IDbContextFactory<AppDbContext> contextFactory, 
        IM3UParser parser, 
        IMediaService mediaService, 
        IPlaylistOrganizerService organizer,
        LanguageDetectionService languageDetection,
        EpgSourceResolver epgSourceResolver,
        IEpgService epgService,
        HttpClient httpClient,
        ISettingsService settingsService, ILocalizationService localizationService)
    {
        _contextFactory = contextFactory;
        _parser = parser;
        _mediaService = mediaService;
        _organizer = organizer;
        _languageDetection = languageDetection;
        _epgSourceResolver = epgSourceResolver;
        _epgService = epgService;
        _httpClient = httpClient;
        _settingsService = settingsService;
        _localizationService = localizationService;
    }

    public async Task<Playlist> AddFromUrlAsync(string name, string url, int? profileId = null)
    {
        var normalizedUrl = (url ?? string.Empty).Trim();
        var lockKey = $"{profileId?.ToString() ?? "null"}|{normalizedUrl}";
        var gate = AddPlaylistLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try 
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] AddFromUrlAsync: {name} - {url}");
            
            using var context = await _contextFactory.CreateDbContextAsync();
            // Check existing first to avoid potential parsing overhead
            var existing = await context.Playlists
                .FirstOrDefaultAsync(p => p.Url == normalizedUrl && p.ProfileId == profileId && p.IsActive);
                
            if (existing != null) 
            {
                if (existing.ChannelCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] Found existing playlist: {existing.Id} with {existing.ChannelCount} channels");
                    await EnsureLinearStreamChannelTypesRepairedOnceAsync(context, existing.Id);
                    
                    // YENİ: Child profile ise mevcut kanalları kontrol et ve temizle
                    var profile = await context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == profileId);
                    if (profile?.IsChild == true)
                    {
                        var channelsInDb = await context.Channels
                            .Where(c => c.PlaylistId == existing.Id)
                            .ToListAsync();
                            
                        var kept = ApplyChildFilter(channelsInDb.ToList());
                        var keptIds = new HashSet<int>(kept.Select(c => c.Id));
                        var toDeleteIds = channelsInDb
                            .Where(c => !keptIds.Contains(c.Id))
                            .Select(c => c.Id)
                            .ToList();
                            
                        if (toDeleteIds.Any())
                        {
                            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Purging {toDeleteIds.Count} non-compliant channels from EXISTING playlist for child profile.");
                            
                            // Batch deletion to avoid SQL parameter limits
                            const int deleteBatchSize = 500;
                            for (int i = 0; i < toDeleteIds.Count; i += deleteBatchSize)
                            {
                                var batch = toDeleteIds.Skip(i).Take(deleteBatchSize).ToList();
                                await context.Channels
                                    .Where(c => batch.Contains(c.Id))
                                    .ExecuteDeleteAsync();
                            }
                                
                            existing.ChannelCount = channelsInDb.Count - toDeleteIds.Count;
                            await context.SaveChangesAsync();
                        }
                        
                        // Deep metadata purge for child profile
                        await PurgeNonCompliantSeriesAsync(context, existing.Id, true);
                    }
                    
                    return existing;
                }
                
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] Found existing playlist {existing.Id} but it is EMPTY (0 channels). Forcing full refresh/re-add.");
                // Deactivate the empty one so we create a fresh one
                existing.IsActive = false;
                await context.SaveChangesAsync();
            }

            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Downloading and parsing M3U from: {normalizedUrl}");
            var channels = await _parser.ParseFromUrlAsync(normalizedUrl);
            var detectedEpgUrl = NormalizeEpgUrl(_parser.LastDetectedEpgUrl);
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Parsed {channels.Count} channels from M3U");

            if (channels.Count == 0)
            {
                throw new InvalidOperationException(_localizationService.GetString("Playlist.Error.ParseEmpty"));
            }

            // Otomatik organizasyon: dedup, kategorize, sıralama
            var organized = _organizer.Organize(channels);
            
            var isChild = false;
            if (profileId.HasValue) 
            {
                var profile = await context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == profileId);
                isChild = profile?.IsChild ?? false;
            }

            if (isChild)
            {
                organized = ApplyChildFilter(organized).ToList();
            }

            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Organized: {channels.Count} › {organized.Count} channels");

            return await AddFromChannelsAsync(name, normalizedUrl, organized, profileId, detectedEpgUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddFromUrlAsync error: {ex}");
            throw;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                if (AddPlaylistLocks.TryRemove(lockKey, out _))
                {
                    gate.Dispose();
                }
            }
        }
    }

    public async Task<Playlist> AddFromChannelsAsync(string name, string sourceUrl, IReadOnlyCollection<Channel> channels, int? profileId = null, string? detectedEpgUrl = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.Playlists
            .FirstOrDefaultAsync(p => p.Url == sourceUrl && p.ProfileId == profileId && p.IsActive);

        if (existing != null)
        {
            await EnsureLinearStreamChannelTypesRepairedOnceAsync(context, existing.Id);
            return existing;
        }

        var playlist = new Playlist
        {
            Name = name,
            Url = sourceUrl,
            ProfileId = profileId,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            ChannelCount = channels.Count,
            IsActive = true,
            EpgUrl = NormalizeEpgUrl(detectedEpgUrl)
        };

        context.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            // Otomatik organizasyon: dedup, kategorize, sıralama
            var organizedChannels = _organizer.Organize(channels.ToList());
            
            playlist.ChannelCount = organizedChannels.Count;
            context.Playlists.Add(playlist);
            await context.SaveChangesAsync();
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Created playlist with ID: {playlist.Id}");

            // Single-transaction bulk insert using Raw ADO.NET (extreme performance)
            foreach (var channel in organizedChannels)
            {
                channel.PlaylistId = playlist.Id;
            }
            await FastSqliteBulkInsertAsync(context, organizedChannels);
            InvalidateLinearStreamRepair(playlist.Id);

            // Fire-and-forget: aggregation runs in background, UI unblocked
            var aggregationPlaylistId = playlist.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _mediaService.AggregateContentAsync(aggregationPlaylistId);
                    _mediaService.RaiseAggregationCompleted(aggregationPlaylistId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] Background aggregation failed: {ex.Message}");
                }
            });

            // AUTO EPG in isolated scope to avoid DbContext cross-thread usage.
            var playlistId = playlist.Id;
            var channelSnapshot = channels.ToList();
            _ = Task.Run(async () =>
            {
                try
                {
                    var appLanguage = (_settingsService?.Settings?.Language ?? "tr").ToUpperInvariant();

                    var epgSources = _epgSourceResolver.ResolveEpgSources(
                        new List<string>(), // Country based detection removed with iptv-epg.org
                        providerEpgUrl: _epgSourceResolver.TryInferXtreamEpgUrl(sourceUrl),
                        m3uEpgUrl: NormalizeEpgUrl(detectedEpgUrl),
                        preferredLanguageCode: appLanguage);

                    for (var i = 0; i < epgSources.Count; i++)
                    {
                        epgSources[i].ClearBeforeLoad = (i == 0);
                    }

                    string? usedEpgUrl = null;
                    string? autoEpgError = null;

                    foreach (var source in epgSources)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoEPG] Loading from {source.Url}");
                            var loadedPrograms = await _epgService.LoadEpgAsync(source.Url, source.IsPrimary, channelSnapshot, clearBeforeSave: source.ClearBeforeLoad);
                            if (loadedPrograms > 0)
                            {
                                usedEpgUrl = source.Url;
                                autoEpgError = null;
                                System.Diagnostics.Debug.WriteLine($"[PlaylistService] EPG loaded from {source.Type} (+{loadedPrograms})");
                                break;
                            }

                            autoEpgError = $"{source.Type}: 0 program";
                            System.Diagnostics.Debug.WriteLine($"[PlaylistService] EPG source had no matches: {source.Type}");
                        }
                        catch (Exception ex)
                        {
                            autoEpgError = $"{source.Type}: {ex.Message}";
                            System.Diagnostics.Debug.WriteLine($"[PlaylistService] EPG source failed: {source.Type} - {ex.Message}");
                        }
                    }

                    if (usedEpgUrl == null && string.IsNullOrWhiteSpace(autoEpgError))
                    {
                        autoEpgError = _localizationService.GetString("Playlist.Error.NoAutoEpg");
                    }

                    using var db = await _contextFactory.CreateDbContextAsync();
                    var playlistToUpdate = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
                    if (playlistToUpdate != null)
                    {
                        playlistToUpdate.EpgLastError = autoEpgError;

                        if (!string.IsNullOrWhiteSpace(usedEpgUrl))
                        {
                            playlistToUpdate.EpgUrl = usedEpgUrl;
                            playlistToUpdate.EpgLastUpdated = DateTime.UtcNow;
                            playlistToUpdate.EpgLastError = null;
                        }

                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] Auto-EPG error: {ex.Message}");

                    try
                    {
                        using var db = await _contextFactory.CreateDbContextAsync();
                        var playlistToUpdate = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
                        if (playlistToUpdate != null)
                        {
                            playlistToUpdate.EpgLastError = UserFriendlyErrorMessage.FromException(ex);
                            await db.SaveChangesAsync();
                        }
                    }
                    catch
                    {
                    }
                }
            });

            return playlist;
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    /// <summary>
    /// Stalker aşamalı yükleme için boş playlist oluşturur.
    /// Kanallar sonradan AppendChannelsAsync ile eklenir.
    /// </summary>
    public async Task<Playlist> CreateEmptyPlaylistAsync(
        string name, string sourceUrl, int? profileId = null, string? epgUrl = null)
    {
        var lockKey = $"empty|{profileId?.ToString() ?? "null"}|{sourceUrl}";
        var gate = AddPlaylistLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            // Var olan aktif playlist'i kontrol et
            var existing = await context.Playlists
                .FirstOrDefaultAsync(p =>
                    p.Url == sourceUrl &&
                    p.IsActive &&
                    p.ProfileId == profileId);

            if (existing != null)
            {
                if (string.IsNullOrWhiteSpace(existing.EpgUrl) && !string.IsNullOrWhiteSpace(epgUrl))
                {
                    existing.EpgUrl = NormalizeEpgUrl(epgUrl);
                    await context.SaveChangesAsync();
                }
                return existing;
            }

            var playlist = new Playlist
            {
                Name         = name,
                Url          = sourceUrl,
                ProfileId    = profileId,
                IsActive     = true,
                ChannelCount = 0,
                CreatedAt    = DateTime.UtcNow,
                LastUpdated  = DateTime.UtcNow,
                EpgUrl       = NormalizeEpgUrl(epgUrl)
            };

            context.Playlists.Add(playlist);
            await context.SaveChangesAsync();

            return playlist;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                if (AddPlaylistLocks.TryRemove(lockKey, out _))
                {
                    gate.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Var olan bir playlist'e yeni kanallar ekler.
    /// Aşamalı yükleme sırasında her kategori bittiğinde çağrılır.
    /// </summary>
    public async Task AppendChannelsAsync(
        int playlistId, IReadOnlyCollection<Channel> channels)
    {
        if (channels.Count == 0) return;

        using var context = await _contextFactory.CreateDbContextAsync();

        // Otomatik organizasyon: dedup, kategorize, sıralama
        // Xtream/Stalker aşamalı yüklediği için sağlayıcı tiplerine güveniyoruz
        var organized = _organizer.Organize(channels.ToList(), trustProviderTypes: true);

        foreach (var channel in organized)
            channel.PlaylistId = playlistId;

        ApplyBackupData(playlistId, organized);

        // Mevcut FastSqliteBulkInsertAsync metodunu kullan
        await FastSqliteBulkInsertAsync(context, organized);
        InvalidateLinearStreamRepair(playlistId);

        // Kanal sayısını güncelle
        await context.Playlists
            .Where(p => p.Id == playlistId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ChannelCount,
                    p => context.Channels.Count(c => c.PlaylistId == p.Id))
                .SetProperty(p => p.LastUpdated, DateTime.UtcNow));
    }

    /// <summary>
    /// Geçici (Dummy) kanalları siler ve yerine gerçek kanalları ekler.
    /// Lazy loading mekanizmasında anlık kategori gösterimi için kullanılır.
    /// Idempotent (tekrar edilebilir) olması için gruba ait mevcut tüm kanalları silip yenilerini yazar.
    /// </summary>
    public async Task ReplaceDummyWithRealChannelsAsync(
        int playlistId, string groupTitle, IReadOnlyCollection<Channel> realChannels)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        // 1. Önce bu gruba ait SADECE geçici (dummy) kanalları temizle
        // Bu sayede aynı isme sahip farklı kategoriler (örn: Live/VOD Action) birbirini silmez, birleşir.
        await context.Channels
            .Where(c => c.PlaylistId == playlistId && 
                        c.GroupTitle == groupTitle && 
                        (c.StreamUrl.StartsWith("stalker-dummy://") || c.StreamUrl.StartsWith("xtream-dummy://")))
            .ExecuteDeleteAsync();

        // 2. Eğer eklenecek gerçek kanal varsa ekle
        if (realChannels.Count > 0)
        {
            // Dublör kanal oluşmaması için (sağlayıcı kanalı başka gruba taşımış olabilir):
            // Eklenen kanalların StreamUrl'leri bu playlist'in başka gruplarında varsa oradan sil.
            var streamUrls = realChannels.Select(rc => rc.StreamUrl).Where(u => !string.IsNullOrEmpty(u)).ToList();
            if (streamUrls.Any())
            {
                await context.Channels
                    .Where(c => c.PlaylistId == playlistId && streamUrls.Contains(c.StreamUrl))
                    .ExecuteDeleteAsync();
            }

            // Otomatik organizasyon: dedup, kategorize, sıralama
            // Xtream/Stalker aşamalı yüklediği için sağlayıcı tiplerine güveniyoruz
            var organized = _organizer.Organize(realChannels.ToList(), trustProviderTypes: true);

            foreach (var channel in organized)
                channel.PlaylistId = playlistId;

            ApplyBackupData(playlistId, organized);

            await FastSqliteBulkInsertAsync(context, organized);
            InvalidateLinearStreamRepair(playlistId);
        }

        // Kanal sayısını güncelle
        await context.Playlists
            .Where(p => p.Id == playlistId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ChannelCount,
                    p => context.Channels.Count(c => c.PlaylistId == p.Id))
                .SetProperty(p => p.LastUpdated, DateTime.UtcNow));
    }

    /// <summary>
    /// Stalker aşamalı yüklemesinde henüz indirilmemiş (geçici kanalı bulunan) kategorileri döndürür.
    /// </summary>
    public async Task<List<string>> GetPendingDummyGroupsAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Channels
            .AsNoTracking()
            .Where(c => c.PlaylistId == playlistId && (c.StreamUrl.StartsWith("stalker-dummy://") || c.StreamUrl.StartsWith("xtream-dummy://")) && c.GroupTitle != null)
            .Select(c => c.GroupTitle!)
            .Distinct()
            .ToListAsync();
    }

    public async Task DeleteAllDummiesAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await context.Channels
            .Where(c => c.PlaylistId == playlistId && (c.StreamUrl.StartsWith("stalker-dummy://") || c.StreamUrl.StartsWith("xtream-dummy://")))
            .ExecuteDeleteAsync();
        InvalidateLinearStreamRepair(playlistId);
            
        // Kanal sayısını güncelle
        await context.Playlists
            .Where(p => p.Id == playlistId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ChannelCount,
                    p => context.Channels.Count(c => c.PlaylistId == p.Id))
                .SetProperty(p => p.LastUpdated, DateTime.UtcNow));
    }

    /// <summary>
    /// Tam yenileme (Full Refresh) için playlist'e ait TÜM kanalları siler.
    /// Sunucudan başarılı yanıt geldikten sonra, yeni dummy kanallar eklenmeden önce çağrılır.
    /// </summary>
    public async Task DeleteAllChannelsForRefreshAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        
        // 1. MEVCUT KULLANICI VERİLERİNİ YEDEKLE (Favori, İzleme Geçmişi vb.)
        var existingChannelData = await context.Channels
            .AsNoTracking()
            .Where(c => c.PlaylistId == playlistId)
            .Select(c => new { c.Name, c.StreamUrl, c.GroupTitle, c.TvgId, c.TvgName, c.Type, c.IsFavorite, c.IsInMyList, c.WatchedPosition, c.Duration, c.IsCompleted, c.LastWatched })
            .ToListAsync();

        var userDataMap = new Dictionary<string, ChannelBackupData>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in existingChannelData)
        {
            var chStub = new Channel { Name = c.Name, StreamUrl = c.StreamUrl, GroupTitle = c.GroupTitle, TvgId = c.TvgId, TvgName = c.TvgName };
            var fingerprint = BuildChannelFingerprint(chStub);
            if (!userDataMap.TryGetValue(fingerprint, out var existing))
            {
                userDataMap[fingerprint] = new ChannelBackupData(c.IsFavorite, c.IsInMyList, c.WatchedPosition, c.Duration, c.IsCompleted, c.LastWatched);
            }
            else
            {
                userDataMap[fingerprint] = new ChannelBackupData(
                    existing.Fav || c.IsFavorite,
                    existing.List || c.IsInMyList,
                    (c.WatchedPosition > existing.Pos) ? c.WatchedPosition : existing.Pos,
                    (c.Duration > existing.Dur) ? c.Duration : existing.Dur,
                    existing.Comp || c.IsCompleted,
                    (c.LastWatched > existing.LastW) ? c.LastWatched : existing.LastW
                );
            }
        }
        
        _refreshBackups[playlistId] = userDataMap;

        // 2. TÜM KANALLARI SİL
        await context.Channels
            .Where(c => c.PlaylistId == playlistId)
            .ExecuteDeleteAsync();
        InvalidateLinearStreamRepair(playlistId);

        // Kanal sayısını sıfırla
        await context.Playlists
            .Where(p => p.Id == playlistId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ChannelCount, 0)
                .SetProperty(p => p.LastUpdated, DateTime.UtcNow));
    }

    private void ApplyBackupData(int playlistId, IEnumerable<Channel> channels)
    {
        if (_refreshBackups.TryGetValue(playlistId, out var backupMap))
        {
            foreach (var nc in channels)
            {
                var fingerprint = BuildChannelFingerprint(nc);
                if (backupMap.TryGetValue(fingerprint, out var data))
                {
                    nc.IsFavorite = data.Fav;
                    nc.IsInMyList = data.List;
                    nc.WatchedPosition = data.Pos;
                    nc.Duration = data.Dur;
                    nc.IsCompleted = data.Comp;
                    nc.LastWatched = data.LastW;
                }
            }
        }
    }

    private async Task EnsureLinearStreamChannelTypesRepairedOnceAsync(AppDbContext context, int playlistId)
    {
        if (!_linearStreamRepairCompleted.TryAdd(playlistId, 0))
        {
            return;
        }

        try
        {
            var repaired = await RepairLinearStreamChannelTypesAsync(context, playlistId);
            if (repaired > 0)
            {
                await _mediaService.AggregateContentAsync(playlistId);
            }
        }
        catch
        {
            _linearStreamRepairCompleted.TryRemove(playlistId, out _);
            throw;
        }
    }

    private void InvalidateLinearStreamRepair(int playlistId)
    {
        _linearStreamRepairCompleted.TryRemove(playlistId, out _);
    }

    private static async Task<int> RepairLinearStreamChannelTypesAsync(AppDbContext context, int playlistId)
    {
        using var trace = PerformanceTraceService.Shared?.BeginOperation("DB", "RepairLinearStreamChannelTypesAsync", $"playlist={playlistId}");
        var liveType = (int)ChannelType.Live;
        var linearRepaired = await context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE Channels
SET Type = {liveType}
WHERE PlaylistId = {playlistId}
  AND Type <> {liveType}
  AND StreamUrl IS NOT NULL
  AND lower(StreamUrl) NOT LIKE '%/movie/%'
  AND lower(StreamUrl) NOT LIKE '%/vod/%'
  AND lower(StreamUrl) NOT LIKE '%/series/%'
  AND lower(StreamUrl) NOT LIKE '%/tv_show/%'
  AND lower(StreamUrl) NOT LIKE '%type=vod%'
  AND lower(StreamUrl) NOT LIKE '%type=movie%'
  AND lower(StreamUrl) NOT LIKE '%type=series%'
  AND (
      lower(StreamUrl) LIKE '%.m3u8'
      OR lower(StreamUrl) LIKE '%.m3u8?%'
      OR lower(StreamUrl) LIKE '%.ts'
      OR lower(StreamUrl) LIKE '%.ts?%'
      OR lower(StreamUrl) LIKE '%.m3u'
      OR lower(StreamUrl) LIKE '%.m3u?%'
      OR lower(StreamUrl) LIKE '%format=m3u8%'
      OR lower(StreamUrl) LIKE '%extension=m3u8%'
      OR lower(StreamUrl) LIKE '%extension=ts%'
  );");

        var seriesKeywordRepaired = await context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE Channels
SET Type = {liveType}
WHERE PlaylistId = {playlistId}
  AND Type <> {liveType}
  AND GroupTitle IS NOT NULL
  AND (
      lower(GroupTitle) LIKE '%dizi%'
      OR lower(GroupTitle) LIKE '%series%'
      OR lower(GroupTitle) LIKE '%koleksiyon%'
  )
  AND (
      StreamUrl IS NULL
      OR (
          lower(StreamUrl) NOT LIKE 'xtream-series://%'
          AND lower(StreamUrl) NOT LIKE 'stalker-series://%'
          AND lower(StreamUrl) NOT LIKE '%/series/%'
          AND lower(StreamUrl) NOT LIKE '%/tv_show/%'
          AND lower(StreamUrl) NOT LIKE '%type=series%'
      )
  )
  AND lower(Name) NOT GLOB '*s[0-9][0-9]e[0-9][0-9]*'
  AND lower(Name) NOT GLOB '*[0-9]x[0-9][0-9]*'
  AND lower(Name) NOT LIKE '%sezon%'
  AND lower(Name) NOT LIKE '%season%'
  AND lower(Name) NOT LIKE '%bölüm%'
  AND lower(Name) NOT LIKE '%bolum%'
  AND lower(Name) NOT LIKE '%episode%';");

        var repaired = linearRepaired + seriesKeywordRepaired;

        if (repaired > 0)
        {
            PerformanceTraceService.Shared?.Counter("DB", "LinearStreamChannelTypesRepaired", repaired, $"playlist={playlistId}");
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Repaired {repaired} linear stream channel type(s) to Live for playlist {playlistId}.");
        }

        return repaired;
    }

    private static bool ShouldForceLiveFromStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        var lowerUrl = streamUrl.Trim().ToLowerInvariant();
        if (lowerUrl.Contains("/movie/") ||
            lowerUrl.Contains("/vod/") ||
            lowerUrl.Contains("/series/") ||
            lowerUrl.Contains("/tv_show/") ||
            lowerUrl.Contains("type=vod") ||
            lowerUrl.Contains("type=movie") ||
            lowerUrl.Contains("type=series"))
        {
            return false;
        }

        var path = lowerUrl;
        var q = path.IndexOf('?');
        if (q >= 0)
        {
            path = path[..q];
        }

        return path.EndsWith(".m3u8") ||
               path.EndsWith(".ts") ||
               path.EndsWith(".m3u") ||
               lowerUrl.Contains("format=m3u8") ||
               lowerUrl.Contains("extension=m3u8") ||
               lowerUrl.Contains("extension=ts");
    }

    public Task ClearRefreshBackupAsync(int playlistId)
    {
        _refreshBackups.TryRemove(playlistId, out _);
        return Task.CompletedTask;
    }

    public async Task<Playlist> AddFromFileAsync(string name, string filePath, int? profileId = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        try 
        {
            var rawChannels = await _parser.ParseFromFileAsync(filePath);
            var detectedEpgUrl = NormalizeEpgUrl(_parser.LastDetectedEpgUrl);
            var channels = _organizer.Organize(rawChannels);

            var isChild = false;
            if (profileId.HasValue) 
            {
                var profile = await context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == profileId);
                isChild = profile?.IsChild ?? false;
            }

            if (isChild)
            {
                channels = ApplyChildFilter(channels).ToList();
                
                // Consistency check: if for some reason this file was already added to this profile, clean up existing data
                var existing = await context.Playlists
                    .Include(p => p.Channels)
                    .FirstOrDefaultAsync(p => p.FilePath == filePath && p.ProfileId == profileId && p.IsActive);
                    
                if (existing != null)
                {
                    var kept = ApplyChildFilter(existing.Channels.ToList());
                    var keptIds = new HashSet<int>(kept.Select(c => c.Id));
                    var toDeleteIds = existing.Channels
                        .Where(c => !keptIds.Contains(c.Id))
                        .Select(c => c.Id)
                        .ToList();

                    if (toDeleteIds.Any())
                    {
                        // Batch deletion to avoid SQL parameter limits
                        const int deleteBatchSize = 500;
                        for (int i = 0; i < toDeleteIds.Count; i += deleteBatchSize)
                        {
                            var batch = toDeleteIds.Skip(i).Take(deleteBatchSize).ToList();
                            await context.Channels
                                .Where(c => batch.Contains(c.Id))
                                .ExecuteDeleteAsync();
                        }
                            
                        existing.ChannelCount = existing.Channels.Count - toDeleteIds.Count;
                        await context.SaveChangesAsync();
                    }
                    
                    // Deep metadata purge for child profile
                    await PurgeNonCompliantSeriesAsync(context, existing.Id, true);
                }
            }
            
            // Otomatik organizasyon: dedup, kategorize, sıralama
            var organized = _organizer.Organize(channels.ToList());

            var playlist = new Playlist
            {
                Name = name,
                FilePath = filePath,
                ProfileId = profileId,
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                ChannelCount = organized.Count,
                IsActive = true,
                EpgUrl = detectedEpgUrl
            };

            context.ChangeTracker.AutoDetectChangesEnabled = false;
            context.Playlists.Add(playlist);
            await context.SaveChangesAsync();

            // Single-transaction bulk insert using Raw ADO.NET (extreme performance)
            foreach (var channel in organized)
            {
                channel.PlaylistId = playlist.Id;
            }
            await FastSqliteBulkInsertAsync(context, organized);
            InvalidateLinearStreamRepair(playlist.Id);

            // Fire-and-forget: aggregation runs in background, UI unblocked
            var fileAggregationPlaylistId = playlist.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _mediaService.AggregateContentAsync(fileAggregationPlaylistId);
                    _mediaService.RaiseAggregationCompleted(fileAggregationPlaylistId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] Background aggregation (file) failed: {ex.Message}");
                }
            });

            return playlist;
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task<List<Playlist>> GetAllAsync(int? profileId = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.Playlists.Where(p => p.IsActive);
        
        if (profileId.HasValue)
        {
            query = query.Where(p => p.ProfileId == profileId);
        }
        
        return await query
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Playlist> RefreshAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        // Load playlist with Profile only — channels loaded via lightweight query below
        var playlist = await context.Playlists
            .Include(p => p.Profile)
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist == null)
            throw new KeyNotFoundException(string.Format(_localizationService.GetString("Playlist.Error.NotFound"), playlistId));

        RemotePlaylistMetadata? latestRemoteMetadata = null;
        if (!string.IsNullOrWhiteSpace(playlist.Url))
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                latestRemoteMetadata = await TryFetchRemoteMetadataAsync(playlist.Url, playlist, cts.Token);
                if (latestRemoteMetadata?.IsUnchanged == true)
                {
                    playlist.LastUpdated = DateTime.UtcNow;
                    UpdatePlaylistSourceMetadata(playlist, latestRemoteMetadata);
                    context.Playlists.Update(playlist);
                    await context.SaveChangesAsync();

                    if (playlist.Profile?.IsChild == true)
                    {
                        // Ensure existing content is safe even if metadata hasn't changed
                        await EnsureChildProfileCleanedAsync(context, playlist);
                    }

                    return playlist;
                }
            }
            catch (Exception ex)
            {
                // Log and continue to attempt regular refresh if HEAD request fails
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] Remote metadata fetch failed: {ex.Message}");
            }
        }

        // Yeni kanalları parse et
        List<Channel> newChannels;
        if (!string.IsNullOrEmpty(playlist.Url))
        {
            newChannels = await _parser.ParseFromUrlAsync(playlist.Url);
        }
        else if (!string.IsNullOrEmpty(playlist.FilePath))
        {
            newChannels = await _parser.ParseFromFileAsync(playlist.FilePath);
        }
        else
        {
            throw new InvalidOperationException(_localizationService.GetString("Playlist.Error.NoSource"));
        }

        // Organizasyon pipeline'ı uygula
        var organizedChannels = _organizer.Organize(newChannels);
        
        if (organizedChannels.Count == 0 && newChannels.Count > 0)
        {
            // All channels were deduped/filtered out but original list wasn't empty. This is likely a profile filter issue.
        }
        else if (organizedChannels.Count == 0)
        {
            throw new InvalidOperationException(_localizationService.GetString("Playlist.Error.EmptyNoDelete"));
        }

        if (playlist.Profile?.IsChild == true)
        {
            organizedChannels = ApplyChildFilter(organizedChannels).ToList();
        }

        // 1. MEVCUT KULLANICI VERİLERİNİ YEDEKLE (Favori, İzleme Geçmişi vb.)
        // Fingerprint -> (IsFavorite, IsInMyList, WatchedPosition, Duration, IsCompleted, LastWatched)
        var existingChannelData = await context.Channels
            .Where(c => c.PlaylistId == playlistId)
            .Select(c => new { c.Name, c.StreamUrl, c.GroupTitle, c.TvgId, c.TvgName, c.Type, c.IsFavorite, c.IsInMyList, c.WatchedPosition, c.Duration, c.IsCompleted, c.LastWatched })
            .ToListAsync();

        var userDataMap = new Dictionary<string, (bool Fav, bool List, TimeSpan? Pos, TimeSpan? Dur, bool Comp, DateTime? LastW)>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in existingChannelData)
        {
            var chStub = new Channel { Name = c.Name, StreamUrl = c.StreamUrl, GroupTitle = c.GroupTitle, TvgId = c.TvgId, TvgName = c.TvgName };
            var fingerprint = BuildChannelFingerprint(chStub);
            if (!userDataMap.TryGetValue(fingerprint, out var existing))
            {
                userDataMap[fingerprint] = (c.IsFavorite, c.IsInMyList, c.WatchedPosition, c.Duration, c.IsCompleted, c.LastWatched);
            }
            else
            {
                // Parmak izi çakışmasında verileri birleştir: Herhangi biri favori ise favori kalsın, 
                // ilerleme bilgisinde ise en ileride olanı baz al.
                // LastWatched: en güncel tarihi koru.
                userDataMap[fingerprint] = (
                    existing.Fav || c.IsFavorite,
                    existing.List || c.IsInMyList,
                    (c.WatchedPosition > existing.Pos) ? c.WatchedPosition : existing.Pos,
                    (c.Duration > existing.Dur) ? c.Duration : existing.Dur,
                    existing.Comp || c.IsCompleted,
                    (c.LastWatched > existing.LastW) ? c.LastWatched : existing.LastW
                );
            }
        }

        // 2. TÜM KANALLARI SİL (Temiz bir başlangıç için)
        // Cascade silme kuralları gereği WatchHistory.ChannelId null'a çekilecek (SetNull), veri kaybı yaşanmayacak.
        await context.Channels
            .Where(c => c.PlaylistId == playlistId)
            .ExecuteDeleteAsync();
        InvalidateLinearStreamRepair(playlistId);

        // 3. YENİ KANALLARA YEDEK VERİLERİ UYGULA
        foreach (var nc in organizedChannels)
        {
            var fingerprint = BuildChannelFingerprint(nc);
            if (userDataMap.TryGetValue(fingerprint, out var data))
            {
                nc.IsFavorite = data.Fav;
                nc.IsInMyList = data.List;
                nc.WatchedPosition = data.Pos;
                nc.Duration = data.Dur;
                nc.IsCompleted = data.Comp;
                nc.LastWatched = data.LastW;
            }
            nc.PlaylistId = playlist.Id;
        }

        // 4. TOPLU EKLEME
        if (organizedChannels.Count > 0)
        {
            await FastSqliteBulkInsertAsync(context, organizedChannels);
            InvalidateLinearStreamRepair(playlist.Id);
        }

        var finalCount = await context.Channels.CountAsync(c => c.PlaylistId == playlist.Id);
        playlist.ChannelCount = finalCount;
        playlist.LastUpdated = DateTime.UtcNow;
        if (latestRemoteMetadata != null)
        {
            UpdatePlaylistSourceMetadata(playlist, latestRemoteMetadata);
        }
        context.Playlists.Update(playlist);
        await context.SaveChangesAsync();

        // Fire-and-forget: re-aggregate in background
        var refreshAggregationPlaylistId = playlist.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                await _mediaService.AggregateContentAsync(refreshAggregationPlaylistId);
                _mediaService.RaiseAggregationCompleted(refreshAggregationPlaylistId);
            }
            catch (Exception ex)
            {
                await LogDetailedErrorAsync("RefreshAsync_Aggregation", ex);
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] Background aggregation (refresh) failed: {ex.Message}");
            }
        });

        return playlist;
    }

    private static Task LogDetailedErrorAsync(string context, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[RefreshError] {context}: {ex}");
        return Task.CompletedTask;
    }
    public async Task DeleteAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var playlist = await context.Playlists
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist != null)
        {
            playlist.IsActive = false;
            InvalidateLinearStreamRepair(playlistId);
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<Channel>> GetChannelsAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await EnsureLinearStreamChannelTypesRepairedOnceAsync(context, playlistId);
        return await context.Channels
            .AsNoTracking()
            .Where(c => c.PlaylistId == playlistId)
            .OrderBy(c => c.GroupTitle)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Get channels with filtering and pagination for fast loading
    /// </summary>
    public async Task<List<Channel>> GetChannelsFilteredAsync(int playlistId, string? searchText = null, string? group = null, ChannelType? type = null, bool onlyFavorites = false, int limit = 1000, ChannelSortOrder sortOrder = ChannelSortOrder.NewestFirst, List<string>? hiddenGroups = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await EnsureLinearStreamChannelTypesRepairedOnceAsync(context, playlistId);
        var query = BuildFilteredChannelQuery(context, playlistId, searchText, group, type, onlyFavorites, hiddenGroups);

        return await ApplySort(query, sortOrder)
            .Take(limit)
            .ToListAsync();
    }
    public async Task<List<Channel>> GetChannelsFilteredPageAsync(int playlistId, int skip, int take, string? searchText = null, string? group = null, ChannelType? type = null, bool onlyFavorites = false, ChannelSortOrder sortOrder = ChannelSortOrder.NewestFirst, List<string>? hiddenGroups = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await EnsureLinearStreamChannelTypesRepairedOnceAsync(context, playlistId);
        var query = BuildFilteredChannelQuery(context, playlistId, searchText, group, type, onlyFavorites, hiddenGroups);

        return await ApplySort(query, sortOrder)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(1, take))
            .ToListAsync();
    }

    public async Task<List<string>> GetGroupsAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Channels
            .AsNoTracking()
            .Where(c => c.PlaylistId == playlistId && c.GroupTitle != null)
            .Select(c => c.GroupTitle!.Trim())
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();
    }

    public async Task<List<string>> GetGroupsByTypeAsync(int playlistId, ChannelType type)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await EnsureLinearStreamChannelTypesRepairedOnceAsync(context, playlistId);
        return await context.Channels
            .AsNoTracking()
            .Where(c => c.PlaylistId == playlistId && c.Type == type && !string.IsNullOrEmpty(c.GroupTitle))
            .Select(c => c.GroupTitle!)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();
    }

    public async Task<(int TotalCount, List<string> AllGroups, List<string> LiveGroups, List<string> VodGroups, List<string> SeriesGroups)> GetChannelGroupMetadataAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await EnsureLinearStreamChannelTypesRepairedOnceAsync(context, playlistId);
        
        // Toplam kanal sayısı (GroupTitle null/boş olanlar dahil) — hafif COUNT sorgusu
        var totalCount = await context.Channels
            .AsNoTracking()
            .Where(c => c.PlaylistId == playlistId)
            .CountAsync();

        // SELECT GroupTitle, Type, COUNT(*) FROM Channels
        // WHERE PlaylistId=? AND GroupTitle IS NOT NULL AND GroupTitle != ''
        // GROUP BY GroupTitle, Type
        // Tüm satırları çekmek yerine sadece eşsiz (GroupTitle, Type) kombinasyonlarını alır.
        var groupData = await context.Channels
            .AsNoTracking()
            .Where(c => c.PlaylistId == playlistId && c.GroupTitle != null && c.GroupTitle != "")
            .GroupBy(c => new { c.GroupTitle, c.Type })
            .Select(g => new { GroupTitle = g.Key.GroupTitle!, g.Key.Type })
            .ToListAsync();

        var allGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var liveGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vodGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seriesGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in groupData)
        {
            var group = item.GroupTitle.Trim();
            allGroups.Add(group);
            
            switch (item.Type)
            {
                case ChannelType.Live:
                    liveGroups.Add(group);
                    break;
                case ChannelType.VOD:
                    vodGroups.Add(group);
                    break;
                case ChannelType.Series:
                    seriesGroups.Add(group);
                    break;
            }
        }

        return (
            totalCount,
            allGroups.OrderBy(g => g).ToList(),
            liveGroups.OrderBy(g => g).ToList(),
            vodGroups.OrderBy(g => g).ToList(),
            seriesGroups.OrderBy(g => g).ToList()
        );
    }

    /// <summary>
    /// Get channel count without loading all channels
    /// </summary>
    public async Task<int> GetChannelCountAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Channels
            .Where(c => c.PlaylistId == playlistId)
            .CountAsync();
    }

    private static IOrderedQueryable<Channel> ApplySort(IQueryable<Channel> query, ChannelSortOrder sortOrder)
    {
        return sortOrder switch
        {
            ChannelSortOrder.OldestFirst => query.OrderBy(c => c.Id),
            ChannelSortOrder.NameAsc => query.OrderBy(c => c.Name).ThenBy(c => c.Id),
            ChannelSortOrder.NameDesc => query.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id),
            _ => query.OrderByDescending(c => c.Id)
        };
    }

    private IQueryable<Channel> BuildFilteredChannelQuery(AppDbContext context, int playlistId, string? searchText, string? group, ChannelType? type, bool onlyFavorites, List<string>? hiddenGroups = null)
    {
        var query = context.Channels
            .AsNoTracking()
            .Where(c => c.PlaylistId == playlistId);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(search) ||
                                     (c.GroupTitle != null && c.GroupTitle.ToLower().Contains(search)));
        }

        if (!string.IsNullOrEmpty(group))
        {
            var normalizedGroup = group.Trim();
            query = query.Where(c =>
                c.GroupTitle != null &&
                (c.GroupTitle == normalizedGroup || c.GroupTitle.Trim() == normalizedGroup));
        }

        if (type.HasValue)
        {
            query = query.Where(c => c.Type == type.Value);
        }

        if (onlyFavorites)
        {
            query = query.Where(c => c.IsFavorite);
        }

        if (hiddenGroups != null && hiddenGroups.Count > 0)
        {
            query = query.Where(c => c.GroupTitle == null || !hiddenGroups.Contains(c.GroupTitle));
        }

        return query;
    }
    public async Task UpdateProviderExpirationAsync(int providerId, DateTime expirationDate)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var account = await context.ProviderAccounts.FindAsync(providerId);
        if (account != null)
        {
            account.ExpirationDate = expirationDate;
            await context.SaveChangesAsync();
        }
    }

    public async Task ClearProviderExpirationAsync(int providerId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var account = await context.ProviderAccounts.FindAsync(providerId);
        if (account != null)
        {
            account.ExpirationDate = null;
            await context.SaveChangesAsync();
        }
    }

    private static string BuildChannelFingerprint(Channel channel)
    {
        var tvgId = NormalizeIdentityToken(channel.TvgId);
        if (!string.IsNullOrWhiteSpace(tvgId))
        {
            return $"tvgid|{tvgId}";
        }

        var tvgName = NormalizeIdentityToken(channel.TvgName);
        var name = NormalizeIdentityToken(channel.Name);
        var group = NormalizeIdentityToken(channel.GroupTitle);

        if (!string.IsNullOrWhiteSpace(tvgName))
        {
            return $"tvgname|{tvgName}|{group}";
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return $"name|{name}|{group}";
        }

        var streamPath = NormalizeStreamIdentity(channel.StreamUrl);
        return $"stream|{streamPath}";
    }

    private static string NormalizeIdentityToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return string.Join(" ", raw
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeForFilter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        return text.ToLowerInvariant()
            .Replace("ş", "s")
            .Replace("ç", "c")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ö", "o")
            .Replace("ı", "i")
            .Replace("i̇", "i"); // Handle potential combined characters
    }

    private static bool ContainsAny(string? text, string[] words)
    {
        if (string.IsNullOrWhiteSpace(text) || words == null || words.Length == 0) return false;

        var normalizedText = NormalizeForFilter(text);
        
        // Use Regex with word boundaries for more accurate matching (prevents false positives like 'adam' in 'madam')
        // We compile the regex list or cache it if performance becomes an issue, but for now, simple loop or combined regex.
        foreach (var word in words)
        {
            var normalizedWord = NormalizeForFilter(word);
            if (string.IsNullOrWhiteSpace(normalizedWord)) continue;

            // Use \b for word boundaries. Note: \b might not handle non-ascii perfectly, 
            // but after normalization to latin chars, it works well.
            if (Regex.IsMatch(normalizedText, $@"\b{Regex.Escape(normalizedWord)}\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyCollection<Channel> ApplyChildFilter(IReadOnlyCollection<Channel> channels)
    {
        var safeCategories = ChildSafetyHelper.GetSafeCategories();
        var dangerousCategories = ChildSafetyHelper.GetDangerousCategories();
        var criticalBlacklist = ChildSafetyHelper.GetCriticalBlacklist();
        var kidFriendlyTitles = ChildSafetyHelper.GetKidFriendlyTitles();

        var filtered = channels.Where(c => 
        {
            // 1. KESİN RED (Hard Blacklist)
            if (ContainsAny(c.Name, criticalBlacklist) || ContainsAny(c.GroupTitle, criticalBlacklist))
                return false;

            string category = c.GroupTitle ?? string.Empty;
            string title = c.Name ?? string.Empty;

            // 1.5. KID-FRIENDLY CHECK (Öncelik: Bariz çocuk içeriği her durumda geçsin)
            // IsOldContent kontrolünden ÖNCE gelmeli ki Toy Story (1995) gibi çocuk klasikleri engellenmesin.
            bool isKidFriendlyByName = ContainsAny(title, kidFriendlyTitles);
            bool isKidFriendlyByCategory = ContainsAny(category, kidFriendlyTitles);
            
            if (isKidFriendlyByName || isKidFriendlyByCategory)
            {
                // Bariz çocuk içeriği: ContentRating'e bakılmaksızın izin ver
                return true;
            }

            // 1.6. TARİH BAZLI ENGELLEME (2000 ve öncesi)
            // Bu noktaya geldiyse bariz çocuk içeriği değil, güvenle engelleyebiliriz.
            if (ChildSafetyHelper.IsOldContent(title) || ChildSafetyHelper.IsOldContent(category))
                return false;

            // 1.7. SERTİFİKA BAZLI FİLTRELEME (Yaş Sınırı - TMDB)
            // Eğer metadata'dan gelen bir sertifika varsa, kelime bazlı tahminden daha güvenilirdir.
            if (!string.IsNullOrEmpty(c.ContentRating))
            {
                if (!ChildSafetyHelper.IsSafeRating(c.ContentRating))
                    return false;
                
                // Eğer sertifika kesin güvenliyse (G, TV-Y vb.) ve ana kara listeye girmiyorsa izin ver.
                if (ChildSafetyHelper.IsSafeRating(c.ContentRating))
                    return true;
            }

            // 2. KATEGORİ SINIFLANDIRMASI
            bool isExplicitlySafe = ContainsAny(category, safeCategories);
            bool isExplicitlyDangerous = ContainsAny(category, dangerousCategories);

            // "Sinema", "Dizi", "Netflix" gibi genel/şüpheli kategoriler
            bool isSuspectCategory = ContainsAny(category, new[] { "sinema", "cinema", "dizi", "series", "vod", "film", "favori", "izle", "aksiyon", "action", "macera", "adventure", "drama", "netflix", "prime", "disney+", "hbo", "starz" });

            // 3. KARAR MANTIĞI
            if (isExplicitlyDangerous) return false;

            if (isExplicitlySafe)
            {
                // Güvenli kategorideyse, ekstra bir kara liste kontrolüyle izin ver
                return true; 
            }

            if (isSuspectCategory || string.IsNullOrWhiteSpace(category))
            {
                // Şüpheli bir kategori veya kategori yoksa: Sadece adı güvenli olanlara izin ver
                // (Örn: Sinema kategorisindeki "Kayıp Balık Nemo" geçsin, ama "Titanic" geçmesin)
                return ContainsAny(title, kidFriendlyTitles);
            }

            // Diğer her şey (Belirsiz): Güvenlik için reddet
            return false;
        }).ToList();
        
        System.Diagnostics.Debug.WriteLine($"[PlaylistService] Category-centric filtering: {channels.Count} -> {filtered.Count} channels retained.");
        return filtered;
    }

    private static async Task PurgeNonCompliantSeriesAsync(AppDbContext context, int playlistId, bool isChild)
    {
        // YENİ: Dizi (Series) ve Bölüm (Episode) tablolarını da temizle.
        // Kanallar silinse bile geçmişten kalan agrege edilmiş dizi meta verileri UI'da görünmeye devam edebilir.
            
        if (!isChild) return;

        // 1. Önce çocuk filtresine uymayan tüm dizileri (Series) bul
        var allSeries = await context.Series
            .Where(s => s.PlaylistId == playlistId)
            .ToListAsync();

        var seriesToDelete = allSeries.Where(s => 
        {
            var criticalBlacklist = ChildSafetyHelper.GetCriticalBlacklist();
            var kidFriendlyTitles = ChildSafetyHelper.GetKidFriendlyTitles();
            var safeCategories = ChildSafetyHelper.GetSafeCategories();

            // 1. KESİN RED
            if (ContainsAny(s.Name, criticalBlacklist) || ContainsAny(s.Genre, criticalBlacklist))
                return true;

            string category = s.Genre ?? string.Empty;
            string title = s.Name ?? string.Empty;

            // 1.5. KID-FRIENDLY CHECK (Öncelik: Bariz çocuk içeriği her durumda kalsın)
            bool isKidFriendlyByName = ContainsAny(title, kidFriendlyTitles);
            bool isKidFriendlyByCategory = ContainsAny(category, kidFriendlyTitles);
            
            if (isKidFriendlyByName || isKidFriendlyByCategory)
            {
                return false; // Silme, bariz çocuk içeriği
            }

            // 1.6. TARİH BAZLI ENGELLEME
            if (ChildSafetyHelper.IsOldContent(title) || ChildSafetyHelper.IsOldContent(category)) return true;

            // 1.7. SERTİFİKA BAZLI FİLTRELEME
            if (!string.IsNullOrEmpty(s.ContentRating))
            {
                if (!ChildSafetyHelper.IsSafeRating(s.ContentRating)) return true; // Tehlikeli -> Sil
            }

            bool isExplicitlySafe = ContainsAny(category, safeCategories);
            bool isSuspectCategory = ContainsAny(category, new[] { "sinema", "cinema", "dizi", "series", "vod", "film", "favori", "izle", "aksiyon", "action", "macera", "adventure", "drama", "netflix", "prime", "disney+", "hbo" });

            if (isExplicitlySafe) return false;

            if (isSuspectCategory || string.IsNullOrWhiteSpace(category))
            {
                // Şüpheli kategori veya boş: İsim kurtarma listesinde yoksa sil
                return !ContainsAny(title, kidFriendlyTitles);
            }

            return true; // Belirsiz -> Korkuluk olarak sil
        }).ToList();

        if (seriesToDelete.Any())
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Purging {seriesToDelete.Count} non-compliant SERIES from child profile.");
            
            // ExecuteDeleteAsync mixed with tracking often causes conflicts or FK errors
            // if the full chain (Series -> Season -> Episode -> Progress) is not handled by raw SQL.
            // RemoveRange handles tracking and cascades safely through EF Core.
            context.Series.RemoveRange(seriesToDelete);
            await context.SaveChangesAsync();
        }
        
        // 2. Öksüz kalmış (hiç bölümü olmayan veya uygunsuz görünen) Bölümleri de temizle 
        // Not: ExecuteDeleteAsync Cascade silmeyi tetiklemeyebilir, o yüzden Series silindiğinde
        // Season ve Episode'lar da veri tabanı yapılandırmasına göre silinmeli. 
        // AppDbContext'te OnDelete(DeleteBehavior.Cascade) olduğu için Series silinince Season ve Episode'lar da silinecektir.
    }


    private static async Task EnsureChildProfileCleanedAsync(AppDbContext context, Playlist playlist)
    {
        // 1. Kanalları Filtrele
        var currentChannels = playlist.Channels.ToList();
        var existingToKeep = ApplyChildFilter(currentChannels);
        var keptIds = new HashSet<int>(existingToKeep.Select(c => c.Id));
        
        var toDelete = playlist.Channels
            .Where(c => !keptIds.Contains(c.Id))
            .ToList();

        if (toDelete.Any())
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Purging {toDelete.Count} non-compliant channels from child profile.");
            
            // CRITICAL: DO NOT use ExecuteDeleteAsync here as these entities are tracked.
            // RemoveRange handles tracking and sync safely.
            context.Channels.RemoveRange(toDelete);
            await context.SaveChangesAsync();
        }

        // 2. Dizi meta verilerini temizle
        await PurgeNonCompliantSeriesAsync(context, playlist.Id, true);
    }

    private static string NormalizeStreamIdentity(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return string.Empty;
        }

        var trimmed = streamUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            var path = uri.AbsolutePath.Trim().ToLowerInvariant();
            return $"{host}{path}";
        }

        var q = trimmed.IndexOf('?');
        var pathOnly = q >= 0 ? trimmed[..q] : trimmed;
        return pathOnly.Trim().ToLowerInvariant();
    }

    private async Task<RemotePlaylistMetadata?> TryFetchRemoteMetadataAsync(string url, Playlist playlist, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            if (!string.IsNullOrWhiteSpace(playlist.SourceEtag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", playlist.SourceEtag);
            }

            if (playlist.SourceLastModified.HasValue)
            {
                request.Headers.IfModifiedSince = playlist.SourceLastModified.Value;
            }

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new RemotePlaylistMetadata
                {
                    IsUnchanged = true,
                    Etag = playlist.SourceEtag,
                    LastModified = playlist.SourceLastModified,
                    ContentLength = playlist.SourceContentLength
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var etag = response.Headers.ETag?.Tag;
            var lastModified = response.Content.Headers.LastModified?.UtcDateTime;
            var contentLength = response.Content.Headers.ContentLength;

            var unchanged = false;
            if (!string.IsNullOrWhiteSpace(etag) &&
                !string.IsNullOrWhiteSpace(playlist.SourceEtag) &&
                string.Equals(etag, playlist.SourceEtag, StringComparison.Ordinal))
            {
                unchanged = true;
            }
            else if (lastModified.HasValue && playlist.SourceLastModified.HasValue &&
                     lastModified.Value == playlist.SourceLastModified.Value &&
                     contentLength.HasValue && playlist.SourceContentLength.HasValue &&
                     contentLength.Value == playlist.SourceContentLength.Value)
            {
                unchanged = true;
            }

            return new RemotePlaylistMetadata
            {
                IsUnchanged = unchanged,
                Etag = etag,
                LastModified = lastModified,
                ContentLength = contentLength
            };
        }
        catch
        {
            return null;
        }
    }

    private static void UpdatePlaylistSourceMetadata(Playlist playlist, RemotePlaylistMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Etag))
        {
            playlist.SourceEtag = metadata.Etag;
        }

        if (metadata.LastModified.HasValue)
        {
            playlist.SourceLastModified = metadata.LastModified.Value;
        }

        if (metadata.ContentLength.HasValue)
        {
            playlist.SourceContentLength = metadata.ContentLength.Value;
        }
    }

    private sealed class RemotePlaylistMetadata
    {
        public bool IsUnchanged { get; set; }
        public string? Etag { get; set; }
        public DateTime? LastModified { get; set; }
        public long? ContentLength { get; set; }
    }

    public async Task RefreshEpgAsync(int playlistId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var playlist = await context.Playlists
            .Include(p => p.Channels)
            .FirstOrDefaultAsync(p => p.Id == playlistId);
        
        if (playlist == null) return;

        var channels = playlist.Channels.ToList();
        
        var channelNames = channels.Select(c => c.Name ?? "").ToList();
        var countryCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(playlist.DetectedCountry))
        {
            countryCandidates.Add(playlist.DetectedCountry);
        }

        foreach (var country in _languageDetection.DetectCountries(channels)
            .Where(c => c.Percentage > 20 || c.ChannelCount > 20)
            .Select(c => c.CountryCode))
        {
            if (!countryCandidates.Contains(country, StringComparer.OrdinalIgnoreCase))
            {
                countryCandidates.Add(country);
            }
        }

        if (countryCandidates.Count == 0)
        {
            countryCandidates.Add("TR");
        }

        playlist.DetectedCountry = countryCandidates[0];

        var appLanguage = (_settingsService?.Settings?.Language ?? "tr").ToUpperInvariant();
        var majorCountries = countryCandidates.Take(2).ToList();

        // EPG kaynaklarını çöz (çoklu ülke + tekilleştirme)
        var epgSources = _epgSourceResolver.ResolveEpgSources(
            majorCountries,
            m3uEpgUrl: playlist.EpgUrl,
            preferredLanguageCode: appLanguage);

        for (var i = 0; i < epgSources.Count; i++)
        {
            epgSources[i].ClearBeforeLoad = (i == 0);
        }

        string? lastError = null;
        foreach (var source in epgSources)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg trying: {source.Type} - {source.Url}");

                var beforeCount = await _epgService.GetTotalProgramCountAsync();
                var loaded = await _epgService.LoadEpgAsync(source.Url, source.IsPrimary, channels, clearBeforeSave: source.ClearBeforeLoad);
                var afterCount = await _epgService.GetTotalProgramCountAsync();
                
                if (loaded == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg no program loaded: {source.Type}");
                    continue;
                }

                playlist.EpgUrl = source.Url;
                playlist.EpgLastUpdated = DateTime.UtcNow;
                playlist.EpgLastError = null;
                await context.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg success: {source.Type} (+{afterCount - beforeCount})");
                return;
            }
            catch (Exception ex)
            {
                lastError = $"{source.Type}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PlaylistService] RefreshEpg failed: {source.Type} - {ex.Message}");
            }
        }

        playlist.EpgLastError = string.IsNullOrWhiteSpace(lastError)
            ? "EPG kaynaklarindan veri alinamadi."
            : lastError;
        await context.SaveChangesAsync();
    }

    private static string? NormalizeEpgUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var normalized = url.Trim().Trim('"', '\'');
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString();
        }

        return null;
    }
    private async Task FastSqliteBulkInsertAsync(AppDbContext context, IReadOnlyCollection<Channel> channels)
    {
        if (channels.Count == 0) return;
        
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;
        
        if (wasClosed) await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = 
                @"INSERT INTO Channels (
                    Name, StreamUrl, LogoUrl, GroupTitle, TvgId, TvgName, Type, PlaylistId, 
                    IsFavorite, IsInMyList, IsCompleted, WatchedPosition, Duration, Country,
                    Rating, Plot, ReleaseYear, ContentRating, BackdropUrl, Cast, Director, Language, TmdbId
                  ) 
                  VALUES (
                    $name, $streamUrl, $logoUrl, $groupTitle, $tvgId, $tvgName, $type, $playlistId, 
                    $isFavorite, $isInMyList, $isCompleted, $watchedPosition, $duration, $country,
                    $rating, $plot, $releaseYear, $contentRating, $backdropUrl, $cast, $director, $language, $tmdbId
                  );";

            var pName = command.CreateParameter(); pName.ParameterName = "$name"; command.Parameters.Add(pName);
            var pStream = command.CreateParameter(); pStream.ParameterName = "$streamUrl"; command.Parameters.Add(pStream);
            var pLogo = command.CreateParameter(); pLogo.ParameterName = "$logoUrl"; command.Parameters.Add(pLogo);
            var pGroup = command.CreateParameter(); pGroup.ParameterName = "$groupTitle"; command.Parameters.Add(pGroup);
            var pTvgId = command.CreateParameter(); pTvgId.ParameterName = "$tvgId"; command.Parameters.Add(pTvgId);
            var pTvgName = command.CreateParameter(); pTvgName.ParameterName = "$tvgName"; command.Parameters.Add(pTvgName);
            var pType = command.CreateParameter(); pType.ParameterName = "$type"; command.Parameters.Add(pType);
            var pPlaylistId = command.CreateParameter(); pPlaylistId.ParameterName = "$playlistId"; command.Parameters.Add(pPlaylistId);
            var pIsFavorite = command.CreateParameter(); pIsFavorite.ParameterName = "$isFavorite"; command.Parameters.Add(pIsFavorite);
            var pIsInMyList = command.CreateParameter(); pIsInMyList.ParameterName = "$isInMyList"; command.Parameters.Add(pIsInMyList);
            var pIsCompleted = command.CreateParameter(); pIsCompleted.ParameterName = "$isCompleted"; command.Parameters.Add(pIsCompleted);
            var pWatchedPosition = command.CreateParameter(); pWatchedPosition.ParameterName = "$watchedPosition"; command.Parameters.Add(pWatchedPosition);
            var pDuration = command.CreateParameter(); pDuration.ParameterName = "$duration"; command.Parameters.Add(pDuration);
            var pCountry = command.CreateParameter(); pCountry.ParameterName = "$country"; command.Parameters.Add(pCountry);
            var pRating = command.CreateParameter(); pRating.ParameterName = "$rating"; command.Parameters.Add(pRating);
            var pPlot = command.CreateParameter(); pPlot.ParameterName = "$plot"; command.Parameters.Add(pPlot);
            var pReleaseYear = command.CreateParameter(); pReleaseYear.ParameterName = "$releaseYear"; command.Parameters.Add(pReleaseYear);
            var pContentRating = command.CreateParameter(); pContentRating.ParameterName = "$contentRating"; command.Parameters.Add(pContentRating);
            var pBackdrop = command.CreateParameter(); pBackdrop.ParameterName = "$backdropUrl"; command.Parameters.Add(pBackdrop);
            var pCast = command.CreateParameter(); pCast.ParameterName = "$cast"; command.Parameters.Add(pCast);
            var pDirector = command.CreateParameter(); pDirector.ParameterName = "$director"; command.Parameters.Add(pDirector);
            var pLanguage = command.CreateParameter(); pLanguage.ParameterName = "$language"; command.Parameters.Add(pLanguage);
            var pTmdbId = command.CreateParameter(); pTmdbId.ParameterName = "$tmdbId"; command.Parameters.Add(pTmdbId);

            foreach (var channel in channels)
            {
                pName.Value = channel.Name ?? "Bilinmeyen Kanal";
                pStream.Value = channel.StreamUrl ?? "";
                pLogo.Value = channel.LogoUrl ?? (object)DBNull.Value;
                pGroup.Value = channel.GroupTitle ?? (object)DBNull.Value;
                pTvgId.Value = channel.TvgId ?? (object)DBNull.Value;
                pTvgName.Value = channel.TvgName ?? (object)DBNull.Value;
                pType.Value = (int)channel.Type;
                pPlaylistId.Value = channel.PlaylistId;
                pIsFavorite.Value = channel.IsFavorite ? 1 : 0;
                pIsInMyList.Value = channel.IsInMyList ? 1 : 0;
                pIsCompleted.Value = channel.IsCompleted ? 1 : 0;
                pWatchedPosition.Value = channel.WatchedPosition?.ToString() ?? (object)DBNull.Value;
                pDuration.Value = channel.Duration?.ToString() ?? (object)DBNull.Value;
                pCountry.Value = channel.Country ?? (object)DBNull.Value;
                pRating.Value = channel.Rating ?? (object)DBNull.Value;
                pPlot.Value = channel.Plot ?? (object)DBNull.Value;
                pReleaseYear.Value = channel.ReleaseYear ?? (object)DBNull.Value;
                pContentRating.Value = channel.ContentRating ?? (object)DBNull.Value;
                pBackdrop.Value = channel.BackdropUrl ?? (object)DBNull.Value;
                pCast.Value = channel.Cast ?? (object)DBNull.Value;
                pDirector.Value = channel.Director ?? (object)DBNull.Value;
                pLanguage.Value = channel.Language ?? (object)DBNull.Value;
                pTmdbId.Value = channel.TmdbId ?? (object)DBNull.Value;

                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}




