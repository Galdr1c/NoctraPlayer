using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Core.Models;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public partial class AddProfileViewModel : ObservableObject
{
    private const string StalkerMacPrefix = "00:1A:79:";
    [System.Text.RegularExpressions.GeneratedRegex("^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$")]
    private static partial System.Text.RegularExpressions.Regex StalkerMacRegex();
    private readonly IProfileService _profileService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IAvatarService _avatarService;
    private readonly IDialogService _dialogService;
    private readonly ILicenseService _licenseService;
    private readonly IM3UParser _m3uParser;
    private readonly IXtreamCodesService _xtreamCodesService;
    private readonly IStalkerPortalService _stalkerPortalService;
    private readonly ISecurityService _securityService;
    private readonly ILocalizationService _localizationService;
    private readonly IPlaylistFilePickerService? _playlistFilePickerService;

    // Simplified Account Details
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    private bool _isUpdatingUrl = false;
    private bool _isLocalM3uFileSource;

    public bool IsLocalM3uFileSource => _isLocalM3uFileSource;
    public bool IsRemoteProviderSource => !IsLocalM3uFileSource;
    public bool CanSwitchProviderType => IsRemoteProviderSource && !IsSaving && !IsAnalyzingConnection;
    public bool CanEditProviderUrl => IsRemoteProviderSource && !IsSaving && !IsAnalyzingConnection;
    public bool ShowConnectionAnalysis => IsRemoteProviderSource;
    public bool ShowLocalM3uFileActions => IsLocalM3uFileSource;

    private void SetLocalM3uFileSourceState(bool value)
    {
        if (_isLocalM3uFileSource == value)
        {
            NotifySourceModePropertiesChanged();
            return;
        }

        _isLocalM3uFileSource = value;
        OnPropertyChanged(nameof(IsLocalM3uFileSource));
        NotifySourceModePropertiesChanged();
    }

    private void NotifySourceModePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsRemoteProviderSource));
        OnPropertyChanged(nameof(CanSwitchProviderType));
        OnPropertyChanged(nameof(CanEditProviderUrl));
        OnPropertyChanged(nameof(ShowConnectionAnalysis));
        OnPropertyChanged(nameof(ShowLocalM3uFileActions));
    }

    partial void OnUrlChanged(string value)
    {
        PlaylistPreviewSummary = string.Empty;

        if (_isUpdatingUrl || string.IsNullOrEmpty(value)) return;
        SetLocalM3uFileSourceState(false);
        
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

    public void SetM3uFileSource(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _isUpdatingUrl = true;
        IsM3U = true;
        IsXtream = false;
        IsStalker = false;
        Url = filePath;
        Username = string.Empty;
        Password = string.Empty;
        _isUpdatingUrl = false;

        PlaylistPreviewSummary = string.Empty;
        ClearAnalysisResults();
        SetLocalM3uFileSourceState(true);
        ValidateRealtimeInputs();
    }

    [RelayCommand]
    private async Task PickM3uFileAsync()
    {
        if (_playlistFilePickerService is null)
        {
            return;
        }

        var copyProgress = new Progress<FileCopyProgress>(OnFileCopyProgressChanged);
        try
        {
            var filePath = await _playlistFilePickerService.PickM3uFileAsync(copyProgress);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                SetM3uFileSource(filePath);
            }
        }
        finally
        {
            StatusMessage = string.Empty;
        }
    }

    private void OnFileCopyProgressChanged(FileCopyProgress progress)
    {
        if (progress.TotalBytes is > 0)
        {
            var copied = FormatBytes(progress.BytesCopied);
            var total = FormatBytes(progress.TotalBytes.Value);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AddProfile.Status.CopyingFileFormat"),
                copied, total);
        }
        else
        {
            StatusMessage = _localizationService.GetString("AddProfile.Status.CopyingFile");
        }
    }

    private static string FormatBytes(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;

        return bytes switch
        {
            >= gb => $"{bytes / (double)gb:F1} GB",
            >= mb => $"{bytes / (double)mb:F1} MB",
            >= kb => $"{bytes / (double)kb:F1} KB",
            _ => $"{bytes} B"
        };
    }

    [RelayCommand]
    private async Task ValidateLocalM3uFileAsync()
    {
        if (!IsLocalM3uFileSource || string.IsNullOrWhiteSpace(Url))
        {
            StatusMessage = _localizationService.GetString("Profiles.Account.FileNotSelected");
            HasError = true;
            return;
        }

        await AnalyzeLocalM3uFileAsync(Url.Trim());
    }

    [RelayCommand]
    private async Task ChangeLocalM3uSourceTypeAsync()
    {
        if (!IsLocalM3uFileSource)
        {
            return;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            _localizationService.GetString("Profiles.Account.ChangeSourceType.Title"),
            _localizationService.GetString("Profiles.Account.ChangeSourceType.Message"));

        if (!confirmed)
        {
            return;
        }

        _isUpdatingUrl = true;
        Url = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        IsM3U = true;
        IsXtream = false;
        IsStalker = false;
        _isUpdatingUrl = false;

        PlaylistPreviewSummary = string.Empty;
        ClearAnalysisResults();
        SetLocalM3uFileSourceState(false);
        ValidateRealtimeInputs();
    }

    private void ParseCredentialsFromUrl(string url)
    {
        try
        {
            if (!url.Contains("?")) return;

            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            var username = query["username"];
            if (!string.IsNullOrWhiteSpace(username))
            {
                _isUpdatingUrl = true;
                Username = username;
                _isUpdatingUrl = false;
            }

            var password = query["password"];
            if (!string.IsNullOrWhiteSpace(password))
            {
                _isUpdatingUrl = true;
                Password = password;
                _isUpdatingUrl = false;
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

            HasError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("AddProfile.Error.UrlParse"), ex);
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

            HasError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("AddProfile.Error.UrlCreate"), ex);
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
        ClearAnalysisResults();

        if (_isUpdatingUrl) return;
        if (value && IsLocalM3uFileSource)
        {
            _isUpdatingUrl = true;
            IsXtream = false;
            IsM3U = true;
            IsStalker = false;
            _isUpdatingUrl = false;
            NotifySourceModePropertiesChanged();
            return;
        }

        if (value)
        {
            // Detect switch from Stalker (MAC in Username)
            if (Username.StartsWith(StalkerMacPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Username = string.Empty;
                Password = string.Empty;

                // Try to restore cached credentials if available
                if (!string.IsNullOrWhiteSpace(_cachedUsername))
                {
                    _isUpdatingUrl = true;
                    if (!string.IsNullOrWhiteSpace(_cachedUrl)) Url = _cachedUrl;
                    Username = _cachedUsername;
                    Password = _cachedPassword ?? string.Empty;
                    _isUpdatingUrl = false;
                }
            }

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
        ClearAnalysisResults();

        if (_isUpdatingUrl) return;
        if (!value && IsLocalM3uFileSource)
        {
            _isUpdatingUrl = true;
            IsM3U = true;
            IsXtream = false;
            IsStalker = false;
            _isUpdatingUrl = false;
            NotifySourceModePropertiesChanged();
            return;
        }

        if (value)
        {
            // Detect switch from Stalker (MAC in Username)
            if (Username.StartsWith(StalkerMacPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Username = string.Empty;
                Password = string.Empty;

                // Try to restore cached credentials if available
                if (!string.IsNullOrWhiteSpace(_cachedUsername))
                {
                    _isUpdatingUrl = true;
                    if (!string.IsNullOrWhiteSpace(_cachedUrl)) Url = _cachedUrl;
                    Username = _cachedUsername;
                    Password = _cachedPassword ?? string.Empty;
                    _isUpdatingUrl = false;
                }
            }

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

    // Cache for switching back from Stalker
    private string? _cachedUrl;
    private string? _cachedUsername;
    private string? _cachedPassword;

    private void ClearAnalysisResults()
    {
        ConnectionHealth = ConnectionHealth.Unknown;
        DetailedStatus = string.Empty;
        StatusMessage = string.Empty;
        HasError = false;
        // PlaylistPreviewSummary is already cleared in individual setters
    }

    partial void OnIsStalkerChanged(bool value)
    {
        PlaylistPreviewSummary = string.Empty;
        ClearAnalysisResults();

        if (_isUpdatingUrl) return;
        if (value && IsLocalM3uFileSource)
        {
            _isUpdatingUrl = true;
            IsStalker = false;
            IsM3U = true;
            IsXtream = false;
            _isUpdatingUrl = false;
            NotifySourceModePropertiesChanged();
            return;
        }

        if (value)
        {
            // Cache current Xtream/M3U credentials before switching to Stalker
            if (!Username.StartsWith(StalkerMacPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _cachedUrl = Url;
                _cachedUsername = Username;
                _cachedPassword = Password;
            }

            IsXtream = false;
            IsM3U = false;

            // Clean URL: Keep only Scheme + Host + Port
            if (!string.IsNullOrWhiteSpace(Url))
            {
                try 
                {
                    var uri = new Uri(Url);
                    var baseUrl = $"{uri.Scheme}://{uri.Host}";
                    if (!uri.IsDefaultPort)
                    {
                        baseUrl += $":{uri.Port}";
                    }
                    _isUpdatingUrl = true;
                    Url = baseUrl;
                    _isUpdatingUrl = false;
                } 
                catch 
                { 
                    // Ignore invalid URLs, let validation handle them
                }
            }

            // Reset Credentials
            Password = string.Empty;

            _isUpdatingUrl = true;
            Username = StalkerMacPrefix;
            _isUpdatingUrl = false;
            
            UpdateStalkerMacValidation(Username);
            ValidateRealtimeInputs();
            return;
        }

        if (!string.Equals(UrlError, _localizationService.GetString("AddProfile.Error.MacInvalid"), StringComparison.Ordinal) &&
            !string.Equals(UrlError, _localizationService.GetString("AddProfile.Error.MacRequired"), StringComparison.Ordinal))
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
            UrlError = _localizationService.GetString("AddProfile.Error.UrlRequired");
            return;
        }

        if (IsM3U && IsLocalM3uFileSource)
        {
            UrlError = File.Exists(Url)
                ? null
                : _localizationService.GetString("AddProfile.Error.M3uUrlRequirement");
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
            UrlError = _localizationService.GetString("AddProfile.Error.UrlInvalid");
            return;
        }

        if (IsM3U)
        {
            var lower = normalizedUrl.ToLowerInvariant();
            if (!lower.Contains(".m3u") && !lower.Contains(".m3u8") && !lower.Contains("get.php"))
            {
                UrlError = _localizationService.GetString("AddProfile.Error.M3uUrlRequirement");
                return;
            }
        }

        if (IsXtream && (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)))
        {
            UrlError = _localizationService.GetString("AddProfile.Error.XtreamCredentialsRequired");
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
            UrlError = _localizationService.GetString("AddProfile.Error.MacRequired");
            return;
        }

        UrlError = StalkerMacRegex().IsMatch(currentUsername.Trim())
            ? null
            : _localizationService.GetString("AddProfile.Error.MacInvalid");
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

    [ObservableProperty]
    private bool _canEditIsChild = true;

    [ObservableProperty]
    private bool _isChildVisible = true;

    // PIN Management
    [ObservableProperty]
    private bool _hasPin;

    [ObservableProperty]
    private string _pinCode = string.Empty;

    [ObservableProperty]
    private string _pinConfirm = string.Empty;

    [ObservableProperty]
    private string? _pinError;

    /// <summary>PIN creation is a premium feature.</summary>
    public bool IsPinAvailable => _licenseService.IsPremium;

    /// <summary>True when editing a profile that already has a PIN stored.</summary>
    public bool HasExistingPin => !string.IsNullOrEmpty(EditingProfile?.PinHash);

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

    partial void OnIsSavingChanged(bool value) => NotifySourceModePropertiesChanged();

    partial void OnIsAnalyzingConnectionChanged(bool value) => NotifySourceModePropertiesChanged();

    [ObservableProperty]
    private string _playlistPreviewSummary = string.Empty;

    [ObservableProperty]
    private Profile? _editingProfile;

    [ObservableProperty]
    private ConnectionHealth _connectionHealth = ConnectionHealth.Unknown;

    [ObservableProperty]
    private string _detailedStatus = string.Empty;

    public event EventHandler? RequestClose;
    public event EventHandler? RequestAvatarPicker;

    public AddProfileViewModel(
        IProfileService profileService,
        IDispatcherService dispatcherService, 
        IAvatarService avatarService, 
        IDialogService dialogService,
        ILicenseService licenseService,
        IM3UParser m3uParser,
        IXtreamCodesService xtreamCodesService,
        IStalkerPortalService stalkerPortalService,
        ISecurityService securityService,
        ILocalizationService localizationService,
        IPlaylistFilePickerService? playlistFilePickerService = null)
    {
        _profileService = profileService;
        _dispatcherService = dispatcherService;
        _avatarService = avatarService;
        _dialogService = dialogService;
        _licenseService = licenseService;
        _m3uParser = m3uParser;
        _xtreamCodesService = xtreamCodesService;
        _stalkerPortalService = stalkerPortalService;
        _securityService = securityService;
        _localizationService = localizationService;
        _playlistFilePickerService = playlistFilePickerService;

        // Initialize with default avatar
        var avatars = _avatarService.GetAvatarsByCategory().Values.FirstOrDefault();
        if (avatars != null && avatars.Any()) SelectedAvatar = avatars.First();
    }

    public void InitializeForEdit(Profile profile)
    {
        EditingProfile = profile;
        OnPropertyChanged(nameof(HasExistingPin));
        ProfileName = profile.Name;
        SelectedAvatar = profile.Avatar ?? "default";
        IsChild = profile.IsChild;
        CanEditIsChild = false;
        IsChildVisible = profile.IsChild; // Only show if it's already a child profile when editing
        HasPin = !string.IsNullOrEmpty(profile.PinHash);
        PinCode = string.Empty; // Never show existing PIN
        PinConfirm = string.Empty;
        
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
            Password = _securityService.Decrypt(profile.ProviderAccount.Password) ?? string.Empty;
            IsXtream = profile.ProviderAccount.Type == ProfileType.XtreamCodes;
            IsM3U = profile.ProviderAccount.Type == ProfileType.M3U;
            IsStalker = profile.ProviderAccount.Type == ProfileType.StalkerPortal;
            _isUpdatingUrl = false;
            SetLocalM3uFileSourceState(IsM3U && !IsHttpSource(Url));

            // Eğer M3U linkiyse ve credentials varsa, parse et
            if (IsM3U && Url.Contains("get.php"))
            {
                ParseCredentialsFromUrl(Url);
            }
        }
    }

    private static bool IsHttpSource(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
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

        var confirmed = await _dialogService.ShowConfirmationAsync(
            _localizationService.GetString("AddProfile.Delete.Title"),
            string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("AddProfile.Delete.ConfirmationFormat"),
                profile.Name));
        if (!confirmed) return;

        try
        {
            IsSaving = true;
            StatusMessage = _localizationService.GetString("AddProfile.Delete.Deleting");

            await _profileService.DeleteProfileAsync(
                EditingProfile.Id,
                EditingProfile.ProviderAccountId);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(
                _localizationService.GetString("Common.Error"),
                _localizationService.GetString("AddProfile.Delete.Error"), ex);
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
            UrlError = _localizationService.GetString("AddProfile.Error.UrlRequired");
            return false;
        }

        if (IsM3U && IsLocalM3uFileSource)
        {
            if (!File.Exists(Url))
            {
                UrlError = _localizationService.GetString("AddProfile.Error.M3uUrlRequirement");
                return false;
            }

            UrlError = null;
            return true;
        }

        if (!Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Url = "http://" + Url.Trim();
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
        {
            UrlError = _localizationService.GetString("AddProfile.Error.UrlInvalid");
            return false;
        }

        if (IsM3U)
        {
            var lower = Url.ToLowerInvariant();
            if (!lower.Contains(".m3u") && !lower.Contains(".m3u8") && !lower.Contains("get.php"))
            {
                UrlError = _localizationService.GetString("AddProfile.Error.M3uUrlRequirement");
                return false;
            }
        }

        if (IsXtream)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                UrlError = _localizationService.GetString("AddProfile.Error.XtreamCredentialsRequired");
                return false;
            }
        }

        if (IsStalker)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                UrlError = _localizationService.GetString("AddProfile.Error.MacRequired");
                return false;
            }

            if (!StalkerMacRegex().IsMatch(Username.Trim()))
            {
                UrlError = _localizationService.GetString("AddProfile.Error.MacInvalid");
                return false;
            }
        }

        UrlError = null;
        return true;
    }
    [RelayCommand]
    private async Task AnalyzeConnectionAsync()
    {
        var urlToCheck = Url?.Trim();
        if (string.IsNullOrWhiteSpace(urlToCheck))
        {
            StatusMessage = _localizationService.GetString("AddProfile.Error.UrlRequired");
            return;
        }

        if (IsM3U && IsLocalM3uFileSource)
        {
            await AnalyzeLocalM3uFileAsync(urlToCheck);
            return;
        }

        if (!urlToCheck.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !urlToCheck.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            urlToCheck = "http://" + urlToCheck;
        }

        // Prepare the actual URL to check based on profile type
        if (IsXtream && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password))
        {
            // For Xtream, we check the player_api.php with credentials
            // This verifies both the server AND the username/password
            var uri = new Uri(urlToCheck);
            var baseUrl = $"{uri.Scheme}://{uri.Host}";
            if (!uri.IsDefaultPort) baseUrl += $":{uri.Port}";
            
            urlToCheck = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(Username)}&password={Uri.EscapeDataString(Password)}";
        }
        else if (IsM3U && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password))
        {
            // ↓ YENİ BLOK — get.php yerine player_api.php ile kontrol et
            // Çoğu sağlayıcı aynı sunucuda hem get.php hem player_api.php çalıştırır
            var uri = new Uri(urlToCheck);
            var baseUrl = $"{uri.Scheme}://{uri.Host}";
            if (!uri.IsDefaultPort) baseUrl += $":{uri.Port}";
            urlToCheck = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(Username)}&password={Uri.EscapeDataString(Password)}";
        }
        else if (IsM3U)
        {
            // M3U için linkin tamamını kontrol ediyoruz (HEAD/GET desteği için)
            // Eğer username/password varsa player_api fallback'i yukarıda yapıldı.
            // Yoksa doğrudan girilen URL'i kullanıyoruz.
        }
        else if (IsStalker)
        {
            // For Stalker, we try to hit the portal initialization endpoint
            // This is better than just the base URL, but we still bypass strict MAC check
            // because a full Stalker handshake is complex to simulate here.
            // We just want to know if a Stalker Portal exists at this address.
            if (!urlToCheck.EndsWith("/c/") && !urlToCheck.EndsWith("/portal.php"))
            {
                // Try to guess the portal path if just base URL is given
                // We will test the base URL first, if that fails or returns 404, we might try common paths?
                // For now, let's just stick to what the user entered, maybe appending /c/ if it looks like a root
            }
        }

        if (!Uri.TryCreate(urlToCheck, UriKind.Absolute, out _))
        {
            StatusMessage = _localizationService.GetString("AddProfile.Error.UrlInvalid");
            return;
        }

        IsAnalyzingConnection = true;
        HasError = false;
        StatusMessage = _localizationService.GetString("AddProfile.Status.Analyzing");
        PlaylistPreviewSummary = string.Empty;
        ConnectionHealth = ConnectionHealth.Unknown;
        DetailedStatus = string.Empty;

        try
        {
            // Perform health check
            var (health, statusCode, latency, error) = await PerformHealthCheckAsync(urlToCheck);

            // Special handling for Xtream Auth failure (JSON response with error or 401)
            // PerformHealthCheckAsync currently returns status code.
            // If it's 200 OK, it means "Server Reached".
            // For Xtream player_api, 200 OK usually means valid login or at least valid API.
            // If credentials are wrong, it might return 200 OK but with JSON {"user_info":{"auth":0}} 
            // Parsing that JSON is heavy, but status code 200 is a good start. 
            // A 401/403 definitely means Auth Failed.

            ConnectionHealth = health;
            DetailedStatus = FormatDetailedStatus(statusCode, latency, error);

            if (health == ConnectionHealth.Critical)
            {
                HasError = true;
                StatusMessage = _localizationService.GetString("AddProfile.Analysis.Failed");
                return;
            }

            var providerVerification = await VerifyProviderAsync(CancellationToken.None);
            if (providerVerification.Health == ConnectionHealth.Critical)
            {
                ConnectionHealth = ConnectionHealth.Critical;
                DetailedStatus = string.IsNullOrWhiteSpace(providerVerification.Error)
                    ? _localizationService.GetString("AddProfile.Analysis.Failed")
                    : providerVerification.Error;
                HasError = true;
                StatusMessage = _localizationService.GetString("AddProfile.Analysis.Failed");
                return;
            }

            if (providerVerification.Health != ConnectionHealth.Unknown)
            {
                ConnectionHealth = providerVerification.Health;
                DetailedStatus = FormatDetailedStatus(statusCode, providerVerification.Latency, null);
            }

            HasError = false;
            StatusMessage = _localizationService.GetString("AddProfile.Analysis.Completed");
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("AddProfile.Analysis.ErrorPrefix"), ex);
            ConnectionHealth = ConnectionHealth.Critical;
            DetailedStatus = _localizationService.GetString("AddProfile.Analysis.UnexpectedError");
        }
        finally
        {
            IsAnalyzingConnection = false;
        }
    }

    private async Task AnalyzeLocalM3uFileAsync(string filePath)
    {
        IsAnalyzingConnection = true;
        HasError = false;
        StatusMessage = _localizationService.GetString("Profiles.Account.FileValidation");
        PlaylistPreviewSummary = string.Empty;
        ConnectionHealth = ConnectionHealth.Unknown;
        DetailedStatus = string.Empty;

        try
        {
            var channels = await _m3uParser.ParseFromFileAsync(filePath);
            if (channels.Count == 0)
            {
                HasError = true;
                ConnectionHealth = ConnectionHealth.Critical;
                StatusMessage = _localizationService.GetString("AddProfile.Analysis.Failed");
                DetailedStatus = _localizationService.GetString("Playlist.Error.EmptyNoDelete");
                return;
            }

            ConnectionHealth = ConnectionHealth.Good;
            DetailedStatus = string.Format(_localizationService.GetString("Profiles.Account.LocalM3uValidationResult"), channels.Count);
            StatusMessage = _localizationService.GetString("AddProfile.Analysis.Completed");
        }
        catch (Exception ex)
        {
            HasError = true;
            ConnectionHealth = ConnectionHealth.Critical;
            StatusMessage = UserFriendlyErrorMessage.WithPrefix(
                _localizationService.GetString("AddProfile.Analysis.ErrorPrefix"),
                ex);
            DetailedStatus = UserFriendlyErrorMessage.FromException(ex);
        }
        finally
        {
            IsAnalyzingConnection = false;
        }
    }

    private string FormatDetailedStatus(int? statusCode, long? latency, string? error)
    {
        if (!statusCode.HasValue && !string.IsNullOrEmpty(error))
            return error;

        var statusText = statusCode.HasValue ? $"{statusCode} {GetReasonPhrase(statusCode.Value)}" : _localizationService.GetString("AddProfile.Http.Unknown");
        var latencyText = latency.HasValue ? $"{latency}ms" : "";
        
        return $"{statusText} - {latencyText}".Trim(' ', '-');
    }

    private string GetReasonPhrase(int statusCode)
    {
        return statusCode switch
        {
            200 => _localizationService.GetString("AddProfile.Http.200"),
            301 => _localizationService.GetString("AddProfile.Http.301"),
            302 => _localizationService.GetString("AddProfile.Http.302"),
            400 => _localizationService.GetString("AddProfile.Http.400"),
            401 => _localizationService.GetString("AddProfile.Http.401"),
            403 => _localizationService.GetString("AddProfile.Http.403"),
            404 => _localizationService.GetString("AddProfile.Http.404"),
            500 => _localizationService.GetString("AddProfile.Http.500"),
            502 or 503 or 504 => _localizationService.GetString("AddProfile.Http.GatewayError"),
            >= 500 => _localizationService.GetString("AddProfile.Http.ServerErrorGeneric"),
            _ => string.Format(CultureInfo.CurrentCulture,
                _localizationService.GetString("AddProfile.Http.Format"), statusCode)
        };
    }

    private async Task<(ConnectionHealth Health, long Latency, string? Error)> VerifyProviderAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (IsXtream)
            {
                var authenticated = await _xtreamCodesService.AuthenticateAsync(
                    NormalizeProviderBaseUrl(Url), Username, Password, cancellationToken);
                stopwatch.Stop();
                return authenticated
                    ? (ClassifyLatency(stopwatch.ElapsedMilliseconds), stopwatch.ElapsedMilliseconds, null)
                    : (ConnectionHealth.Critical, stopwatch.ElapsedMilliseconds, _localizationService.GetString("Xtream.Error.AuthFailed"));
            }

            if (IsStalker)
            {
                var authenticated = await _stalkerPortalService.AuthenticateAsync(
                    Url, Username, cancellationToken);
                if (!authenticated)
                {
                    stopwatch.Stop();
                    return (ConnectionHealth.Critical, stopwatch.ElapsedMilliseconds, _localizationService.GetString("Stalker.Error.HandshakeFailed"));
                }

                var categories = await _stalkerPortalService.GetCategoriesAsync(
                    Url, Username, cancellationToken);
                stopwatch.Stop();
                return categories.Count > 0
                    ? (ClassifyLatency(stopwatch.ElapsedMilliseconds), stopwatch.ElapsedMilliseconds, null)
                    : (ConnectionHealth.Critical, stopwatch.ElapsedMilliseconds, _localizationService.GetString("AddProfile.Error.NewProviderValidationFailed"));
            }

            if (IsM3U && IsLocalM3uFileSource)
            {
                var channels = await _m3uParser.ParseFromFileAsync(Url.Trim());
                stopwatch.Stop();
                return channels.Count > 0
                    ? (ClassifyLatency(stopwatch.ElapsedMilliseconds), stopwatch.ElapsedMilliseconds, null)
                    : (ConnectionHealth.Critical, stopwatch.ElapsedMilliseconds, _localizationService.GetString("Playlist.Error.EmptyNoDelete"));
            }

            if (IsM3U && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password))
            {
                var authenticated = await _xtreamCodesService.AuthenticateAsync(
                    NormalizeProviderBaseUrl(Url), Username, Password, cancellationToken);
                stopwatch.Stop();
                return authenticated
                    ? (ClassifyLatency(stopwatch.ElapsedMilliseconds), stopwatch.ElapsedMilliseconds, null)
                    : (ConnectionHealth.Critical, stopwatch.ElapsedMilliseconds, _localizationService.GetString("Xtream.Error.AuthFailed"));
            }

            stopwatch.Stop();
            return (ConnectionHealth.Unknown, stopwatch.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return (ConnectionHealth.Critical, stopwatch.ElapsedMilliseconds, UserFriendlyErrorMessage.FromException(ex));
        }
    }

    private static ConnectionHealth ClassifyLatency(long latencyMs)
    {
        return latencyMs < 500 ? ConnectionHealth.Good :
               latencyMs < 1500 ? ConnectionHealth.Weak :
               ConnectionHealth.Bad;
    }

    private static string NormalizeProviderBaseUrl(string url)
    {
        var trimmed = url?.Trim() ?? string.Empty;
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}"
            : trimmed.TrimEnd('/');
    }

    private async Task<(ConnectionHealth Health, int? StatusCode, long? Latency, string? Error)> PerformHealthCheckAsync(string url)
    {
        try
        {
            using var handler = new System.Net.Http.HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            using var client = new System.Net.Http.HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15); // 15s timeout
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Try HEAD first for lightweight check, fallback to GET
            System.Net.Http.HttpResponseMessage response;
            try
            {
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                response = await client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    response.Dispose();
                    stopwatch.Restart();
                    response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                }
            }
            catch (System.Net.Http.HttpRequestException)
            {
                // Some servers block HEAD, try GET
                stopwatch.Restart();
                response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            }
            
            stopwatch.Stop();
            var latency = stopwatch.ElapsedMilliseconds;
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return (ClassifyLatency(latency), code, latency, null);
            }
            else
            {
                var error = code switch
                {
                    401 or 403 => _localizationService.GetString("AddProfile.Error.Unauthorized"),
                    404 => _localizationService.GetString("AddProfile.Error.NotFound"),
                    405 => _localizationService.GetString("AddProfile.Error.MethodNotAllowed"),
                    >= 500 => _localizationService.GetString("AddProfile.Error.ServerError"),
                    _ => string.Format(CultureInfo.CurrentCulture,
                        _localizationService.GetString("AddProfile.Http.Format"), code)
                };

                return (ConnectionHealth.Critical, code, latency, error);
            }
        }
        catch (Exception ex)
        {
            return (ConnectionHealth.Critical, null, null, UserFriendlyErrorMessage.FromException(ex));
        }
    }

    private async Task<PlaylistImportPreview> BuildImportPreviewAsync(
        ConnectionHealth health = ConnectionHealth.Unknown, 
        long? latency = null, 
        int? statusCode = null)
    {
        try
        {
            var excludedProviderAccountId = EditingProfile != null ? EditingProfile.ProviderAccountId : 0;
            var selectedType = IsStalker
                ? ProfileType.StalkerPortal
                : IsXtream
                    ? ProfileType.XtreamCodes
                    : ProfileType.M3U;

            IReadOnlyCollection<Channel> channels;
            if (IsStalker)
            {
                channels = await _stalkerPortalService.GetChannelsAsync(
                    Url,
                    Username,
                    includeVod: true);

                return PlaylistImportPreview.FromChannels(channels, "Stalker Portal", health, latency, statusCode);
            }

            if (IsXtream)
            {
                channels = await _xtreamCodesService.GetChannelsAsync(
                    Url,
                    Username,
                    Password,
                    includeSeriesEpisodes: false);

                return PlaylistImportPreview.FromChannels(channels, "Xtream", health, latency, statusCode);
            }

            channels = IsLocalM3uFileSource
                ? await _m3uParser.ParseFromFileAsync(Url.Trim())
                : await _m3uParser.ParseFromUrlAsync(Url);
            return PlaylistImportPreview.FromChannels(channels, "M3U", health, latency, statusCode);
        }
        catch (Exception ex)
        {
            var sourceType = IsStalker ? "Stalker Portal" : IsXtream ? "Xtream" : "M3U";
            return PlaylistImportPreview.Invalid(sourceType, UserFriendlyErrorMessage.FromException(ex), statusCode);
        }
    }
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Clear previous errors
        ProfileNameError = null;
        UrlError = null;
        PinError = null;
        HasError = false;
        // Non-premium users cannot save with PIN
        if (HasPin && !_licenseService.IsPremium)
        {
            HasPin = false;
        }
        StatusMessage = string.Empty;

        // If user did not type a name, generate one from URL host.
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            if (IsM3U && IsLocalM3uFileSource && !string.IsNullOrWhiteSpace(Url))
            {
                ProfileName = Path.GetFileNameWithoutExtension(Url.Trim());
            }
            else if (Uri.TryCreate(Url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                ProfileName = uri.Host;
            }

            if (string.IsNullOrWhiteSpace(ProfileName))
            {
                ProfileNameError = _localizationService.GetString("AddProfile.Error.ProfileNameRequired");
                return;
            }
        }

        // Validate URL
        if (!ValidateUrl())
        {
            return;
        }

        // Validate PIN
        if (HasPin)
        {
            if (PinCode.Length > 0)
            {
                // Must be exactly 4 digits
                if (PinCode.Length != 4 || !PinCode.All(char.IsDigit))
                {
                    PinError = _localizationService.GetString("AddProfile.Error.PinLength");
                    return;
                }

                // Confirmation must match
                if (PinCode != PinConfirm)
                {
                    PinError = _localizationService.GetString("AddProfile.Error.PinMismatch");
                    return;
                }
            }
            else if (string.IsNullOrEmpty(EditingProfile?.PinHash))
            {
                // No existing PIN and nothing entered — require PIN entry
                PinError = _localizationService.GetString("AddProfile.Error.PinRequired");
                return;
            }
            // else: HasPin=true, PinCode boş, EditingProfile.PinHash dolu → mevcut PIN korunur
        }

        try
        {
            // Show saving indicator
            StatusMessage = _localizationService.GetString("AddProfile.Status.Saving");
            IsSaving = true;

            var newAccountType = IsStalker
                ? ProfileType.StalkerPortal
                : IsXtream
                    ? ProfileType.XtreamCodes
                    : ProfileType.M3U;

            bool credentialsChanged = false;
            var encryptedPassword = _securityService.Encrypt(Password) ?? string.Empty;

            if (EditingProfile?.ProviderAccount != null)
            {
                var originalUrl = EditingProfile.ProviderAccount.Url;
                var originalUsername = EditingProfile.ProviderAccount.Username ?? string.Empty;
                var originalPassword = _securityService.Decrypt(EditingProfile.ProviderAccount.Password) ?? string.Empty;
                var originalType = EditingProfile.ProviderAccount.Type;

                credentialsChanged = originalUrl != Url || 
                                     originalUsername != Username || 
                                     originalPassword != Password || 
                                     originalType != newAccountType;
            }

            

            if (EditingProfile != null && credentialsChanged)
            {
                StatusMessage = _localizationService.GetString("AddProfile.Status.Analyzing");

                var providerValidation = await VerifyProviderAsync(CancellationToken.None);
                if (providerValidation.Health == ConnectionHealth.Critical)
                {
                    HasError = true;
                    StatusMessage = string.IsNullOrWhiteSpace(providerValidation.Error)
                        ? _localizationService.GetString("AddProfile.Error.NewProviderValidationFailed")
                        : providerValidation.Error;
                    UrlError = StatusMessage;
                    ConnectionHealth = ConnectionHealth.Critical;
                    DetailedStatus = StatusMessage;
                    return;
                }

                if (providerValidation.Health == ConnectionHealth.Unknown)
                {
                    var preview = await BuildImportPreviewAsync();
                    if (!preview.IsValid || preview.TotalChannels <= 0)
                    {
                        HasError = true;
                        StatusMessage = string.IsNullOrWhiteSpace(preview.ErrorMessage)
                            ? _localizationService.GetString("AddProfile.Error.NewProviderValidationFailed")
                            : preview.ErrorMessage;
                        UrlError = StatusMessage;
                        ConnectionHealth = ConnectionHealth.Critical;
                        DetailedStatus = StatusMessage;
                        return;
                    }
                }
            }

            var request = new ProfileSaveRequest
            {
                ProfileName = ProfileName,
                Avatar = SelectedAvatar,
                IsChild = IsChild,
                Url = Url,
                Username = Username,
                EncryptedPassword = encryptedPassword,
                AccountType = newAccountType,
                CredentialsChanged = credentialsChanged,
                PinHash = HasPin && PinCode.Length == 4
                    ? _securityService.HashPin(PinCode)
                    : HasPin && EditingProfile?.PinHash != null
                        ? EditingProfile.PinHash  // Keep existing PIN if toggle is on but no new code entered
                        : null,                    // PIN disabled or removed
                ExistingIds = EditingProfile != null
                    ? new ExistingProfileIds(EditingProfile.Id, EditingProfile.ProviderAccountId)
                    : null
            };

            var result = await _profileService.SaveProfileAsync(request);

            if (result == null)
            {
                // License limit exceeded
                await _dialogService.ShowUpsellAsync();
                return;
            }

            // Success feedback
            StatusMessage = _localizationService.GetString("AddProfile.Status.Saved");
            await Task.Delay(400);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Common.ErrorPrefix"), ex);
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


