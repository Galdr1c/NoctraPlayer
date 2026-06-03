using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface IAppEditionService
{
    AppEdition CurrentEdition { get; }

    bool IsFreeEdition { get; }

    bool IsPremiumEdition { get; }

    string EditionDisplayName { get; }

    string PremiumStoreLaunchUri { get; }

    string PremiumStoreWebUri { get; }

    string PremiumStoreProductId { get; }

    string StoreProductId { get; }

    string StoreReviewLaunchUri { get; }
}
