using System.IO.Compression;
using System.Globalization;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// EPG (Electronic Program Guide) servisi
/// </summary>
public class EpgService : IEpgService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly LanguageDetectionService _languageDetectionService;
    private readonly ILogger<EpgService>? _logger;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    
    public bool IsLoaded { get; private set; }
    public DateTime? LastUpdated { get; private set; }
    public string? LastError { get; private set; }

    public EpgService(IDbContextFactory<AppDbContext> contextFactory, HttpClient httpClient, ISettingsService settingsService, ILocalizationService localizationService, LanguageDetectionService languageDetectionService, ILogger<EpgService>? logger = null)
    {
        _contextFactory = contextFactory;
        _httpClient = httpClient;
        _settingsService = settingsService;
        _localizationService = localizationService;
        _languageDetectionService = languageDetectionService;
        _logger = logger;
    }

    public void ClearLastError()
    {
        LastError = null;
    }

    public async Task<int> LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null, int daysAhead = 1, IProgress<EpgProgressInfo>? progress = null, bool clearBeforeSave = false, IDictionary<string, string>? headers = null)
    {
        if (!await _loadSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            _logger?.LogWarning("Another EPG load is in progress, skipping new request.");
            return -1; // -1 indicates it was skipped due to concurrency
        }

        // Check if EPG is enabled globally
        if (!_settingsService.Settings.EpgEnabled)
        {
            _logger?.LogInformation("EPG is disabled in settings, skipping load.");
            return 0;
        }

        int totalLoaded = 0;
        try
        {
            LastError = null; // Clear previous error
            if (string.IsNullOrEmpty(epgUrl)) return 0;

            progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Downloading, Message = _localizationService.GetString("Epg.Progress.Downloading"), ProgressPercent = 5 });

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            
            HttpResponseMessage response;
            if (headers != null && headers.Count > 0)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, epgUrl);
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            }
            else
            {
                response = await NetworkRetry.ExecuteAsync(
                    () => _httpClient.GetAsync(epgUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token),
                    cancellationToken: cts.Token).ConfigureAwait(false);
            }

            using var _ = response;
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);

            // GZip decompression support (.gz URLs)
            await using Stream dataStream = epgUrl.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                response.Content.Headers.ContentEncoding.Contains("gzip")
                ? new GZipStream(stream, CompressionMode.Decompress)
                : stream;

            if (dataStream is GZipStream)
            {
                progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Decompressing, Message = _localizationService.GetString("Epg.Progress.Decompressing"), ProgressPercent = 15 });
            }

            progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Parsing, Message = _localizationService.GetString("Epg.Progress.Parsing"), ProgressPercent = 20 });

            var settings = new System.Xml.XmlReaderSettings 
            { 
                Async = true, 
                DtdProcessing = System.Xml.DtdProcessing.Ignore 
            };
            using var reader = System.Xml.XmlReader.Create(dataStream, settings);

            // Build mapping dictionary for EPG (Name -> list of channel Ids)
            var channelMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var xmlChannelIdToDbTvgIds = new Dictionary<string, List<string>>();
            var tvgIdToInternalIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var allowedPrimaryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (channelsForMapping != null)
            {
                progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Matching, Message = _localizationService.GetString("Epg.Progress.Matching"), ProgressPercent = 25 });
                foreach (var channel in channelsForMapping)
                {
                    var idStr = channel.Id.ToString();
                    if (!string.IsNullOrWhiteSpace(channel.TvgId))
                    {
                        allowedPrimaryIds.Add(channel.TvgId!);
                        if (!tvgIdToInternalIds.TryGetValue(channel.TvgId!, out var list))
                        {
                            list = new List<string>();
                            tvgIdToInternalIds[channel.TvgId!] = list;
                        }
                        if (!list.Contains(idStr)) list.Add(idStr);
                    }

                    allowedPrimaryIds.Add(idStr);

                    foreach (var variant in GetNameVariants(channel.Name))
                    {
                        if (string.IsNullOrEmpty(variant)) continue;
                        if (!channelMap.TryGetValue(variant, out var list))
                        {
                            list = new List<string>();
                            channelMap[variant] = list;
                        }
                        if (!list.Contains(idStr)) list.Add(idStr);
                    }
                    if (!string.IsNullOrEmpty(channel.TvgName))
                    {
                        foreach (var variant in GetNameVariants(channel.TvgName))
                        {
                            if (string.IsNullOrEmpty(variant)) continue;
                            if (!channelMap.TryGetValue(variant, out var list))
                            {
                                list = new List<string>();
                                channelMap[variant] = list;
                            }
                            if (!list.Contains(idStr)) list.Add(idStr);
                        }
                    }
                }
            }

            var programs = new List<EpgProgram>();
            var batchSize = 2500; // Slightly larger batch for better performance
            var windowStartUtc = DateTime.UtcNow.Date;
            var windowEndUtc = windowStartUtc.AddDays(Math.Max(1, daysAhead) + 1);

            using var context = await _contextFactory.CreateDbContextAsync();
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                // Clear immediately if requested, ensuring we don't wait for the first matching program.
                if (clearBeforeSave)
                {
                    await context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms").ConfigureAwait(false);
                    _logger?.LogDebug("[EpgService] EPG data cleared at the start of load (Atomic).");
                }

                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element)
                    {
                        if (reader.Name == "channel") 
                        {
                            if (channelMap.Count > 0)
                            {
                                    var xmlId = reader.GetAttribute("id");
                                    var effectiveXmlId = xmlId;
                                    
                                    // Fallback for malformed XML where channel ID is missing or empty
                                    var isMalformedId = string.IsNullOrWhiteSpace(xmlId);

                                    using var subReader = reader.ReadSubtree();
                                    while (await subReader.ReadAsync().ConfigureAwait(false))
                                    {
                                        if (subReader.NodeType == System.Xml.XmlNodeType.Element && subReader.Name == "display-name")
                                        {
                                            var displayName = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                            
                                            // If the ID was missing or empty, use the first display name as the effective ID
                                            if (isMalformedId && string.IsNullOrWhiteSpace(effectiveXmlId))
                                            {
                                                effectiveXmlId = displayName;
                                            }

                                            foreach (var variant in GetNameVariants(displayName))
                                            {
                                                var dbChannelIds = ResolveMappedChannelIds(variant, channelMap);
                                                if (dbChannelIds != null && dbChannelIds.Any())
                                                {
                                                    if (!xmlChannelIdToDbTvgIds.TryGetValue(effectiveXmlId!, out var targetList))
                                                    {
                                                        targetList = new List<string>();
                                                        xmlChannelIdToDbTvgIds[effectiveXmlId!] = targetList;
                                                    }
                                                    foreach (var id in dbChannelIds)
                                                        if (!targetList.Contains(id)) targetList.Add(id);
                                                    
                                                    if (isPrimary) allowedPrimaryIds.Add(effectiveXmlId!);
                                                    break; 
                                                }
                                            }
                                        }
                                    }

                                    // If we still have a valid ID (from attribute or fallback) 
                                    // and it matches a TvgId directly in our database
                                    if (!string.IsNullOrWhiteSpace(effectiveXmlId) && tvgIdToInternalIds.TryGetValue(effectiveXmlId, out var byTvgIds))
                                    {
                                        if (!xmlChannelIdToDbTvgIds.TryGetValue(effectiveXmlId, out var targetList))
                                        {
                                            targetList = new List<string>();
                                            xmlChannelIdToDbTvgIds[effectiveXmlId] = targetList;
                                        }
                                        foreach (var id in byTvgIds)
                                            if (!targetList.Contains(id)) targetList.Add(id);

                                        if (isPrimary) allowedPrimaryIds.Add(effectiveXmlId);
                                    }
                            }
                        }
                        else if (reader.Name == "programme")
                        {
                            var start = reader.GetAttribute("start");
                            var stop = reader.GetAttribute("stop");
                             var channel = reader.GetAttribute("channel");

                            if (string.IsNullOrEmpty(channel)) continue;

                            var targetIds = new List<string>();
                            if (xmlChannelIdToDbTvgIds.TryGetValue(channel, out var mappedIds))
                            {
                                targetIds.AddRange(mappedIds);
                            }
                            else if (isPrimary)
                            {
                                if (allowedPrimaryIds.Count > 0 && !allowedPrimaryIds.Contains(channel))
                                {
                                    continue;
                                }
                                targetIds.Add(channel);
                            }
                            else
                            {
                                continue;
                            }

                            var startTime = ParseXmlTvDate(start);
                            var endTime = ParseXmlTvDate(stop);

                             if (endTime <= windowStartUtc || startTime >= windowEndUtc)
                                continue;

                            var programTemplate = new EpgProgram
                            {
                                StartTime = startTime,
                                EndTime = endTime
                            };

                            using var subReader = reader.ReadSubtree();
                            while (await subReader.ReadAsync().ConfigureAwait(false))
                            {
                                if (subReader.NodeType == System.Xml.XmlNodeType.Element)
                                {
                                    switch (subReader.Name)
                                    {
                                        case "title":
                                            programTemplate.Title = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                            break;
                                        case "desc":
                                            if (string.IsNullOrWhiteSpace(programTemplate.Description))
                                            {
                                                var desc = await subReader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                                                if (!string.IsNullOrWhiteSpace(desc))
                                                {
                                                    programTemplate.Description = desc;
                                                }
                                            }
                                            break;
                                    }
                                }
                            }

                            foreach (var tId in targetIds)
                            {
                                programs.Add(new EpgProgram
                                {
                                    ChannelId = tId,
                                    StartTime = programTemplate.StartTime,
                                    EndTime = programTemplate.EndTime,
                                    Title = programTemplate.Title,
                                    Description = programTemplate.Description
                                });
                                totalLoaded++;
                            }

                            if (programs.Count >= batchSize)
                            {
                                progress?.Report(new EpgProgressInfo 
                                { 
                                    Status = EpgLoadStatus.Saving, 
                                    Message = string.Format(_localizationService.GetString("Epg.Progress.SavingFormat"), totalLoaded), 
                                    ProgressPercent = Math.Min(98, 30 + (totalLoaded / 5000.0 * 5.0)),
                                    LoadedCount = totalLoaded
                                });

                                await AddProgramsDeduplicatedAsync(context, programs).ConfigureAwait(false);
                                await context.SaveChangesAsync().ConfigureAwait(false);
                                programs.Clear();
                            }
                        }
                    }
                }

                if (programs.Any())
                {
                    await AddProgramsDeduplicatedAsync(context, programs).ConfigureAwait(false);
                    await context.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                context.ChangeTracker.AutoDetectChangesEnabled = true;
            }


            IsLoaded = true;
            LastUpdated = DateTime.UtcNow;
            progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Completed, Message = _localizationService.GetString("Common.Completed"), ProgressPercent = 100, LoadedCount = totalLoaded });
            return totalLoaded;
        }
        catch (Exception ex)
        {
            LastError = UserFriendlyErrorMessage.FromException(ex);
            _logger?.LogError(ex, "EPG load failed for URL {EpgUrl}", epgUrl);
            progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Failed, Message = LastError, ProgressPercent = 100 });
            if (isPrimary) throw;
            return 0;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    private static async Task AddProgramsDeduplicatedAsync(AppDbContext context, List<EpgProgram> programs)
    {
        if (programs.Count == 0)
        {
            return;
        }

        var minStart = programs.Min(p => p.StartTime);
        var maxEnd = programs.Max(p => p.EndTime);
        var channelIds = programs
            .Select(p => p.ChannelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        var existingPrograms = channelIds.Count == 0
            ? new List<EpgProgram>()
            : await context.EpgPrograms
                .AsNoTracking()
                .Where(p => channelIds.Contains(p.ChannelId)
                         && p.EndTime > minStart
                         && p.StartTime < maxEnd)
                .Select(p => new EpgProgram
                {
                    ChannelId = p.ChannelId,
                    Title = p.Title,
                    StartTime = p.StartTime,
                    EndTime = p.EndTime
                })
                .ToListAsync()
                .ConfigureAwait(false);

        var exactKeys = new HashSet<string>(existingPrograms.Select(BuildEpgDedupKey), StringComparer.Ordinal);
        var accepted = new List<EpgProgram>(programs.Count);

        foreach (var program in programs)
        {
            if (string.IsNullOrWhiteSpace(program.ChannelId) || program.EndTime <= program.StartTime)
            {
                continue;
            }

            var key = BuildEpgDedupKey(program);
            if (!exactKeys.Add(key))
            {
                continue;
            }

            if (HasEquivalentOverlappingProgram(program, existingPrograms) ||
                HasEquivalentOverlappingProgram(program, accepted))
            {
                continue;
            }

            accepted.Add(program);
        }

        if (accepted.Count > 0)
        {
            await context.EpgPrograms.AddRangeAsync(accepted).ConfigureAwait(false);
        }
    }

    private static bool HasEquivalentOverlappingProgram(EpgProgram program, IEnumerable<EpgProgram> candidates)
    {
        var title = NormalizeEpgTitle(program.Title);

        foreach (var candidate in candidates)
        {
            if (!string.Equals(candidate.ChannelId, program.ChannelId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(NormalizeEpgTitle(candidate.Title), title, StringComparison.Ordinal))
            {
                continue;
            }

            if (GetOverlapRatio(program.StartTime, program.EndTime, candidate.StartTime, candidate.EndTime) >= 0.8)
            {
                return true;
            }
        }

        return false;
    }

    private static List<EpgProgram> DeduplicateProgramList(IEnumerable<EpgProgram> programs)
    {
        var exactKeys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<EpgProgram>();

        foreach (var program in programs.OrderBy(p => p.StartTime).ThenBy(p => p.EndTime))
        {
            var key = BuildEpgDedupKey(program);
            if (!exactKeys.Add(key))
            {
                continue;
            }

            if (HasEquivalentOverlappingProgram(program, result))
            {
                continue;
            }

            result.Add(program);
        }

        return result;
    }

    private static List<EpgProgram> NormalizeProgramTimeline(IEnumerable<EpgProgram> programs)
    {
        var result = new List<EpgProgram>();

        foreach (var program in DeduplicateProgramList(programs))
        {
            if (program.EndTime <= program.StartTime)
            {
                continue;
            }

            while (result.Count > 0)
            {
                var previous = result[^1];
                if (!string.Equals(previous.ChannelId, program.ChannelId, StringComparison.OrdinalIgnoreCase) ||
                    previous.EndTime <= program.StartTime)
                {
                    break;
                }

                if (string.Equals(NormalizeEpgTitle(previous.Title), NormalizeEpgTitle(program.Title), StringComparison.Ordinal))
                {
                    if (program.EndTime > previous.EndTime)
                    {
                        previous.EndTime = program.EndTime;
                    }
                    goto NextProgram;
                }

                if (previous.StartTime < program.StartTime)
                {
                    previous.EndTime = program.StartTime;
                    if (previous.EndTime > previous.StartTime)
                    {
                        break;
                    }
                }

                result.RemoveAt(result.Count - 1);
            }

            result.Add(program);

        NextProgram:
            continue;
        }

        return result;
    }

    private static EpgProgram? PickCurrentProgram(IEnumerable<EpgProgram> programs, DateTime now)
        => NormalizeProgramTimeline(programs)
            .Where(p => p.StartTime <= now && p.EndTime > now)
            .OrderByDescending(p => p.StartTime)
            .ThenBy(p => p.EndTime)
            .FirstOrDefault();

    private static double GetOverlapRatio(DateTime startA, DateTime endA, DateTime startB, DateTime endB)
    {
        var overlapStart = startA > startB ? startA : startB;
        var overlapEnd = endA < endB ? endA : endB;
        if (overlapEnd <= overlapStart)
        {
            return 0;
        }

        var shortestDuration = Math.Min((endA - startA).TotalSeconds, (endB - startB).TotalSeconds);
        return shortestDuration <= 0
            ? 0
            : (overlapEnd - overlapStart).TotalSeconds / shortestDuration;
    }

    private static string BuildEpgDedupKey(EpgProgram program)
        => string.Join('\u001F',
            NormalizeEpgChannelId(program.ChannelId),
            program.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            program.EndTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            NormalizeEpgTitle(program.Title));

    private static string NormalizeEpgChannelId(string? channelId)
        => (channelId ?? string.Empty).Trim().ToUpperInvariant();

    private static string NormalizeEpgTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        return string.Join(' ', title.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private IEnumerable<string> GetNameVariants(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract country code from name using LanguageDetectionService
        var countryCode = _languageDetectionService.DetectCountryFromName(name);

        // 1. Full normalized name WITH country prefix
        var normalizedFull = NormalizeName(name);
        if (normalizedFull.Length >= 3)
        {
            var withCountry = $"{countryCode}:{normalizedFull}";
            if (seen.Add(withCountry))
                yield return withCountry;
        }

        // 2. Strip parenthesized suffix: "Star TV (TR)" → "Star TV"
        var noParens = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)\s*$", "").Trim();
        if (noParens != name)
        {
            var v = NormalizeName(noParens);
            if (v.Length >= 3)
            {
                var withCountry = $"{countryCode}:{v}";
                if (seen.Add(withCountry))
                    yield return withCountry;
            }
        }

        // 3. Strip pipe/slash separators: "TR | Kanal D" → "Kanal D"
        var separators = new[] { '|', '/', '\\' };
        foreach (var sep in separators)
        {
            var idx = name.IndexOf(sep);
            if (idx > 0 && idx < name.Length - 1)
            {
                var after = name[(idx + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(after))
                {
                    var v = NormalizeName(after);
                    if (v.Length >= 3)
                    {
                        var withCountry = $"{countryCode}:{v}";
                        if (seen.Add(withCountry))
                            yield return withCountry;
                    }
                }
            }
        }

        // 4. Strip trailing dot-suffix: "KanalD.tr" → "KanalD"
        var dotIdx = name.LastIndexOf('.');
        if (dotIdx > 0)
        {
            var suffix = name[(dotIdx + 1)..];
            if (suffix.Length <= 3 && suffix.All(char.IsLetter))
            {
                var v = NormalizeName(name[..dotIdx]);
                if (v.Length >= 3)
                {
                    var withCountry = $"{countryCode}:{v}";
                    if (seen.Add(withCountry))
                        yield return withCountry;
                }
            }
        }

        // 5. Strip trailing country names
        var trailingCountries = new[] { "turkey", "turkiye", "türkiye", "tr", "de", "uk", "us", "fr", "it", "es", "nl", "ru" };
        var lowerName = name.Trim().ToLowerInvariant();
        foreach (var country in trailingCountries)
        {
            if (lowerName.EndsWith(" " + country, StringComparison.Ordinal))
            {
                var trimmed = name.Trim()[..^(country.Length + 1)].Trim();
                var v = NormalizeName(trimmed);
                if (v.Length >= 3)
                {
                    var withCountry = $"{countryCode}:{v}";
                    if (seen.Add(withCountry))
                        yield return withCountry;
                }
            }
        }
    }

    private static bool TryStripLeadingCountryCode(string input, out string stripped)
    {
        stripped = input.Trim();
        if (stripped.Length < 5)
            return false;

        var i = 0;
        while (i < stripped.Length && char.IsLetter(stripped[i]) && i < 3) i++;
        if (i < 2 || i > 3)
            return false;

        // Must look like code token (all upper-ish letters).
        var code = stripped[..i];
        if (!code.All(char.IsLetter))
            return false;

        var j = i;
        while (j < stripped.Length && char.IsWhiteSpace(stripped[j])) j++;
        if (j < stripped.Length && (stripped[j] == '-' || stripped[j] == ':' || stripped[j] == '|' || stripped[j] == '/' || stripped[j] == '\\'))
        {
            j++;
            while (j < stripped.Length && char.IsWhiteSpace(stripped[j])) j++;
        }
        else if (j == i) // no whitespace/separator after code
        {
            return false;
        }

        if (j >= stripped.Length)
            return false;

        stripped = stripped[j..];
        return true;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var s = name.ToLowerInvariant();

        // beIN Sports De-obfuscation (Şifreli isimleri çözme)
        // be*n, b*in, be!n, b.e.i.n, be-in gibi varyasyonları 'bein'e çevir
        if (s.Contains('b') && (s.Contains('i') || s.Contains('n')))
        {
            // Regex: b followed by any non-alphanumeric or digit, then i, n, etc.
            s = System.Text.RegularExpressions.Regex.Replace(s, @"b[e\*!1\.\-\s]*[i\*!1\.\-\s]*n", "bein");
            // Eğer hala 'be n' gibi boşluklu kalmışsa birleştir
            if (s.Contains("be n")) s = s.Replace("be n", "bein");
        }

        s = s.Replace('ı', 'i')
            .Replace('İ', 'i')
            .Replace('ş', 's')
            .Replace('Ş', 's')
            .Replace('ğ', 'g')
            .Replace('Ğ', 'g')
            .Replace('ü', 'u')
            .Replace('Ü', 'u')
            .Replace('ö', 'o')
            .Replace('Ö', 'o')
            .Replace('ç', 'c')
            .Replace('Ç', 'c');

        // IPTV Specific: Remove anything inside brackets or pipes
        // "TR | Kanal D" -> "Kanal D", "[TR] Star TV" -> "Star TV"
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\|[^|]+\|", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\[[^\]]+\]", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\([^)]+\)", " ");
        s = s.Replace("|", " ");

        var chars = new List<char>(s.Length);
        foreach (var ch in s)
        {
            chars.Add(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        var noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hd", "fhd", "uhd", "sd", "hevc", "h265", "h264", "4k", "fullhd", "qhd", "hdr", "10bit", "av1", "raw", "1080i", "720i",
            "feed", "ticari", "50fps", "60fps", "mobie", "mobile", "sdmobile", "fhdmobile", "web", "app", "ios", "android", "iptv", "ts", "m3u8",
            "1080p", "720p", "480p", "2160p", "1080", "720", "576", "live", "vip", "premium",
            "backup", "bkp", "multi", "sub", "ace", "plus", "extra", "max", "sat",
            "turkey", "turkiye", "türkiye", "tr", "tur",
            "de", "ger", "germany", "deutschland",
            "gb", "uk", "en", "england",
            "us", "usa",
            "fr", "fra", "france",
            "it", "ita", "italy", "italia",
            "es", "esp", "spain", "espana",
            "mx", "mex", "mexico",
            "ar", "arg", "argentina",
            "br", "bra", "brazil",
            "pt", "portugal",
            "nl", "nld", "netherlands",
            "ru", "rus", "russia",
            "al", "alb", "albania",
            "ge", "geo", "georgia",
            "gr", "gre", "greece",
            "hu", "hun", "hungary",
            "hk", "se", "swe", "sweden",
            "ch", "che", "switzerland",
            "yayin", "kesintisiz",
        };

        var tokens = new string(chars.ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !noise.Contains(t));

        return string.Concat(tokens);
    }
    private static List<string>? ResolveMappedChannelIds(string normalizedDisplayName, Dictionary<string, List<string>> channelMap)
    {
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || normalizedDisplayName.Length < 3)
            return null;

        // Fast path: Exact match (O(1))
        if (channelMap.TryGetValue(normalizedDisplayName, out var exactList))
        {
            return exactList;
        }

        // Optimization: Skip fuzzy matching for very short names to avoid false positives.
        if (normalizedDisplayName.Length < 4)
            return null;

        // Dynamic threshold: shorter names need less strict matching
        var maxLen = Math.Max(normalizedDisplayName.Length, 3);
        var threshold = maxLen <= 6 ? 0.70 : maxLen <= 10 ? 0.75 : 0.82;

        List<string>? bestIds = null;
        double bestScore = 0;

        foreach (var kvp in channelMap)
        {
            // Preliminary check: if first characters don't match, similarity is likely low
            if (kvp.Key[0] != normalizedDisplayName[0]) continue;

            var score = Similarity(normalizedDisplayName, kvp.Key);
            if (score > bestScore)
            {
                bestScore = score;
                bestIds = kvp.Value;
            }
            
            // If we found a very high confidence match, stop searching
            if (bestScore > 0.95) break;
        }

        return bestScore >= threshold ? bestIds : null;
    }

    private static double Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        if (a == b) return 1;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            var min = Math.Min(a.Length, b.Length);
            var max = Math.Max(a.Length, b.Length);
            return 0.88 * (double)min / max;
        }

        return BigramDice(a, b);
    }

    private static double BigramDice(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return a == b ? 1 : 0;

        var aBigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < a.Length - 1; i++)
        {
            var bg = a.Substring(i, 2);
            aBigrams[bg] = aBigrams.TryGetValue(bg, out var count) ? count + 1 : 1;
        }

        var intersection = 0;
        var bCount = 0;
        for (var i = 0; i < b.Length - 1; i++)
        {
            var bg = b.Substring(i, 2);
            bCount++;
            if (aBigrams.TryGetValue(bg, out var count) && count > 0)
            {
                intersection++;
                aBigrams[bg] = count - 1;
            }
        }

        var aCount = a.Length - 1;
        return (2.0 * intersection) / (aCount + bCount);
    }
    private void ApplyTimeOffset(EpgProgram? program)
    {
        if (program == null) return;
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        if (offset == TimeSpan.Zero) return;

        program.StartTime = program.StartTime.Add(offset);
        program.EndTime = program.EndTime.Add(offset);
    }

    private void ApplyTimeOffset(IEnumerable<EpgProgram> programs)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        if (offset == TimeSpan.Zero) return;

        foreach (var program in programs)
        {
            program.StartTime = program.StartTime.Add(offset);
            program.EndTime = program.EndTime.Add(offset);
        }
    }

    public async Task<EpgProgram?> GetCurrentProgramAsync(Channel channel)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var now = DateTime.UtcNow.Add(-offset);
        using var context = await _contextFactory.CreateDbContextAsync();

        foreach (var epgId in GetEpgLookupIds(channel))
        {
            var programs = await context.EpgPrograms
                .AsNoTracking()
                .Where(p => p.ChannelId == epgId
                         && p.EndTime > now.AddHours(-6)
                         && p.StartTime <= now.AddHours(6))
                .OrderBy(p => p.StartTime)
                .ThenBy(p => p.EndTime)
                .ToListAsync();

            var program = PickCurrentProgram(programs, now);

            if (program != null)
            {
                ApplyTimeOffset(program);
                return program;
            }
        }

        return null;
    }

    public async Task<Dictionary<int, EpgProgram?>> GetCurrentProgramsAsync(IEnumerable<Channel> channels)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var now = DateTime.UtcNow.Add(-offset);
        using var context = await _contextFactory.CreateDbContextAsync();

        var liveChannels = channels.Where(c => c.Type == ChannelType.Live).ToList();
        var results = new Dictionary<int, EpgProgram?>();

        if (liveChannels.Count == 0) return results;

        var lookupIdsByChannel = liveChannels.ToDictionary(c => c.Id, GetEpgLookupIds);
        var allSearchIds = lookupIdsByChannel.Values.SelectMany(ids => ids).Distinct().ToList();

        // Optimized bulk query: fetch all current programs for these IDs in one go
        var matchingPrograms = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => allSearchIds.Contains(p.ChannelId)
                     && p.EndTime > now.AddHours(-6)
                     && p.StartTime <= now.AddHours(6))
            .ToListAsync();

        matchingPrograms = matchingPrograms
            .GroupBy(p => p.ChannelId)
            .SelectMany(g => NormalizeProgramTimeline(g))
            .ToList();

        foreach (var channel in liveChannels)
        {
            EpgProgram? program = null;

            if (lookupIdsByChannel.TryGetValue(channel.Id, out var lookupIds))
            {
                foreach (var lookupId in lookupIds)
                {
                    program = matchingPrograms
                        .Where(p => p.ChannelId == lookupId && p.StartTime <= now && p.EndTime > now)
                        .OrderByDescending(p => p.StartTime)
                        .ThenBy(p => p.EndTime)
                        .FirstOrDefault();
                    if (program != null)
                    {
                        break;
                    }
                }
            }

            if (program != null)
            {
                ApplyTimeOffset(program);
            }
            results[channel.Id] = program;
        }

        return results;
    }

    private static List<string> GetEpgLookupIds(Channel channel)
        => new[]
            {
                channel.TvgId,
                channel.TvgName,
                channel.Name,
                channel.Id > 0 ? channel.Id.ToString(CultureInfo.InvariantCulture) : null
            }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task ClearEpgAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM EpgPrograms");
        IsLoaded = false;
    }

    public async Task<Dictionary<string, List<EpgProgram>>> GetProgramsBulkAsync(
        IEnumerable<string> channelIds, DateTime fromLocal, DateTime toLocal)
    {
        var ids = channelIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var fromUtc = fromLocal.ToUniversalTime().Add(-offset);
        var toUtc   = toLocal.ToUniversalTime().Add(-offset);

        using var context = await _contextFactory.CreateDbContextAsync();

        // Tek SQL sorgusu: tüm kanallar için zaman penceresi içindeki programlar
        var programs = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => ids.Contains(p.ChannelId)
                     && p.EndTime   >= fromUtc
                     && p.StartTime <= toUtc)
            .OrderBy(p => p.ChannelId)
            .ThenBy(p => p.StartTime)
            .ToListAsync();

        ApplyTimeOffset(programs);

        return programs
            .GroupBy(p => p.ChannelId)
            .ToDictionary(g => g.Key, g => NormalizeProgramTimeline(g));
    }

    public async Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        // Ensure we compare in UTC if stored in UTC
        var fromUtc = from.ToUniversalTime().Add(-offset);
        var toUtc = to.ToUniversalTime().Add(-offset);

        using var context = await _contextFactory.CreateDbContextAsync();
        var programs = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime >= fromUtc && p.StartTime <= toUtc)
            .OrderBy(p => p.StartTime)
            .ToListAsync();

        ApplyTimeOffset(programs);
        return NormalizeProgramTimeline(programs);
    }

    public async Task<List<EpgProgram>> GetUpcomingProgramsAsync(string channelId, int count = 5)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var now = DateTime.UtcNow.Add(-offset);
        using var context = await _contextFactory.CreateDbContextAsync();
        var programs = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime > now)
            .OrderBy(p => p.StartTime)
            .Take(count)
            .ToListAsync();

        ApplyTimeOffset(programs);
        return NormalizeProgramTimeline(programs);
    }

    public async Task<List<EpgProgram>> GetTodayProgramsAsync(string channelId)
    {
        var offset = TimeSpan.FromHours(_settingsService.Settings.EpgTimeOffsetHours);
        var todayStart = DateTime.UtcNow.Date.Add(-offset);
        var todayEnd = todayStart.AddDays(1);
        using var context = await _contextFactory.CreateDbContextAsync();
        var programs = await context.EpgPrograms
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId && p.StartTime >= todayStart && p.StartTime < todayEnd)
            .OrderBy(p => p.StartTime)
            .ToListAsync();

        ApplyTimeOffset(programs);
        return NormalizeProgramTimeline(programs);
    }

    public async Task<int> GetTotalProgramCountAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpgPrograms.CountAsync();
    }

    public async Task<int> GetDistinctChannelCountAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpgPrograms
            .Select(p => p.ChannelId)
            .Distinct()
            .CountAsync();
    }

    public async Task<DateTime?> GetMaxProgramEndTimeAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var maxTime = await context.EpgPrograms
            .Select(p => (DateTime?)p.EndTime)
            .MaxAsync();
        return maxTime;
    }

    public async Task VacuumAsync()
    {
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            // SQLite specific command to reclaim unused space
            await context.Database.ExecuteSqlRawAsync("VACUUM");
            _logger?.LogInformation("[EpgService] Database vacuum completed successfully.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[EpgService] Failed to vacuum database.");
        }
    }

    /// <summary>
    /// XMLTV tarih formatını parse eder (yyyyMMddHHmmss +HHMM)
    /// </summary>
    private static DateTime ParseXmlTvDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return DateTime.MinValue;

        // XMLTV formats: 
        // 20240101120000 +0300
        // 20240101120000
        
        try
        {
            dateStr = dateStr.Trim();
            var normalizedOffsetDate = NormalizeXmlTvOffset(dateStr);

            // With offset
            if (DateTimeOffset.TryParseExact(
                normalizedOffsetDate,
                new[] { "yyyyMMddHHmmss zzz", "yyyyMMddHHmm zzz" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var dto))
            {
                return dto.UtcDateTime;
            }

            // Without offset
            if (DateTime.TryParseExact(
                datePart(normalizedOffsetDate),
                new[] { "yyyyMMddHHmmss", "yyyyMMddHHmm" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var dt))
            {
                return dt.ToUniversalTime();
            }
        }
        catch { }

        return DateTime.MinValue;
    }

    private static string NormalizeXmlTvOffset(string value)
    {
        // Supports:
        // 20240101120000 +0300
        // 20240101120000+0300
        // 20240101120000 +03:00
        var trimmed = value.Trim();
        var compact = trimmed.Replace(" ", "");

        var signIndex = compact.IndexOf('+');
        if (signIndex < 0)
        {
            signIndex = compact.IndexOf('-', 1); // ignore leading minus on malformed date
        }

        if (signIndex <= 0)
        {
            return trimmed;
        }

        var d = compact[..signIndex];
        var offset = compact[signIndex..];

        if (offset.Length == 5 && (offset[0] == '+' || offset[0] == '-'))
        {
            offset = $"{offset[..3]}:{offset[3..]}";
        }

        return $"{d} {offset}";
    }

    private static string datePart(string fullDate)
    {
        return fullDate.Split(' ')[0];
    }
}
