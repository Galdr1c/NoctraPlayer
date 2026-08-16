using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.Core.Advertising;

/// <summary>
/// Fetches ad placement rules from a trusted endpoint instead of hard-coding
/// them into the APK/AAB. Any failure falls back to ConservativeDefault
/// (fail-closed): a config error can never make ads more aggressive.
///
/// The URL is embedded at Release build time (NOCTRA_ADVERTISING_CONFIG_URL →
/// AssemblyMetadata, see Noctra.Core.csproj); DEBUG honors the runtime
/// environment variable so tests and local runs stay deterministic.
///
/// Expected JSON (all sections and fields optional; parsing merges
/// field-by-field — a missing or invalid field keeps the in-app default for
/// that field only, so a minimal { "enabled": false } payload acts as an
/// emergency kill switch without breaking the other placements):
/// {
///   "schemaVersion": 1,
///   "movies":  { "enabled": true,  "spacing": 14, "max": 2 },
///   "series":  { "enabled": true,  "spacing": 14, "max": 2 },
///   "live":    { "enabled": true,  "spacing": 20, "max": 1 },
///   "search":  { "enabled": true,  "spacing": 10, "max": 1 },
///   "home":    { "enabled": false, "spacing": 6,  "max": 1 },
///   "playbackExit": {
///     "enabled": true,
///     "minSessionAgeMinutes": 5,
///     "minPlaybackDurationMinutes": 10,
///     "cooldownMinutes": 18,
///     "maxPerHour": 2,
///     "maxPerDay": 4,
///     "allowLive": false
///   }
/// }
/// </summary>
public sealed class RemoteAdvertisingConfigService : IRemoteAdvertisingConfigService
{
    private const long MaxConfigBytes = 16 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _httpClient;
    private volatile AdvertisingOptions _current = AdvertisingOptions.ConservativeDefault;

    public RemoteAdvertisingConfigService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public event EventHandler? OptionsChanged;

