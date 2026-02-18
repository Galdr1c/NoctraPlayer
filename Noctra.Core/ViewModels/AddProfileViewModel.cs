using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;

namespace Noctra.ViewModels;

public partial class AddProfileViewModel : ObservableObject
{
    private const string StalkerMacPrefix = "00:1A:79:";
    private const string ProfilesLimitKey = "profiles";
    private static readonly System.Text.RegularExpressions.Regex StalkerMacRegex = new(
        "^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$",
        System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly AppDbContext _context;
    private readonly IDispatcherService _dispatcherService;
    private readonly IAvatarService _avatarService;
    private readonly IDialogService _dialogService;
    private readonly ILicenseService _licenseService;
    private readonly IM3UParser _m3uParser;
    private readonly IXtreamCodesService _xtreamCodesService;
    private readonly IStalkerPortalService _stalkerPortalService;
    private readonly IContentDownloadService _contentDownloadService;

    // Simplified Account Details
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    private bool _isUpdatingUrl = false;

    partial void OnUrlChanged(string value)
    {
        PlaylistPreviewSummary = string.Empty;

        if (_isUpdatingUrl || string.IsNullOrEmpty(value)) return;
        
        try
        {
            _isUpdatingUrl = true;
            
            var lower = value.ToLower();
            
            // Auto-detect M3U
            if (lower.Contains(".m3u") || lower.Contains(".m3u8") || lower.Contains("get.php"))
            {
                if (!IsM3U) IsM3U = true;
            }
            else if (lower.Contains("stalker_portal") || lower.Contains("/portal"))
            {
                if (!IsStalker) IsStalker = true;
            }

            // Extract credentials from query string without changing the selected provider type.
            if (value.Contains("?"))
            {
                var uri = new Uri(value);
                var query = uri.Query;
                
                var usernameMatch = System.Text.RegularExpressions.Regex.Match(
                    query, @"[?&]username=([^&]+)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var passwordMatch = System.Text.RegularExpressions.Regex.Match(
                    query, @"[?&]password=([^&]+)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (usernameMatch.Success) 
                    Username = Uri.UnescapeDataString(usernameMatch.Groups[1].Value);
                if (passwordMatch.Success) 
                    Password = Uri.UnescapeDataString(passwordMatch.Groups[1].Value);
            }
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"URL parse error: {ex.Message}");
        }
        finally
        {
            _isUpdatingUrl = false;
        }

        ValidateRealtimeInputs();
    }

    private void ParseCredentialsFromUrl(string url)
    {
        try
        {
            if (!url.Contains("?")) return;

            var uri = new Uri(url);
            // Use Microsoft.AspNetCore.WebUtilities.QueryHelpers for parsing
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);

            if (query.TryGetValue("username", out var usernameValues))
            {
                var username = usernameValues.ToString();
                if (!string.IsNullOrWhiteSpace(username))
                {
                    _isUpdatingUrl = true;
                    Username = username;
                    _isUpdatingUrl = false;
                }
            }

            if (query.TryGetValue("password", out var passwordValues))
            {
                var password = passwordValues.ToString();
                if (!string.IsNullOrWhiteSpace(password))
                {
                    _isUpdatingUrl = true;
                    Password = password;
                    _isUpdatingUrl = false;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Credential parse error: {ex.Message}");
        }
    }

    private void ConvertM3UUrlToXtream(string m3uUrl)
    {
        try
        {
            var uri = new Uri(m3uUrl);
            
            // Extract base server URL (without get.php and query)
            var baseUrl = $"{uri.Scheme}://{uri.Host}";
            if (uri.Port != 80 && uri.Port != 443)
            {
                baseUrl += $":{uri.Port}";
            }

            _isUpdatingUrl = true;
            Url = baseUrl;
            _isUpdatingUrl = false;

            StatusMessage = "M3U linki Xtream formatına dönüştürüldü";
            HasError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("URL parse hatasi", ex);
            HasError = true;
        }
    }

    private void ConvertXtreamToM3UUrl()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Url) || 
                string.IsNullOrWhiteSpace(Username) || 
                string.IsNullOrWhiteSpace(Password))
            {
                return;
            }

            var baseUrl = Url.TrimEnd('/');
            if (!baseUrl.StartsWith("http://") && !baseUrl.StartsWith("https://"))
            {
                baseUrl = "http://" + baseUrl;
            }

