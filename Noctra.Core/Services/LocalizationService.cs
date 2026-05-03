using System.Text.Json;
using Microsoft.Extensions.Logging;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class LocalizationService : ILocalizationService
{
    private const string FallbackLanguage = "en-US";
    private readonly ILogger<LocalizationService>? _logger;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _translations;

    public LocalizationService(ILogger<LocalizationService>? logger = null)
    {
        _logger = logger;
        _translations = LoadTranslations();
        CurrentLanguage = NormalizeLanguageCode("tr");
    }

    public string CurrentLanguage { get; private set; }

    public event Action? LanguageChanged;

    public string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (TryGetTranslation(CurrentLanguage, key, out var value))
        {
            return value;
        }

        if (TryGetTranslation(FallbackLanguage, key, out value))
        {
            _logger?.LogDebug("Localization key {Key} missing in {Language}; using {FallbackLanguage}.", key, CurrentLanguage, FallbackLanguage);
            return value;
        }

        _logger?.LogWarning("Localization key {Key} missing in {Language} and fallback {FallbackLanguage}.", key, CurrentLanguage, FallbackLanguage);
        return key;
    }

    public void SetLanguage(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        if (string.Equals(CurrentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentLanguage = normalized;
        LanguageChanged?.Invoke();
    }

    internal static string NormalizeLanguageCode(string? languageCode)
    {
        var normalized = (languageCode ?? "tr").Trim().ToLowerInvariant();
        return normalized switch
        {
            "tr" or "tr-tr" => "tr-TR",
            "en" or "en-us" => "en-US",
            "de" or "de-de" => "de-DE",
            "fr" or "fr-fr" => "fr-FR",
            "es" or "es-es" => "es-ES",
            _ => "tr-TR"
        };
    }

    private bool TryGetTranslation(string language, string key, out string value)
    {
        value = string.Empty;
        if (!_translations.TryGetValue(language, out var dictionary))
        {
            return false;
        }

        return dictionary.TryGetValue(key, out value!);
    }

    private Dictionary<string, IReadOnlyDictionary<string, string>> LoadTranslations()
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Localization.Translations.", StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in resourceNames)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                var json = reader.ReadToEnd();
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                var language = resourceName.Split('.')[^2];
                
                translations[language] = new Dictionary<string, string>(values, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load translation resource: {ResourceName}", resourceName);
            }
        }

        return translations;
    }
}