    public AdvertisingOptions CurrentOptions => _current;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var url = ResolveConfigUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true
            };

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (response.Content.Headers.ContentLength.HasValue &&
                response.Content.Headers.ContentLength.Value > MaxConfigBytes)
            {
                return;
            }

            var json = await ReadContentWithLimitAsync(response.Content, cts.Token);
            if (TryParse(json, out var parsed))
            {
                _current = parsed;
                OptionsChanged?.Invoke(this, EventArgs.Empty);
                Noctra.Diagnostics.PerformanceTrace.Mark(
                    "Ads.RemoteConfig",
                    parsed.Movies.MinContentSpacing * 100 + parsed.Movies.MaxSlots,
                    scope: "movies");
            }
        }
        catch (Exception ex) when (
            ex is OperationCanceledException or
            HttpRequestException or
            JsonException or
            IOException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[RemoteAdvertisingConfig] fetch failed: {ex.Message}");
        }
    }

    public static bool TryParse(string json, out AdvertisingOptions options)
    {
        options = AdvertisingOptions.ConservativeDefault;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            options = new AdvertisingOptions
            {
                Movies = ParseNative(root, "movies", options.Movies),
                Series = ParseNative(root, "series", options.Series),
                Live = ParseNative(root, "live", options.Live),
                Search = ParseNative(root, "search", options.Search),
                Home = ParseNative(root, "home", options.Home),
                PlaybackExit = ParseInterstitial(root, options.PlaybackExit)
            };
            return true;
        }
        catch (JsonException)
        {
            options = AdvertisingOptions.ConservativeDefault;
            return false;
        }
    }

    private static NativeAdPlacementOptions ParseNative(
        JsonElement root,
        string sectionName,
        NativeAdPlacementOptions fallback)
    {
        if (!root.TryGetProperty(sectionName, out var section) ||
            section.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        var enabled = fallback.Enabled;
        if (section.TryGetProperty("enabled", out var enabledProperty) &&
            enabledProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            enabled = enabledProperty.ValueKind == JsonValueKind.True;
        }

        var spacing = fallback.MinContentSpacing;
        if (section.TryGetProperty("spacing", out var spacingProperty) &&
            spacingProperty.ValueKind == JsonValueKind.Number &&
            spacingProperty.TryGetInt32(out var parsedSpacing) &&
            parsedSpacing >= RemoteAdvertisingLimits.NativeMinSpacing &&
            parsedSpacing <= RemoteAdvertisingLimits.NativeMaxSpacing)
        {
            spacing = parsedSpacing;
        }

        var max = fallback.MaxSlots;
        if (section.TryGetProperty("max", out var maxProperty) &&
            maxProperty.ValueKind == JsonValueKind.Number &&
            maxProperty.TryGetInt32(out var parsedMax) &&
            parsedMax >= 0 &&
            parsedMax <= RemoteAdvertisingLimits.NativeMaxSlots)
        {
            max = parsedMax;
        }

        return new NativeAdPlacementOptions(enabled, spacing, max);
    }

    private static InterstitialAdOptions ParseInterstitial(
        JsonElement root,
        InterstitialAdOptions fallback)
    {
        if (!root.TryGetProperty("playbackExit", out var section) ||
            section.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        var enabled = fallback.Enabled;
        if (section.TryGetProperty("enabled", out var enabledProperty) &&
            enabledProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            enabled = enabledProperty.ValueKind == JsonValueKind.True;
        }

        var sessionAge = fallback.MinSessionAge;
        if (TryGetIntInRange(
                section,
                "minSessionAgeMinutes",
                RemoteAdvertisingLimits.InterstitialMinSessionAgeMinutes,
                RemoteAdvertisingLimits.InterstitialMaxSessionAgeMinutes,
                out var parsedSessionAge))
        {
            sessionAge = TimeSpan.FromMinutes(parsedSessionAge);
        }

        var playbackDuration = fallback.MinPlaybackDuration;
        if (TryGetIntInRange(
                section,
                "minPlaybackDurationMinutes",
                RemoteAdvertisingLimits.InterstitialMinPlaybackMinutes,
                RemoteAdvertisingLimits.InterstitialMaxPlaybackMinutes,
                out var parsedPlaybackDuration))
        {
            playbackDuration = TimeSpan.FromMinutes(parsedPlaybackDuration);
        }

        var cooldown = fallback.Cooldown;
        if (TryGetIntInRange(
                section,
                "cooldownMinutes",
                RemoteAdvertisingLimits.InterstitialMinCooldownMinutes,
                RemoteAdvertisingLimits.InterstitialMaxCooldownMinutes,
                out var parsedCooldown))
        {
            cooldown = TimeSpan.FromMinutes(parsedCooldown);
        }

        var maxPerHour = fallback.MaxPerHour;
        if (TryGetIntInRange(
                section,
                "maxPerHour",
                0,
                RemoteAdvertisingLimits.InterstitialMaxPerHour,
                out var parsedMaxPerHour))
        {
            maxPerHour = parsedMaxPerHour;
        }

        var maxPerDay = fallback.MaxPerDay;
        if (TryGetIntInRange(
                section,
                "maxPerDay",
                0,
                RemoteAdvertisingLimits.InterstitialMaxPerDay,
                out var parsedMaxPerDay))
        {
            maxPerDay = parsedMaxPerDay;
        }

        var allowLive = fallback.AllowLiveContent;
        if (section.TryGetProperty("allowLive", out var allowLiveProperty) &&
            allowLiveProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            allowLive = allowLiveProperty.ValueKind == JsonValueKind.True;
        }

        return new InterstitialAdOptions(
            enabled,
            sessionAge,
            playbackDuration,
            cooldown,
            maxPerHour,
            maxPerDay,
            allowLive);
    }

    private static bool TryGetIntInRange(
        JsonElement section,
        string propertyName,
        int min,
        int max,
        out int value)
    {
        value = 0;
        if (!section.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var parsed) ||
            parsed < min ||
            parsed > max)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static string ResolveConfigUrl()
    {
#if DEBUG
        // DEBUG'da URL yalnızca runtime ortamından gelir (LoadDotEnv → .env
        // veya NOCTRA_ADVERTISING_CONFIG_URL). Build-time metadata okunmaz;
        // böylece testler .env içeriğinden bağımsız, deterministik kalır.
        var overrideUrl = Environment.GetEnvironmentVariable("NOCTRA_ADVERTISING_CONFIG_URL");
        if (!string.IsNullOrWhiteSpace(overrideUrl))
        {
            return overrideUrl.Trim();
        }

        return string.Empty;
#else
        return Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(
                a.Key,
                "Noctra.AdvertisingConfigUrl",
                StringComparison.Ordinal))
            ?.Value ?? string.Empty;
#endif
    }

    private static async Task<string> ReadContentWithLimitAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxConfigBytes)
            {
                throw new IOException("Advertising config exceeds size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}