            var m3uUrl = $"{baseUrl}/get.php?username={Uri.EscapeDataString(Username)}&password={Uri.EscapeDataString(Password)}&type=m3u_plus&output=ts";

            _isUpdatingUrl = true;
            Url = m3uUrl;
            _isUpdatingUrl = false;

            StatusMessage = "Xtream bilgileri M3U linkine dönüştürüldü";
            HasError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("URL olusturma hatasi", ex);
            HasError = true;
        }
    }

    [ObservableProperty]
    private string _username = string.Empty;

    partial void OnUsernameChanged(string value)
    {
        PlaylistPreviewSummary = string.Empty;

        if (_isUpdatingUrl) return;

        if (IsStalker)
        {
            var suffix = value;
            if (suffix.StartsWith(StalkerMacPrefix, StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix[StalkerMacPrefix.Length..];
            }

            _isUpdatingUrl = true;
            Username = StalkerMacPrefix + NormalizeStalkerMacSuffix(suffix);
            _isUpdatingUrl = false;
            UpdateStalkerMacValidation(Username);
            return;
        }
        
        if (IsM3U && !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Password))
        {
            ConvertXtreamToM3UUrl();
        }

        ValidateRealtimeInputs();
    }

    [ObservableProperty]
    private string _password = string.Empty;

    partial void OnPasswordChanged(string value)
    {
        PlaylistPreviewSummary = string.Empty;

        if (_isUpdatingUrl) return;
        
        if (IsM3U && !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Username))
        {
            ConvertXtreamToM3UUrl();
        }

        ValidateRealtimeInputs();
    }
    
        [ObservableProperty]
    private bool _isXtream = true;

    partial void OnIsXtreamChanged(bool value)
    {
        PlaylistPreviewSummary = string.Empty;

        if (_isUpdatingUrl) return;

        if (value)
        {
            IsM3U = false;
            IsStalker = false;

            if (!string.IsNullOrWhiteSpace(Url) && Url.Contains("get.php"))
            {
                ParseCredentialsFromUrl(Url);
                ConvertM3UUrlToXtream(Url);
            }
        }

        ValidateRealtimeInputs();
    }

    [ObservableProperty]
    private bool _isM3U = false;

    partial void OnIsM3UChanged(bool value)
    {
        PlaylistPreviewSummary = string.Empty;

        if (_isUpdatingUrl) return;

        if (value)
        {
            IsXtream = false;
            IsStalker = false;

            if (!string.IsNullOrWhiteSpace(Url) &&
                !string.IsNullOrWhiteSpace(Username) &&
                !string.IsNullOrWhiteSpace(Password))
            {
                ConvertXtreamToM3UUrl();
            }
        }

        ValidateRealtimeInputs();
    }

    [ObservableProperty]
    private bool _isStalker = false;

    partial void OnIsStalkerChanged(bool value)
    {
        PlaylistPreviewSummary = string.Empty;

        if (_isUpdatingUrl) return;

        if (value)
        {
            IsXtream = false;
            IsM3U = false;
            _isUpdatingUrl = true;
            Username = StalkerMacPrefix;
            _isUpdatingUrl = false;
            UpdateStalkerMacValidation(Username);
            ValidateRealtimeInputs();
            return;
        }

        if (!string.Equals(UrlError, "MAC adresi gecersiz. Ornek: 00:1A:79:AA:BB:CC", StringComparison.Ordinal) &&
            !string.Equals(UrlError, "Stalker Portal icin MAC adresi gereklidir", StringComparison.Ordinal))
        {
            return;
        }

        UrlError = null;
        ValidateRealtimeInputs();
    }

    private void ValidateRealtimeInputs()
    {
        if (IsStalker)
        {
            UpdateStalkerMacValidation(Username);
            return;
        }

        if (string.IsNullOrWhiteSpace(Url))
        {
            UrlError = "URL gereklidir";
            return;
        }

        var normalizedUrl = Url.Trim();
        if (!normalizedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalizedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = "http://" + normalizedUrl;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out _))
        {
            UrlError = "Gecersiz URL formati";
            return;
        }

        if (IsM3U)
        {
            var lower = normalizedUrl.ToLowerInvariant();
            if (!lower.Contains(".m3u") && !lower.Contains(".m3u8") && !lower.Contains("get.php"))
            {
                UrlError = "M3U URL'i .m3u, .m3u8 veya get.php icermelidir";
                return;
            }
        }

        if (IsXtream && (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)))
        {
            UrlError = "Xtream icin kullanici adi ve sifre gereklidir";
            return;
        }

        UrlError = null;
    }

    private void UpdateStalkerMacValidation(string currentUsername)
    {
        if (!IsStalker)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(currentUsername))
        {
            UrlError = "Stalker Portal icin MAC adresi gereklidir";
            return;
        }

        UrlError = StalkerMacRegex.IsMatch(currentUsername.Trim())
            ? null
            : "MAC adresi gecersiz. Ornek: 00:1A:79:AA:BB:CC";
    }

    private static string NormalizeStalkerMacSuffix(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var hex = new string(input
            .Where(c => Uri.IsHexDigit(c))
            .Select(char.ToUpperInvariant)
            .ToArray());

        if (hex.Length > 6)
        {
            hex = hex[..6];
        }

        if (hex.Length == 0)
        {
            return string.Empty;
        }

        var groups = Enumerable
            .Range(0, (hex.Length + 1) / 2)
            .Select(i =>
            {
                var start = i * 2;
                var len = Math.Min(2, hex.Length - start);
                return hex.Substring(start, len);
            });

        return string.Join(":", groups);
    }

    // Profile Details
    [ObservableProperty]
    private string _profileName = string.Empty;
    
    [ObservableProperty]
    private string _selectedAvatar = "default";

    [ObservableProperty]
    private bool _isChild;

    // Validations
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _urlError;

    [ObservableProperty]
    private string? _profileNameError;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isAnalyzingConnection;

    [ObservableProperty]
    private string _playlistPreviewSummary = string.Empty;

    [ObservableProperty]
    private Profile? _editingProfile;

    public event EventHandler? RequestClose;
    public event EventHandler? RequestAvatarPicker;

    public AddProfileViewModel(
        AppDbContext context, 
        IDispatcherService dispatcherService, 
        IAvatarService avatarService, 
        IDialogService dialogService,
        ILicenseService licenseService,
        IM3UParser m3uParser,
        IXtreamCodesService xtreamCodesService,
        IStalkerPortalService stalkerPortalService,
        IContentDownloadService contentDownloadService)
    {
        _context = context;
        _dispatcherService = dispatcherService;
        _avatarService = avatarService;
        _dialogService = dialogService;
        _licenseService = licenseService;
        _m3uParser = m3uParser;
        _xtreamCodesService = xtreamCodesService;
        _stalkerPortalService = stalkerPortalService;
        _contentDownloadService = contentDownloadService;

        // Initialize with default avatar
        var avatars = _avatarService.GetAvatarsByCategory().Values.FirstOrDefault();
        if (avatars != null && avatars.Any()) SelectedAvatar = avatars.First();
    }

    public void InitializeForEdit(Profile profile)
    {
        EditingProfile = profile;
        ProfileName = profile.Name;
        SelectedAvatar = profile.Avatar ?? "default";
        IsChild = profile.IsChild;
        
        // Set account
        InitializeEdit(profile);
    }

    private void InitializeEdit(Profile profile)
    {
        if (profile.ProviderAccount != null)
        {
            _isUpdatingUrl = true;

            // Set account details
            Url = profile.ProviderAccount.Url;
            Username = profile.ProviderAccount.Username ?? string.Empty;
            Password = profile.ProviderAccount.Password ?? string.Empty;
            IsXtream = profile.ProviderAccount.Type == ProfileType.XtreamCodes;
            IsM3U = profile.ProviderAccount.Type == ProfileType.M3U;
            IsStalker = profile.ProviderAccount.Type == ProfileType.StalkerPortal;

            _isUpdatingUrl = false;

            // Eğer M3U linkiyse ve credentials varsa, parse et
            if (IsM3U && Url.Contains("get.php"))
            {
                ParseCredentialsFromUrl(Url);
            }
        }
    }


    [RelayCommand]
    private void OpenAvatarPicker()
    {
        RequestAvatarPicker?.Invoke(this, EventArgs.Empty);
    }

    public void SetAvatar(string avatar)
    {
        SelectedAvatar = avatar;
    }

    [RelayCommand]
    private async Task DeleteProfile(Profile? profile)
    {
        if (profile == null || EditingProfile == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync("Profil Sil",
            $"'{profile.Name}' profilini silmek istediginize emin misiniz?");
        if (!confirmed) return;

        try
        {
            IsSaving = true;
            StatusMessage = "Profil siliniyor...";

            var profileId = EditingProfile.Id;
            var providerAccountId = await _context.Profiles
                .Where(p => p.Id == profileId)
                .Select(p => p.ProviderAccountId)
                .FirstOrDefaultAsync();

            if (providerAccountId == 0) return;

            var hasOtherProfiles = await _context.Profiles
                .AnyAsync(p => p.ProviderAccountId == providerAccountId && p.Id != profileId);

            await _contentDownloadService.DeleteProfileDownloadsAsync(profileId);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            await _context.WatchHistories
                .Where(h => h.ProfileId == profileId)
                .ExecuteDeleteAsync();

            await _context.SeriesEpisodeProgresses
                .Where(p => p.ProfileId == profileId)
                .ExecuteDeleteAsync();

            await _context.Playlists
                .Where(p => p.ProfileId == profileId)
                .ExecuteDeleteAsync();

            await _context.Profiles
                .Where(p => p.Id == profileId)
                .ExecuteDeleteAsync();

            if (!hasOtherProfiles)
            {
                await _context.ProviderAccounts
                    .Where(a => a.Id == providerAccountId)
                    .ExecuteDeleteAsync();
            }

            await transaction.CommitAsync();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Hata", "Profil silinirken bir hata olustu.", ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool ValidateUrl()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            UrlError = "URL gereklidir";
            return false;
        }

        if (!Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Url = "http://" + Url.Trim();
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
        {
            UrlError = "Gecersiz URL formati";
            return false;
        }

        if (IsM3U)
        {
            var lower = Url.ToLowerInvariant();
            if (!lower.Contains(".m3u") && !lower.Contains(".m3u8") && !lower.Contains("get.php"))
            {
                UrlError = "M3U URL'i .m3u, .m3u8 veya get.php icermelidir";
                return false;
            }
        }

        if (IsXtream)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                UrlError = "Xtream icin kullanici adi ve sifre gereklidir";
                return false;
            }
        }

        if (IsStalker)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                UrlError = "Stalker Portal icin MAC adresi gereklidir";
                return false;
            }

            if (!StalkerMacRegex.IsMatch(Username.Trim()))
            {
                UrlError = "MAC adresi gecersiz. Ornek: 00:1A:79:AA:BB:CC";
                return false;
            }
        }

        UrlError = null;
        return true;
    }
    [RelayCommand]
    private async Task AnalyzeConnectionAsync()
    {
        if (!ValidateUrl())
        {
            return;
        }

        IsAnalyzingConnection = true;
        HasError = false;
        StatusMessage = "Baglanti analiz ediliyor...";
        PlaylistPreviewSummary = string.Empty;

        try
        {
            var preview = await BuildImportPreviewAsync();
            HasError = !preview.IsValid;
            PlaylistPreviewSummary = preview.ToSummaryText();
            StatusMessage = preview.IsValid ? "Baglanti analizi tamamlandi" : "Baglanti analizi basarisiz";
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("Analiz hatasi", ex);
            PlaylistPreviewSummary = string.Empty;
        }
        finally
        {
            IsAnalyzingConnection = false;
        }
    }

    private async Task<PlaylistImportPreview> BuildImportPreviewAsync()
    {
        try
        {
            var excludedProviderAccountId = EditingProfile != null ? EditingProfile.ProviderAccountId : 0;
            var selectedType = IsStalker
                ? ProfileType.StalkerPortal
                : IsXtream
                    ? ProfileType.XtreamCodes
                    : ProfileType.M3U;

            var existingAccountDuplicate = await _context.ProviderAccounts
                .AnyAsync(a =>
                    a.Id != excludedProviderAccountId &&
                    a.Type == selectedType &&
                    a.Url == Url &&
                    (a.Username ?? string.Empty) == Username &&
                    (a.Password ?? string.Empty) == Password);

            IReadOnlyCollection<Channel> channels;
            if (IsStalker)
            {
                channels = await _stalkerPortalService.GetChannelsAsync(
                    Url,
                    Username,
                    includeVod: true);

                return PlaylistImportPreview.FromChannels(channels, "Stalker Portal", existingAccountDuplicate);
            }

            if (IsXtream)
            {
                channels = await _xtreamCodesService.GetChannelsAsync(
                    Url,
                    Username,
                    Password,
                    includeSeriesEpisodes: false);

                return PlaylistImportPreview.FromChannels(channels, "Xtream", existingAccountDuplicate);
            }

            channels = await _m3uParser.ParseFromUrlAsync(Url);
            return PlaylistImportPreview.FromChannels(channels, "M3U", existingAccountDuplicate);
        }
        catch (Exception ex)
        {
            var sourceType = IsStalker ? "Stalker Portal" : IsXtream ? "Xtream" : "M3U";
            return PlaylistImportPreview.Invalid(sourceType, UserFriendlyErrorMessage.FromException(ex));
        }
    }
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Clear previous errors
        ProfileNameError = null;
        UrlError = null;
        HasError = false;
        StatusMessage = string.Empty;

        // If user did not type a name, generate one from URL host.
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                ProfileName = uri.Host;
            }
            else
            {
                ProfileNameError = "Profil adi gereklidir";
                return;
            }
        }

        // Validate URL
        if (!ValidateUrl())
        {
            return;
        }

        try
        {
            // Show saving indicator
            StatusMessage = "Kaydediliyor...";
            IsSaving = true;

            using var transaction = await _context.Database.BeginTransactionAsync();
            ProviderAccount account;

            if (EditingProfile?.ProviderAccount != null)
            {
                 var selectedAccount = EditingProfile.ProviderAccount;
                 
                 // Detach existing if needed
                 var existing = _context.ProviderAccounts.Local
                     .FirstOrDefault(a => a.Id == selectedAccount.Id);
                 
                 if (existing != null && existing != selectedAccount)
                 {
                     _context.Entry(existing).State = EntityState.Detached;
                 }
                 
                 // Enable tracking/Update
                 selectedAccount.Url = Url;
                 selectedAccount.Username = Username;
                 selectedAccount.Password = Password;
                 selectedAccount.Type = IsStalker
                     ? ProfileType.StalkerPortal
                     : IsXtream
                        ? ProfileType.XtreamCodes
                        : ProfileType.M3U;
                 selectedAccount.ExpirationDate = null; // Reset date on credential change
                 
                 _context.Entry(selectedAccount).State = EntityState.Modified;
                 account = selectedAccount;
            }
            else
            {
                account = new ProviderAccount
                {
                    Name = ProfileName + " Hesabı",
                    Type = IsStalker
                        ? ProfileType.StalkerPortal
                        : IsXtream
                            ? ProfileType.XtreamCodes
                            : ProfileType.M3U,
                    Url = Url,
                    Username = Username,
                    Password = Password
                };
                _context.ProviderAccounts.Add(account);
            }

            if (EditingProfile != null)
            {
                // Update existing profile
                // Keep cached playlists. Channel list should refresh only when user explicitly requests it.

                // Check if profile is tracked
                var trackedProfile = _context.Profiles.Local.FirstOrDefault(p => p.Id == EditingProfile.Id);
                if (trackedProfile != null && trackedProfile != EditingProfile)
                {
                     _context.Entry(trackedProfile).State = EntityState.Detached;
                }

                EditingProfile.Name = ProfileName;
                EditingProfile.Avatar = SelectedAvatar;
                EditingProfile.IsChild = IsChild;
                
                _context.Entry(EditingProfile).State = EntityState.Modified;
            }
            else
            {
                var profileCount = await _context.Profiles.CountAsync();
                if (!_licenseService.IsWithinLimit(ProfilesLimitKey, profileCount))
                {
                    await transaction.RollbackAsync();
                    await _dialogService.ShowUpsellAsync();
                    return;
                }

                // Create new profile
                var profile = new Profile
                {
                    Name = ProfileName,
                    ProviderAccount = account,
                    Avatar = SelectedAvatar,
                    IsChild = IsChild,
                    LastUsed = DateTime.Now
                };
                _context.Profiles.Add(profile);
            }


            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Success feedback
            StatusMessage = "Profil kaydedildi";
            await Task.Delay(400);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("Hata", ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}


