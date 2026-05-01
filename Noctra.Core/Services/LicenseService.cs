using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// License service implementation
/// Free and Premium Store packages are edition-driven.
/// </summary>
public class LicenseService : ObservableObject, ILicenseService
{
    private SubscriptionInfo _currentSubscription = new();
    private readonly IAppEditionService _appEditionService;

    // ==========================================
    // FEATURE NAMES
    // ==========================================
    public static class Features
    {
        public const string AdFree = "ad_free";
        public const string EpgAutoRefresh = "epg_auto_refresh";
        public const string ResumePlayback = "resume_playback";
        public const string SleepTimer = "sleep_timer";
    }

    // ==========================================
    // LIMIT NAMES
    // ==========================================
    public static class Limits
    {
        public const string Profiles = "profiles";
        public const string CustomEpgUrls = "custom_epg_urls";
    }

    public LicenseService(IAppEditionService appEditionService)
    {
        _appEditionService = appEditionService;
        _currentSubscription.Tier = _appEditionService.IsPremiumEdition
            ? SubscriptionTier.Premium
            : SubscriptionTier.Free;
    }

    // ==========================================
    // LEGACY PROPERTIES (backward compat)
    // ==========================================
    public bool IsPremium => _currentSubscription.IsPremiumOrHigher;
    public bool CanUpgradeToPremium => _appEditionService.IsFreeEdition;
    public bool IsEditionLockedPremium => _appEditionService.IsPremiumEdition;

    public void ActivatePremium()
    {
        if (_appEditionService.IsPremiumEdition)
        {
            return;
        }

        _currentSubscription.Tier = SubscriptionTier.Premium;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        OnPropertyChanged(nameof(CanUpgradeToPremium));
        SubscriptionChanged?.Invoke();
    }

    public void DeactivatePremium()
    {
        if (_appEditionService.IsPremiumEdition || _currentSubscription.Tier == SubscriptionTier.Free)
        {
            return;
        }

        _currentSubscription.Tier = SubscriptionTier.Free;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        OnPropertyChanged(nameof(CanUpgradeToPremium));
        SubscriptionChanged?.Invoke();
    }

    public string GetPriceText()
    {
        return "499.95 TL (Tek Sefer)";
    }

    // ==========================================
    // NEW TIER SYSTEM
    // ==========================================

    public SubscriptionInfo GetCurrentSubscription()
    {
        return _currentSubscription;
    }

    public SubscriptionTier CurrentTier => _currentSubscription.Tier;

    public bool IsFeatureAvailable(string featureName)
    {
        var tier = _currentSubscription.Tier;

        return featureName switch
        {
            Features.AdFree => tier == SubscriptionTier.Premium,
            Features.EpgAutoRefresh => tier == SubscriptionTier.Premium,
            Features.ResumePlayback => tier == SubscriptionTier.Premium,
            Features.SleepTimer => tier == SubscriptionTier.Premium,
            _ => false
        };
    }

    public bool IsWithinLimit(string limitName, int currentCount)
    {
        var tier = _currentSubscription.Tier;

        int maxAllowed = limitName switch
        {
            Limits.Profiles => tier == SubscriptionTier.Premium
                ? TierLimits.Premium.MaxProfiles
                : TierLimits.Free.MaxProfiles,
            Limits.CustomEpgUrls => tier == SubscriptionTier.Premium
                ? TierLimits.Premium.MaxCustomEpgUrls
                : TierLimits.Free.MaxCustomEpgUrls,
            _ => int.MaxValue
        };

        return currentCount < maxAllowed;
    }

    public int GetLimit(string limitName)
    {
        var tier = _currentSubscription.Tier;

        return limitName switch
        {
            Limits.Profiles => tier == SubscriptionTier.Premium
                ? TierLimits.Premium.MaxProfiles
                : TierLimits.Free.MaxProfiles,
            Limits.CustomEpgUrls => tier == SubscriptionTier.Premium
                ? TierLimits.Premium.MaxCustomEpgUrls
                : TierLimits.Free.MaxCustomEpgUrls,
            _ => int.MaxValue
        };
    }

    public event Action? SubscriptionChanged;

    public async Task<bool> StartPurchaseFlowAsync(SubscriptionTier targetTier)
    {
        if (targetTier != SubscriptionTier.Premium || _appEditionService.IsPremiumEdition)
        {
            return false;
        }

        var candidateUris = new[]
        {
            _appEditionService.PremiumStoreLaunchUri,
            _appEditionService.PremiumStoreWebUri
        };

        foreach (var candidate in candidateUris.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = true
                });
                return await Task.FromResult(true);
            }
            catch
            {
                // Try the next URI.
            }
        }

        return false;
    }

    public async Task RefreshSubscriptionStatusAsync()
    {
        _currentSubscription.Tier = _appEditionService.IsPremiumEdition
            ? SubscriptionTier.Premium
            : _currentSubscription.Tier;

        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        OnPropertyChanged(nameof(CanUpgradeToPremium));
        await Task.CompletedTask;
    }

    /// <summary>
    /// Debug/test iÃ§in tier'Ä± manuel ayarla (Event fÄ±rlatmaz)
    /// </summary>
    public void SetTierForTesting(SubscriptionTier tier)
    {
        if (_appEditionService.IsPremiumEdition && tier != SubscriptionTier.Premium)
        {
            return;
        }

        _currentSubscription.Tier = tier;
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(CurrentTier));
        OnPropertyChanged(nameof(CanUpgradeToPremium));
    }
}
