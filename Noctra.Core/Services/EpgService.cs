using System.IO.Compression;
using System.Globalization;
using System.Net.Http;
using System.Text;
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

        int totalLoaded = 0;
        try
        {
            // Check if EPG is enabled globally after the semaphore is acquired so
            // all exits are covered by the release in finally.
            if (!_settingsService.Settings.EpgEnabled)
            {
                _logger?.LogInformation("EPG is disabled in settings, skipping load.");
                return 0;
            }

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
            var internalIdToGroupCountry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allowedPrimaryIdGroupCountries = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var allowedPrimaryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceCountryCode = DetectExplicitCountryCode(epgUrl);

            if (channelsForMapping != null)
            {
                progress?.Report(new EpgProgressInfo { Status = EpgLoadStatus.Matching, Message = _localizationService.GetString("Epg.Progress.Matching"), ProgressPercent = 25 });
                foreach (var channel in channelsForMapping)
                {
                    var idStr = channel.Id.ToString();
                    var groupCountryCode = DetectExplicitCountryCode(channel.GroupTitle);
                    var channelCountryCode = NormalizeExplicitCountryCode(channel.Country)
                        ?? DetectExplicitCountryCode(channel.Name)
                        ?? DetectExplicitCountryCode(channel.TvgName)
                        ?? groupCountryCode;
                    if (!string.IsNullOrWhiteSpace(channelCountryCode))
                    {
                        internalIdToGroupCountry[idStr] = channelCountryCode;
                    }

                    if (!string.IsNullOrWhiteSpace(channel.TvgId))
                    {
                        allowedPrimaryIds.Add(channel.TvgId!);
                        AddAllowedPrimaryGroupCountry(allowedPrimaryIdGroupCountries, channel.TvgId!, channelCountryCode);
                        if (!tvgIdToInternalIds.TryGetValue(channel.TvgId!, out var list))
                        {
                            list = new List<string>();
                            tvgIdToInternalIds[channel.TvgId!] = list;
                        }
                        if (!list.Contains(idStr)) list.Add(idStr);
                    }

                    allowedPrimaryIds.Add(idStr);
                    AddAllowedPrimaryGroupCountry(allowedPrimaryIdGroupCountries, idStr, channelCountryCode);

                    foreach (var variant in GetNameVariantsWithCountryHint(channel.Name, channelCountryCode))
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
                        foreach (var variant in GetNameVariantsWithCountryHint(channel.TvgName, channelCountryCode))
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
                                    var displayNameVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                                            var displayCountryCode = DetectExplicitCountryCode(displayName) ?? sourceCountryCode;
                                            foreach (var variant in GetNameVariantsWithCountryHint(displayName, displayCountryCode))
                                            {
                                                displayNameVariants.Add(variant);
                                            }
                                        }
                                    }

                                    if (string.IsNullOrWhiteSpace(effectiveXmlId))
                                    {
                                        continue;
                                    }

                                    List<string>? exactTvgIds = null;
                                    var hadExactTvgId = tvgIdToInternalIds.TryGetValue(effectiveXmlId, out var byTvgIds);
                                    if (hadExactTvgId)
                                    {
                                        exactTvgIds = FilterIdsByGroupCountry(byTvgIds, internalIdToGroupCountry, sourceCountryCode);
                                        if (exactTvgIds == null || exactTvgIds.Count == 0)
                                        {
                                            // An explicit ID that belongs to another country must not fall back
                                            // to a weaker name match.
                                            continue;
                                        }
                                    }

                                    var resolvedIds = ResolveXmlChannelTargets(exactTvgIds, displayNameVariants, channelMap);
                                    resolvedIds = FilterIdsByGroupCountry(resolvedIds, internalIdToGroupCountry, sourceCountryCode);
                                    if (resolvedIds == null || resolvedIds.Count == 0)
                                    {
                                        continue;
                                    }

                                    xmlChannelIdToDbTvgIds[effectiveXmlId] = resolvedIds;
                                    if (isPrimary)
                                    {
                                        allowedPrimaryIds.Add(effectiveXmlId);
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

                                if (!IsAllowedPrimaryGroupCountryCompatible(channel, sourceCountryCode, allowedPrimaryIdGroupCountries))
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
                                    ProgressPercent = Math.Min(98, 30 + (65 * (1 - Math.Exp(-totalLoaded / 20000.0)))),
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

        foreach (var program in programs
            .OrderBy(p => p.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.StartTime)
            .ThenByDescending(p => p.EndTime))
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

            var overlapRatio = GetOverlapRatio(
                program.StartTime,
                program.EndTime,
                candidate.StartTime,
                candidate.EndTime);

            if (overlapRatio >= 0.95)
            {
                return true;
            }

            if (overlapRatio >= 0.80 &&
                string.Equals(NormalizeEpgTitle(candidate.Title), title, StringComparison.Ordinal))
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

        foreach (var program in programs
            .OrderBy(p => p.StartTime)
            .ThenByDescending(p => p.EndTime))
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
                else if (previous.StartTime == program.StartTime)
                {
                    if (previous.EndTime >= program.EndTime)
                    {
                        goto NextProgram;
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
        => GetNameVariantsWithCountryHint(name, null);

    private IEnumerable<string> GetNameVariantsWithCountryHint(string name, string? countryHint)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Explicit playlist/XMLTV metadata is stronger evidence than words in the
        // channel name. Name detection remains a fallback for metadata-poor lists.
        // Only explicit metadata/prefixes may become a hard country identity.
        // Broad name patterns (for example "Haber", "ATV" or "TLC") are useful
        // for suggestions, but are too ambiguous for EPG ownership.
        var countryCode = NormalizeExplicitCountryCode(countryHint)
            ?? DetectExplicitCountryCode(name)
            ?? "XX";

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
            "backup", "bkp", "multi", "sub", "ace",
            "turkey", "turkiye", "türkiye", "tr", "tur",
            "de", "ger", "germany", "deutschland", "at", "aut", "austria", "osterreich",
            "gb", "uk", "en", "eng", "england", "britain",
            "us", "usa", "america", "ca", "can", "canada", "au", "aus", "australia", "nz", "nzl", "zealand",
            "fr", "fra", "france", "be", "bel", "belgium",
            "it", "ita", "italy", "italia",
            "es", "esp", "spain", "espana",
            "mx", "mex", "mexico",
            "ar", "arg", "argentina",
            "br", "bra", "brazil",
            "pt", "portugal",
            "nl", "nld", "netherlands",
            "ru", "rus", "russia",
            "pl", "pol", "poland", "polska",
            "ro", "rom", "romania",
            "bg", "bgr", "bulgaria",
            "cz", "cze", "czech", "czechia",
            "sk", "svk", "slovakia",
            "hr", "hrv", "croatia",
            "rs", "srb", "serbia",
            "si", "svn", "slovenia",
            "ba", "bih", "bosnia",
            "mk", "mkd", "macedonia",
            "ua", "ukr", "ukraine",
            "by", "blr", "belarus",
            "al", "alb", "albania",
            "ge", "geo", "georgia",
            "gr", "gre", "greece",
            "hu", "hun", "hungary",
            "no", "nor", "norway",
            "dk", "dnk", "denmark",
            "fi", "fin", "finland",
            "ie", "irl", "ireland",
            "hk", "se", "swe", "sweden",
            "ch", "che", "switzerland",
            "in", "ind", "india", "pk", "pak", "pakistan",
            "id", "idn", "indonesia", "my", "mys", "malaysia", "th", "tha", "thailand", "ph", "phl", "philippines",
            "jp", "jpn", "japan", "kr", "kor", "korea", "cn", "chn", "china", "tw", "twn", "taiwan",
            "il", "isr", "israel", "ir", "irn", "iran", "sa", "sau", "saudi", "ae", "uae", "qa", "qat", "qatar",
            "eg", "egy", "egypt", "ma", "mar", "morocco", "tn", "tun", "tunisia",
            "yayin", "kesintisiz",
        };

        var tokens = new string(chars.ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !noise.Contains(t));

        return string.Concat(tokens);
    }

    private static void AddAllowedPrimaryGroupCountry(
        Dictionary<string, HashSet<string>> groupCountriesByPrimaryId,
        string primaryId,
        string? groupCountryCode)
    {
        if (string.IsNullOrWhiteSpace(primaryId) || string.IsNullOrWhiteSpace(groupCountryCode))
            return;

        if (!groupCountriesByPrimaryId.TryGetValue(primaryId, out var countries))
        {
            countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            groupCountriesByPrimaryId[primaryId] = countries;
        }

        countries.Add(groupCountryCode);
    }

    private static List<string>? FilterIdsByGroupCountry(
        List<string>? ids,
        Dictionary<string, string> internalIdToGroupCountry,
        string? sourceCountryCode)
    {
        var sourceCountry = NormalizeExplicitCountryCode(sourceCountryCode);
        if (ids == null || ids.Count == 0 || string.IsNullOrWhiteSpace(sourceCountry))
            return ids;

        var filtered = ids
            .Where(id =>
                !internalIdToGroupCountry.TryGetValue(id, out var groupCountry) ||
                string.Equals(groupCountry, sourceCountry, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return filtered.Count == 0 ? null : filtered;
    }

    private static bool IsAllowedPrimaryGroupCountryCompatible(
        string primaryId,
        string? sourceCountryCode,
        Dictionary<string, HashSet<string>> groupCountriesByPrimaryId)
    {
        var sourceCountry = NormalizeExplicitCountryCode(sourceCountryCode);
        if (string.IsNullOrWhiteSpace(primaryId) || string.IsNullOrWhiteSpace(sourceCountry))
            return true;

        if (!groupCountriesByPrimaryId.TryGetValue(primaryId, out var countries) || countries.Count == 0)
            return true;

        return countries.Contains(sourceCountry);
    }

    private static string? DetectExplicitCountryCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalizedValue = RemoveDiacritics(value);
        var tokens = System.Text.RegularExpressions.Regex
            .Split(normalizedValue, @"[^A-Za-z]+")
            .Where(token => !string.IsNullOrWhiteSpace(token));

        foreach (var token in tokens)
        {
            var countryCode = NormalizeExplicitCountryCode(token);
            if (!string.IsNullOrWhiteSpace(countryCode))
                return countryCode;
        }

        return null;
    }

    private static string? NormalizeExplicitCountryCode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return RemoveDiacritics(token).Trim().ToUpperInvariant() switch
        {
            "TR" or "TUR" or "TURKEY" or "TURKIYE" => "TR",
            "DE" or "GER" or "GERMANY" or "DEUTSCHLAND" => "DE",
            "AT" or "AUT" or "AUSTRIA" or "OSTERREICH" => "AT",
            "GB" or "UK" or "EN" or "ENG" or "ENGLAND" or "BRITAIN" => "GB",
            "US" or "USA" or "AMERICA" => "US",
            "CA" or "CAN" or "CANADA" => "CA",
            "AU" or "AUS" or "AUSTRALIA" => "AU",
            "NZ" or "NZL" or "ZEALAND" => "NZ",
            "FR" or "FRA" or "FRANCE" => "FR",
            "BE" or "BEL" or "BELGIUM" => "BE",
            "IT" or "ITA" or "ITALY" or "ITALIA" => "IT",
            "ES" or "ESP" or "SPAIN" or "ESPANA" => "ES",
            "NL" or "NLD" or "NETHERLANDS" => "NL",
            "RU" or "RUS" or "RUSSIA" => "RU",
            "PL" or "POL" or "POLAND" or "POLSKA" => "PL",
            "RO" or "ROM" or "ROMANIA" => "RO",
            "BG" or "BGR" or "BULGARIA" => "BG",
            "CZ" or "CZE" or "CZECH" or "CZECHIA" => "CZ",
            "SK" or "SVK" or "SLOVAKIA" => "SK",
            "HR" or "HRV" or "CROATIA" => "HR",
            "RS" or "SRB" or "SERBIA" => "RS",
            "SI" or "SVN" or "SLOVENIA" => "SI",
            "BA" or "BIH" or "BOSNIA" => "BA",
            "MK" or "MKD" or "MACEDONIA" => "MK",
            "UA" or "UKR" or "UKRAINE" => "UA",
            "BY" or "BLR" or "BELARUS" => "BY",
            "PT" or "PRT" or "PORTUGAL" => "PT",
            "BR" or "BRA" or "BRAZIL" => "BR",
            "MX" or "MEX" or "MEXICO" => "MX",
            "AR" or "ARG" or "ARGENTINA" => "AR",
            "AL" or "ALB" or "ALBANIA" => "AL",
            "GE" or "GEO" or "GEORGIA" => "GE",
            "GR" or "GRE" or "GREECE" => "GR",
            "HU" or "HUN" or "HUNGARY" => "HU",
            "NO" or "NOR" or "NORWAY" => "NO",
            "DK" or "DNK" or "DENMARK" => "DK",
            "FI" or "FIN" or "FINLAND" => "FI",
            "IE" or "IRL" or "IRELAND" => "IE",
            "HK" => "HK",
            "SE" or "SWE" or "SWEDEN" => "SE",
            "CH" or "CHE" or "SWITZERLAND" => "CH",
            "IN" or "IND" or "INDIA" => "IN",
            "PK" or "PAK" or "PAKISTAN" => "PK",
            "ID" or "IDN" or "INDONESIA" => "ID",
            "MY" or "MYS" or "MALAYSIA" => "MY",
            "TH" or "THA" or "THAILAND" => "TH",
            "PH" or "PHL" or "PHILIPPINES" => "PH",
            "JP" or "JPN" or "JAPAN" => "JP",
            "KR" or "KOR" or "KOREA" => "KR",
            "CN" or "CHN" or "CHINA" => "CN",
            "TW" or "TWN" or "TAIWAN" => "TW",
            "IL" or "ISR" or "ISRAEL" => "IL",
            "IR" or "IRN" or "IRAN" => "IR",
            "SA" or "SAU" or "SAUDI" => "SA",
            "AE" or "UAE" => "AE",
            "QA" or "QAT" or "QATAR" => "QA",
            "EG" or "EGY" or "EGYPT" => "EG",
            "MA" or "MAR" or "MOROCCO" => "MA",
            "TN" or "TUN" or "TUNISIA" => "TN",
            _ => null
        };
    }

    private static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    private static List<string>? ResolveXmlChannelTargets(
        List<string>? exactTvgIds,
        IEnumerable<string> displayNameVariants,
        Dictionary<string, List<string>> channelMap)
    {
        var exactIds = DistinctChannelIds(exactTvgIds);
        if (exactIds.Count > 0)
        {
            return exactIds;
        }

        var variants = displayNameVariants
            .Where(variant => !string.IsNullOrWhiteSpace(variant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Exact display-name evidence is evaluated as a set. Multiple names in the
        // same XMLTV channel may be aliases, but conflicting exact targets are unsafe.
        var exactNameCandidates = variants
            .Where(channelMap.ContainsKey)
            .Select(variant => channelMap[variant]);
        var exactNameTarget = SelectSingleTargetSet(exactNameCandidates, out var exactNameConflict);
        if (exactNameConflict)
        {
            return null;
        }

        if (exactNameTarget != null)
        {
            return exactNameTarget;
        }

        var fuzzyCandidates = variants
            .Select(variant => ResolveMappedChannelIds(variant, channelMap))
            .Where(ids => ids != null)
            .Select(ids => ids!);
        return SelectSingleTargetSet(fuzzyCandidates, out _);
    }

    private static List<string>? SelectSingleTargetSet(
        IEnumerable<List<string>> candidates,
        out bool conflict)
    {
        conflict = false;
        List<string>? selected = null;
        string? selectedKey = null;

        foreach (var candidate in candidates)
        {
            var distinctIds = DistinctChannelIds(candidate);
            if (distinctIds.Count == 0)
            {
                continue;
            }

            var key = BuildChannelIdSetKey(distinctIds);
            if (selected == null)
            {
                selected = distinctIds;
                selectedKey = key;
                continue;
            }

            if (!string.Equals(selectedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                conflict = true;
                return null;
            }
        }

        return selected;
    }

    private static List<string> DistinctChannelIds(IEnumerable<string>? ids)
    {
        if (ids == null)
        {
            return [];
        }

        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildChannelIdSetKey(IEnumerable<string> ids)
        => string.Join('\u001F', ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));

    private static List<string>? ResolveMappedChannelIds(string normalizedDisplayName, Dictionary<string, List<string>> channelMap)
    {
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || normalizedDisplayName.Length < 3)
            return null;

        // Fast path: Exact match (O(1))
        if (channelMap.TryGetValue(normalizedDisplayName, out var exactList))
        {
            return exactList;
        }

        // Fuzzy matching is deliberately conservative: a wrong guide is worse than
        // no guide. Exact IDs and exact normalized names are handled above.
        if (normalizedDisplayName.Length < 4)
            return null;

        const double minimumConfidence = 0.92;
        const double minimumRunnerUpMargin = 0.06;
        var (displayCountry, displayBaseName) = SplitCountryQualifiedName(normalizedDisplayName);
        var candidatesByTargetSet = new Dictionary<string, (List<string> Ids, double Score)>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in channelMap)
        {
            var (candidateCountry, candidateBaseName) = SplitCountryQualifiedName(kvp.Key);
            if (!string.IsNullOrWhiteSpace(displayCountry) &&
                !string.IsNullOrWhiteSpace(candidateCountry) &&
                !string.Equals(displayCountry, candidateCountry, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (displayBaseName.Length == 0 || candidateBaseName.Length == 0 ||
                displayBaseName[0] != candidateBaseName[0])
            {
                continue;
            }

            var score = Similarity(displayBaseName, candidateBaseName);
            if (score < minimumConfidence)
            {
                continue;
            }

            var ids = DistinctChannelIds(kvp.Value);
            if (ids.Count == 0)
            {
                continue;
            }

            var targetSetKey = BuildChannelIdSetKey(ids);
            if (!candidatesByTargetSet.TryGetValue(targetSetKey, out var current) || score > current.Score)
            {
                candidatesByTargetSet[targetSetKey] = (ids, score);
            }
        }

        var rankedCandidates = candidatesByTargetSet.Values
            .OrderByDescending(candidate => candidate.Score)
            .ToList();
        if (rankedCandidates.Count == 0)
        {
            return null;
        }

        if (rankedCandidates.Count > 1 &&
            rankedCandidates[0].Score - rankedCandidates[1].Score < minimumRunnerUpMargin)
        {
            return null;
        }

        return rankedCandidates[0].Ids;
    }

    private static (string? Country, string BaseName) SplitCountryQualifiedName(string value)
    {
        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return (null, value);
        }

        var country = value[..separatorIndex];
        return (string.Equals(country, "XX", StringComparison.OrdinalIgnoreCase) ? null : country,
            value[(separatorIndex + 1)..]);
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
        if (!_settingsService.Settings.EpgEnabled)
        {
            return null;
        }

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
        if (!_settingsService.Settings.EpgEnabled)
        {
            return new Dictionary<int, EpgProgram?>();
        }

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EpgService] Failed to parse XMLTV date '{dateStr}': {ex.Message}");
        }

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
