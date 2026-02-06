using IPTVPlayer.Services.Interfaces;
using IPTVPlayer.Models;
using Windows.Services.Store;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace IPTVPlayer.WinUI.Services;

public sealed class StoreEntitlementService : ILicenseService
{
    private readonly StoreContext _context;
    private SubscriptionInfo _currentSubscription;
    
    // Partner Center'daki Durable add-on Store ID
    // TODO: Replace with actual ID from Partner Center
    private const string PremiumLifetimeAddOnId = "9PXXXXX"; 
    
    public event EventHandler<SubscriptionInfo>? SubscriptionChanged;

    public StoreLicenseService()
    {
        // StoreContext.GetDefault() works for packaged apps
        _context = StoreContext.GetDefault();
        
        _currentSubscription = new SubscriptionInfo 
        { 
            Tier = SubscriptionTier.Free,
            ExpiryDate = DateTime.MaxValue
        };
    }

    public SubscriptionInfo GetSubscription() => _currentSubscription;

    public async Task InitializeAsync()
    {
        await CheckLicenseAsync();
    }

    public async Task<bool> PurchasePremiumAsync()
    {
        try
        {
            // IMPORTANT: Must be called on UI thread
            // WinUI 3 handles the UI context associated with StoreContext automatically for GetDefault() in packaged apps?
            // If not, we might need IInitializeWithWindow, but standard practice for Packaged App is GetDefault().
            
            StorePurchaseResult result = await _context.RequestPurchaseAsync(PremiumLifetimeAddOnId);

            if (result.Status == StorePurchaseStatus.Succeeded || 
                result.Status == StorePurchaseStatus.AlreadyPurchased)
            {
                // Satın alma sonrası lisansı tekrar oku
                await CheckLicenseAsync();
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase Error: {ex}");
        }
        return false;
    }

    public async Task CheckLicenseAsync()
    {
        try
        {
            // license cached olabilir (offline support)
            StoreAppLicense license = await _context.GetAppLicenseAsync();
            
            bool isActive = license.AddOnLicenses.TryGetValue(PremiumLifetimeAddOnId, out var addOnLicense) 
                            && addOnLicense.IsActive;

            var newTier = isActive ? SubscriptionTier.Premium : SubscriptionTier.Free;
            
            if (_currentSubscription.Tier != newTier)
            {
                _currentSubscription.Tier = newTier;
                SubscriptionChanged?.Invoke(this, _currentSubscription);
            }
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"License Check Error: {ex}");
        }
    }

    public bool IsFeatureAvailable(string featureName)
    {
        var tier = _currentSubscription.Tier;
        
        return featureName switch
        {
            "AudioTrackSelection" => tier == SubscriptionTier.Premium,
            "SubtitleTrackSelection" => tier == SubscriptionTier.Premium,
            "AdFree" => tier == SubscriptionTier.Premium,
            "FullEpg" => tier == SubscriptionTier.Premium,
            "Multiview" => tier == SubscriptionTier.Premium,
            "Timeshift" => tier == SubscriptionTier.Premium,
             _ => true
        };
    }

    public string GetPriceText() => "480 TL (Tek Sefer)";
}
