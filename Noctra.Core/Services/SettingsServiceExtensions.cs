using System;
using System.Threading.Tasks;

namespace Noctra.Services;

/// <summary>
/// ISettingsService.SaveAsync çağrıları için yardımcılar.
/// </summary>
public static class SettingsServiceExtensions
{
    /// <summary>
    /// Fire-and-forget çağrıları için SaveAsync'in hatayı yutan sürümü.
    /// Hata zaten SettingsService içinde loglanır; çağıranın sonucu
    /// doğrulaması gerekmediği durumlarda unobserved exception üretmeyi engeller.
    /// </summary>
    public static async Task SaveAsyncBestEffort(this ISettingsService settingsService)
    {
        try
        {
            await settingsService.SaveAsync().ConfigureAwait(false);
        }
        catch (SettingsPersistenceException)
        {
            // Log already written by SettingsService.
        }
    }
}
