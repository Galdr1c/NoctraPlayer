using System.Reflection;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public sealed class AppEditionService : IAppEditionService
{
    public AppEditionService()
    {
        var metadata = ReadMetadata();

        CurrentEdition = ParseEdition(GetMetadataValue(metadata, "NoctraEdition"));
        EditionDisplayName = GetMetadataValue(metadata, "NoctraEditionDisplayName") ??
            (CurrentEdition == AppEdition.Premium ? "Noctra Premium" : "Noctra");
        PremiumStoreProductId = GetMetadataValue(metadata, "NoctraPremiumStoreProductId") ?? string.Empty;
        PremiumStoreLaunchUri = GetMetadataValue(metadata, "NoctraPremiumStoreLaunchUri") ??
            BuildLaunchUri(PremiumStoreProductId);
        PremiumStoreWebUri = GetMetadataValue(metadata, "NoctraPremiumStoreWebUri") ?? string.Empty;
    }

    public AppEdition CurrentEdition { get; }

    public bool IsFreeEdition => CurrentEdition == AppEdition.Free;

    public bool IsPremiumEdition => CurrentEdition == AppEdition.Premium;

    public string EditionDisplayName { get; }

    public string PremiumStoreLaunchUri { get; }

    public string PremiumStoreWebUri { get; }

    public string PremiumStoreProductId { get; }

    private static IEnumerable<AssemblyMetadataAttribute> ReadMetadata()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            return entryAssembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        }

        return Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>();
    }

    private static string? GetMetadataValue(IEnumerable<AssemblyMetadataAttribute> metadata, string key)
    {
        return metadata.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static AppEdition ParseEdition(string? editionValue)
    {
        if (Enum.TryParse<AppEdition>(editionValue, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return AppEdition.Free;
    }

    private static string BuildLaunchUri(string productId)
    {
        return string.IsNullOrWhiteSpace(productId)
            ? string.Empty
            : $"ms-windows-store://pdp/?productid={productId}";
    }
}
