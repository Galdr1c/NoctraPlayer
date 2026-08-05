using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Core.Models;
using Noctra.Core.Services;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public partial class AddProfileViewModel : ObservableObject
{

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
    public bool CanEditProviderCredentials => IsRemoteProviderSource && !IsSaving && !IsAnalyzingConnection;
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
        OnPropertyChanged(nameof(CanEditProviderCredentials));
        OnPropertyChanged(nameof(ShowConnectionAnalysis));
        OnPropertyChanged(nameof(ShowLocalM3uFileActions));
    }

    partial void OnUrlChanged(string value)
    {
        ServerUrlError = null;
        UrlError = null;
        PlaylistPreviewSummary = string.Empty;
        InvalidateActiveAnalysis();

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

        // Property setters above already called InvalidateActiveAnalysis() via OnChanged handlers,
        // which increments generation and clears results. No need to call again.
        SetLocalM3uFileSourceState(true);
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
            SetStatus(string.Empty, FormStatusKind.None);
        }
    }

    private void OnFileCopyProgressChanged(FileCopyProgress progress)
    {
        if (progress.TotalBytes is > 0)
        {
            var copied = FormatBytes(progress.BytesCopied);
            var total = FormatBytes(progress.TotalBytes.Value);
            SetStatus(string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AddProfile.Status.CopyingFileFormat"),
                copied, total), FormStatusKind.Progress);
        }
        else
        {
            SetStatus(_localizationService.GetString("AddProfile.Status.CopyingFile"), FormStatusKind.Progress);
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
            SetStatus(_localizationService.GetString("Profiles.Account.FileNotSelected"), FormStatusKind.Error);
            return;
        }

        var token = PrepareAnalysisRequest();
        await AnalyzeLocalM3uFileAsync(Url.Trim(), token, _analysisGeneration);
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
        // Property setters above already called InvalidateActiveAnalysis() via OnChanged handlers,
        // which increments generation and clears results. No need to call again.
        SetLocalM3uFileSourceState(false);
        ClearValidationErrors();
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
            SetStatus(UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("AddProfile.Error.UrlParse"), ex), FormStatusKind.Error);
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
            SetStatus(UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("AddProfile.Error.UrlCreate"), ex), FormStatusKind.Error);
        }
    }

    [ObservableProperty]
    private string _username = string.Empty;

    partial void OnUsernameChanged(string value)
    {
        PlaylistPreviewSummary = string.Empty;
        UsernameError = null;
        MacAddressError = null;
        InvalidateActiveAnalysis();

        if (_isUpdatingUrl) return;

        // MAC formatlaması artık ProfileSetupView code-behind'da
        // caret-korumalı şekilde yapılıyor (UsernameTextBox_TextChanged).

        if (IsM3U && !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Password))
        {
            ConvertXtreamToM3UUrl();
        }
    }

    [ObservableProperty]
    private string _password = string.Empty;

    partial void OnPasswordChanged(string value)
    {
        PlaylistPreviewSummary = string.Empty;
        PasswordError = null;
        InvalidateActiveAnalysis();

        if (_isUpdatingUrl) return;
        
        if (IsM3U && !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Username))
        {
            ConvertXtreamToM3UUrl();
        }
    }
    
    [ObservableProperty]
    private bool _isXtream = true;

    partial void OnIsXtreamChanged(bool value)
    {
        PlaylistPreviewSummary = string.Empty;
        InvalidateActiveAnalysis();
        IsPasswordRevealed = false;

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
            if (Username.StartsWith(StalkerMacFormatter.Prefix, StringComparison.OrdinalIgnoreCase))
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

        ClearValidationErrors();
    }

    [ObservableProperty]
    private bool _isM3U = false;

    partial void OnIsM3UChanged(bool value)
    {
        PlaylistPreviewSummary = string.Empty;
        InvalidateActiveAnalysis();
        IsPasswordRevealed = false;

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
            if (Username.StartsWith(StalkerMacFormatter.Prefix, StringComparison.OrdinalIgnoreCase))
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

        ClearValidationErrors();
    }

    [ObservableProperty]
    private bool _isStalker = false;

    // Cache for switching back from Stalker
    private string? _cachedUrl;
    private string? _cachedUsername;
    private string? _cachedPassword;

    private CancellationTokenSource? _analysisCts;
    private int _analysisGeneration = 0;

    private void CancelActiveAnalysis()
    {
        try
        {
            _analysisCts?.Cancel();
            _analysisCts?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to cancel analysis: {ex.Message}");
        }
        finally
        {
            _analysisCts = null;
        }
    }

    /// <summary>
    /// Input değiştiğinde çağrılır: generation artırır, aktif analizi iptal eder
    /// ve sonuçları temizler. Bu, eski analiz sonuçlarının yeni forma uygulanmasını
    /// engeller (race condition önlemi).
    /// </summary>
    private void InvalidateActiveAnalysis()
    {
        Interlocked.Increment(ref _analysisGeneration);
        CancelActiveAnalysis();
        ClearAnalysisResults();
    }

    private CancellationToken PrepareAnalysisRequest()
    {
        CancelActiveAnalysis();
        Interlocked.Increment(ref _analysisGeneration);
        _analysisCts = new CancellationTokenSource();
        return _analysisCts.Token;
    }

    private void ClearAnalysisResults()
    {
        ConnectionHealth = ConnectionHealth.Unknown;
        DetailedStatus = string.Empty;
        StatusMessage = string.Empty;
        StatusKind = FormStatusKind.None;
        HasError = false;
        // PlaylistPreviewSummary is already cleared in individual setters
    }

    partial void OnIsStalkerChanged(bool value)
    {
        PlaylistPreviewSummary = string.Empty;
        InvalidateActiveAnalysis();
        IsPasswordRevealed = false;

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
            if (!Username.StartsWith(StalkerMacFormatter.Prefix, StringComparison.OrdinalIgnoreCase))
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
            Username = StalkerMacFormatter.Prefix;
            _isUpdatingUrl = false;
            
            ClearValidationErrors();
            return;
        }

        if (!string.Equals(UrlError, _localizationService.GetString("AddProfile.Error.MacInvalid"), StringComparison.Ordinal) &&
            !string.Equals(UrlError, _localizationService.GetString("AddProfile.Error.MacRequired"), StringComparison.Ordinal))
        {
            return;
        }

        ClearValidationErrors();
    }

    public void TouchField(string fieldName)
    {
        switch (fieldName)
        {
            case "ProfileName":
                IsProfileNameTouched = true;
                ValidateProfileName();
                break;
            case "Url":
                IsUrlTouched = true;
                ValidateUrlField();
                break;
            case "Username":
                IsUsernameTouched = true;
                ValidateUsernameField();
                break;
            case "Password":
                IsPasswordTouched = true;
                ValidatePasswordField();
                break;
            case "PinCode":
                IsPinCodeTouched = true;
                ValidatePinCodeField();
                break;
            case "PinConfirm":
                IsPinConfirmTouched = true;
                ValidatePinConfirmField();
                break;
        }
    }

    private void ClearValidationErrors()
    {
        ProfileNameError = null;
        ServerUrlError = null;
        UrlError = null;
        UsernameError = null;
        MacAddressError = null;
        PasswordError = null;
        PinError = null;
        PinConfirmationError = null;

        IsProfileNameTouched = false;
        IsUrlTouched = false;
        IsUsernameTouched = false;
        IsPasswordTouched = false;
        IsPinCodeTouched = false;
        IsPinConfirmTouched = false;
        IsSubmitted = false;
    }

    private void ValidateProfileName()
    {
        if (!IsProfileNameTouched && !IsSubmitted)
        {
            ProfileNameError = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            ProfileNameError = _localizationService.GetString("AddProfile.Error.ProfileNameRequired");
        }
        else
        {
            ProfileNameError = null;
        }
    }

    private void ValidateUrlField()
    {
        if (!IsUrlTouched && !IsSubmitted)
        {
            ServerUrlError = null;
            UrlError = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(Url))
        {
            ServerUrlError = _localizationService.GetString("AddProfile.Error.UrlRequired");
            UrlError = ServerUrlError;
            return;
        }

        if (IsM3U && IsLocalM3uFileSource)
        {
            if (!File.Exists(Url))
            {
                ServerUrlError = _localizationService.GetString("AddProfile.Error.M3uUrlRequirement");
                UrlError = ServerUrlError;
                return;
            }
            ServerUrlError = null;
            UrlError = null;
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
            ServerUrlError = _localizationService.GetString("AddProfile.Error.UrlInvalid");
            UrlError = ServerUrlError;
            return;
        }

        if (IsM3U)
        {
            var lower = normalizedUrl.ToLowerInvariant();
            if (!lower.Contains(".m3u") && !lower.Contains(".m3u8") && !lower.Contains("get.php"))
            {
                ServerUrlError = _localizationService.GetString("AddProfile.Error.M3uUrlRequirement");
                UrlError = ServerUrlError;
                return;
            }
        }

        ServerUrlError = null;
        UrlError = null;
    }

    private void ValidateUsernameField()
    {
        if (!IsUsernameTouched && !IsSubmitted)
        {
            UsernameError = null;
            MacAddressError = null;
            return;
        }

        if (IsStalker)
        {
            UsernameError = null;
            if (string.IsNullOrWhiteSpace(Username))
            {
                MacAddressError = _localizationService.GetString("AddProfile.Error.MacRequired");
            }
            else if (!StalkerMacFormatter.IsValid(Username))
            {
                MacAddressError = _localizationService.GetString("AddProfile.Error.MacInvalid");
            }
            else
            {
                MacAddressError = null;
            }
        }
        else if (IsXtream)
        {
            MacAddressError = null;
            if (string.IsNullOrWhiteSpace(Username))
            {
                UsernameError = _localizationService.GetString("AddProfile.Error.XtreamCredentialsRequired");
            }
            else
            {
                UsernameError = null;
            }
        }
        else
        {
            UsernameError = null;
            MacAddressError = null;
        }
    }

    private void ValidatePasswordField()
    {
        if (!IsPasswordTouched && !IsSubmitted)
        {
            PasswordError = null;
            return;
        }

        if (IsXtream)
        {
            if (string.IsNullOrWhiteSpace(Password))
            {
                PasswordError = _localizationService.GetString("AddProfile.Error.XtreamCredentialsRequired");
            }
            else
            {
                PasswordError = null;
            }
        }
        else
        {
            PasswordError = null;
        }
    }

    private void ValidatePinCodeField()
    {
        if (!IsPinCodeTouched && !IsSubmitted)
        {
            PinError = null;
            return;
        }

        if (HasPin)
        {
            if (string.IsNullOrEmpty(PinCode))
            {
                if (string.IsNullOrEmpty(EditingProfile?.PinHash))
                {
                    PinError = _localizationService.GetString("AddProfile.Error.PinRequired");
                }
                else
                {
                    PinError = null;
                }
            }
            else if (PinCode.Length != 4 || !PinCode.All(char.IsDigit))
            {
                PinError = _localizationService.GetString("AddProfile.Error.PinLength");
            }
            else
            {
                PinError = null;
            }
        }
        else
        {
            PinError = null;
        }
    }

    private void ValidatePinConfirmField()
    {
        if (!IsPinConfirmTouched && !IsSubmitted)
        {
            PinConfirmationError = null;
            return;
        }

        if (HasPin && !string.IsNullOrEmpty(PinCode) && PinCode.Length == 4)
        {
            if (PinCode != PinConfirm)
            {
                PinConfirmationError = _localizationService.GetString("AddProfile.Error.PinMismatch");
            }
            else
            {
                PinConfirmationError = null;
            }
        }
        else
        {
            PinConfirmationError = null;
        }
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

    /// <summary>
    /// Yapıştırılan metni PIN'e uygun hale getirir: rakam olmayan karakterler
    /// (boşluk, ayraç, harf vb.) atılır, Unicode rakamlar (tam genişlik,
    /// Arap-Hint vb.) ASCII rakama çevrilir. Böylece kaydedilen PIN, sayısal
    /// tuş takımının ürettiği ASCII rakamlarla her zaman yeniden girilebilir.
    /// </summary>
    private static string NormalizePinInput(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Sık görülen ASCII rakam girişinde ayırma/yeniden kurma yapma —
        // her tuş vuruşunda gereksiz ayırma olmasın, değer aynen dönsün.
        if (value.All(c => c is >= '0' and <= '9'))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch))
            {
                builder.Append((char)('0' + (int)char.GetNumericValue(ch)));
            }
        }

        return builder.ToString();
    }

    partial void OnPinCodeChanged(string value)
    {
        var normalized = NormalizePinInput(value);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            PinCode = normalized;
        }
    }

    partial void OnPinConfirmChanged(string value)
    {
        var normalized = NormalizePinInput(value);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            PinConfirm = normalized;
        }
    }

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
    private FormStatusKind _statusKind = FormStatusKind.None;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Sets both StatusMessage and StatusKind atomically.
    /// Automatically sets HasError = true when kind is Error.
    /// </summary>
    private void SetStatus(string message, FormStatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
        HasError = kind == FormStatusKind.Error;
    }

    [ObservableProperty]
    private string? _urlError;

    [ObservableProperty]
    private string? _profileNameError;

    [ObservableProperty]
    private string? _serverUrlError;

    [ObservableProperty]
    private string? _usernameError;

    [ObservableProperty]
    private string? _passwordError;

    [ObservableProperty]
    private string? _macAddressError;

    [ObservableProperty]
    private string? _pinConfirmationError;

    [ObservableProperty]
    private bool _isProfileNameTouched;

    [ObservableProperty]
    private bool _isUrlTouched;

    [ObservableProperty]
    private bool _isUsernameTouched;

    [ObservableProperty]
    private bool _isPasswordTouched;

    [ObservableProperty]
    private bool _isPinCodeTouched;

    [ObservableProperty]
    private bool _isPinConfirmTouched;

    [ObservableProperty]
    private bool _isSubmitted;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isPasswordRevealed;

    partial void OnIsPasswordRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordToggleIcon));
        OnPropertyChanged(nameof(PasswordToggleA11yName));
    }

    /// <summary>Eye icon kind that reflects current reveal state.</summary>
    public string PasswordToggleIcon => IsPasswordRevealed ? "EyeClosed" : "EyeOutline";

    /// <summary>Accessible name for the password toggle button.</summary>
    public string PasswordToggleA11yName => IsPasswordRevealed
        ? _localizationService.GetString("Profiles.Account.HidePassword")
        : _localizationService.GetString("Profiles.Account.ShowPassword");

    [RelayCommand]
    private void TogglePasswordReveal()
    {
        IsPasswordRevealed = !IsPasswordRevealed;
    }

    [RelayCommand]
    private async Task RequestPremiumUpgradeAsync()
    {
        await _dialogService.ShowUpsellAsync();
    }

    [ObservableProperty]
    private bool _isAnalyzingConnection;

    partial void OnIsSavingChanged(bool value) => NotifySourceModePropertiesChanged();

    partial void OnIsAnalyzingConnectionChanged(bool value) => NotifySourceModePropertiesChanged();

    [ObservableProperty]
    private string _playlistPreviewSummary = string.Empty;

    [ObservableProperty]
    private Profile? _editingProfile;

    /// <summary>
    /// Düzenleme ekranına girerken alınan merkezî erişim yetkisi (Edit).
    /// Save ve Delete işlemleri bu grant'i servis katmanına taşır; PIN kapısı
    /// View code-behind'e bağımlı olmadan zorunlu kalır.
    /// </summary>
    public ProfileAccessGrant? AccessGrant { get; set; }

    [ObservableProperty]
    private ConnectionHealth _connectionHealth = ConnectionHealth.Unknown;

    [ObservableProperty]
    private string _detailedStatus = string.Empty;

    public event EventHandler? RequestClose;
    public event EventHandler? RequestAvatarPicker;
    public event EventHandler<string>? ValidationErrorOccurred;

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

        // Initialize with a random avatar instead of always the first one
        var avatars = _avatarService.GetAvatarsByCategory().Values.FirstOrDefault();
        if (avatars != null && avatars.Count > 0)
        {
            SelectedAvatar = avatars[Random.Shared.Next(avatars.Count)];
        }
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
            SetStatus(_localizationService.GetString("AddProfile.Delete.Deleting"), FormStatusKind.Progress);

            await _profileService.DeleteProfileAsync(
                EditingProfile.Id,
                EditingProfile.ProviderAccountId,
                AccessGrant ?? throw new ProfileAccessDeniedException());

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
        IsSubmitted = true;
        ValidateUrlField();
        ValidateUsernameField();
        ValidatePasswordField();

        return ServerUrlError == null && UsernameError == null && PasswordError == null && MacAddressError == null;
    }


    [RelayCommand]
    private async Task AnalyzeConnectionAsync()
    {
        var urlToCheck = Url?.Trim();
        if (string.IsNullOrWhiteSpace(urlToCheck))
        {
            SetStatus(_localizationService.GetString("AddProfile.Error.UrlRequired"), FormStatusKind.Error);
            return;
        }

        var token = PrepareAnalysisRequest();
        var generation = _analysisGeneration;

        if (IsM3U && IsLocalM3uFileSource)
        {
            await AnalyzeLocalM3uFileAsync(urlToCheck, token, generation);
            return;
        }

        if (!urlToCheck.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !urlToCheck.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            urlToCheck = "http://" + urlToCheck;
        }

        if (IsM3U)
        {
            if (!Uri.TryCreate(urlToCheck, UriKind.Absolute, out _))
            {
                if (generation == _analysisGeneration)
                {
                    SetStatus(_localizationService.GetString("AddProfile.Error.UrlInvalid"), FormStatusKind.Error);
                }
                return;
            }

            await AnalyzeRemoteM3uAsync(urlToCheck, token, generation);
            return;
        }

        // Prepare the actual URL to check based on profile type
        if (IsXtream && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password))
        {
            try
            {
                var uri = new Uri(urlToCheck);
                var baseUrl = $"{uri.Scheme}://{uri.Host}";
                if (!uri.IsDefaultPort) baseUrl += $":{uri.Port}";
                
                urlToCheck = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(Username)}&password={Uri.EscapeDataString(Password)}";
            }
            catch
            {
                if (generation == _analysisGeneration)
                {
                    SetStatus(_localizationService.GetString("AddProfile.Error.UrlInvalid"), FormStatusKind.Error);
                }
                return;
            }
        }
        else if (IsStalker)
        {
            if (!urlToCheck.EndsWith("/c/") && !urlToCheck.EndsWith("/portal.php"))
            {
                // Stalker paths
            }
        }

        if (!Uri.TryCreate(urlToCheck, UriKind.Absolute, out _))
        {
            if (generation == _analysisGeneration)
            {
                SetStatus(_localizationService.GetString("AddProfile.Error.UrlInvalid"), FormStatusKind.Error);
            }
            return;
        }

        if (IsM3U)
        {
            await AnalyzeRemoteM3uAsync(urlToCheck, token, generation);
            return;
        }

        IsAnalyzingConnection = true;
        HasError = false;
        SetStatus(_localizationService.GetString("AddProfile.Status.Analyzing"), FormStatusKind.Progress);
        PlaylistPreviewSummary = string.Empty;
        ConnectionHealth = ConnectionHealth.Unknown;
        DetailedStatus = string.Empty;

        try
        {
            // Lightweight health check: single HEAD/GET request for all provider types.
            // Full authentication (AuthenticateAsync + GetCategoriesAsync) is only
            // performed during Save, not during test connection.
            var (health, statusCode, latency, error) = await PerformHealthCheckAsync(urlToCheck, token);

            if (generation != _analysisGeneration) return;

            ConnectionHealth = health;
            DetailedStatus = FormatDetailedStatus(statusCode, latency, error);

            if (health == ConnectionHealth.Critical)
            {
                SetStatus(_localizationService.GetString("AddProfile.Analysis.Failed"), FormStatusKind.Error);
                return;
            }

            SetStatus(_localizationService.GetString("AddProfile.Analysis.Completed"), FormStatusKind.Success);
        }
        catch (OperationCanceledException)
        {
            // Ignored, active analysis cancelled
        }
        catch (Exception ex)
        {
            if (generation == _analysisGeneration)
            {
                SetStatus(UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("AddProfile.Analysis.ErrorPrefix"), ex), FormStatusKind.Error);
                ConnectionHealth = ConnectionHealth.Critical;
                DetailedStatus = _localizationService.GetString("AddProfile.Analysis.UnexpectedError");
            }
        }
        finally
        {
            if (generation == _analysisGeneration)
            {
                IsAnalyzingConnection = false;
            }
        }
    }

    private async Task AnalyzeRemoteM3uAsync(string urlToCheck, CancellationToken cancellationToken, int generation)
    {
        IsAnalyzingConnection = true;
        HasError = false;
        SetStatus(_localizationService.GetString("AddProfile.Status.Analyzing"), FormStatusKind.Progress);
        PlaylistPreviewSummary = string.Empty;
        ConnectionHealth = ConnectionHealth.Unknown;
        DetailedStatus = string.Empty;
        UrlError = null;

        try
        {
            // Remote M3U URL: HTTP health check (same as Xtream/Stalker).
            // Shows "200 OK - 450ms" format.
            var (health, statusCode, latency, error) = await PerformHealthCheckAsync(urlToCheck, cancellationToken);

            if (generation != _analysisGeneration) return;

            ConnectionHealth = health;
            DetailedStatus = FormatDetailedStatus(statusCode, latency, error);

            if (health == ConnectionHealth.Critical)
            {
                SetStatus(_localizationService.GetString("AddProfile.Analysis.Failed"), FormStatusKind.Error);
                return;
            }

            SetStatus(_localizationService.GetString("AddProfile.Analysis.Completed"), FormStatusKind.Success);
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            if (generation == _analysisGeneration)
            {
                ConnectionHealth = ConnectionHealth.Critical;
                DetailedStatus = UserFriendlyErrorMessage.FromException(ex);
                SetStatus(string.Empty, FormStatusKind.None);
            }
        }
        finally
        {
            if (generation == _analysisGeneration)
            {
                IsAnalyzingConnection = false;
            }
        }
    }

    private async Task AnalyzeLocalM3uFileAsync(string filePath, CancellationToken cancellationToken, int generation)
    {
        IsAnalyzingConnection = true;
        HasError = false;
        SetStatus(_localizationService.GetString("Profiles.Account.FileValidation"), FormStatusKind.Progress);
        PlaylistPreviewSummary = string.Empty;
        ConnectionHealth = ConnectionHealth.Unknown;
        DetailedStatus = string.Empty;

        try
        {
            // Count #EXTINF lines to determine channel count and validate M3U format.
            var (isValid, channelCount) = await CountM3uChannelsAsync(filePath, cancellationToken);

            if (generation != _analysisGeneration) return;

            if (!isValid)
            {
                SetStatus(_localizationService.GetString("AddProfile.Analysis.Failed"), FormStatusKind.Error);
                ConnectionHealth = ConnectionHealth.Critical;
                DetailedStatus = _localizationService.GetString("Playlist.Error.EmptyNoDelete");
                return;
            }

            ConnectionHealth = ConnectionHealth.Good;
            DetailedStatus = string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("Profiles.Account.ValidM3uFileWithCount"),
                channelCount.ToString("N0", CultureInfo.CurrentCulture));
            SetStatus(_localizationService.GetString("AddProfile.Analysis.Completed"), FormStatusKind.Success);
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            if (generation == _analysisGeneration)
            {
                ConnectionHealth = ConnectionHealth.Critical;
                SetStatus(UserFriendlyErrorMessage.WithPrefix(
                    _localizationService.GetString("AddProfile.Analysis.ErrorPrefix"),
                    ex), FormStatusKind.Error);
                DetailedStatus = UserFriendlyErrorMessage.FromException(ex);
            }
        }
        finally
        {
            if (generation == _analysisGeneration)
            {
                IsAnalyzingConnection = false;
            }
        }
    }

    /// <summary>
    /// Counts all #EXTINF lines in an M3U file to determine channel count
    /// and validate format. First line must be #EXTM3U header.
    /// </summary>
    private static async Task<(bool IsValid, int ChannelCount)> CountM3uChannelsAsync(
        string filePath, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(filePath);
        var lineCount = 0;
        var channelCount = 0;
        var headerValid = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;

            lineCount++;

            // First non-empty line must be #EXTM3U header
            if (!headerValid)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                headerValid = line.TrimStart().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase);
                if (!headerValid) break;
                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                channelCount++;
            }
        }

        return (headerValid && channelCount > 0, channelCount);
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

            if (IsM3U)
            {
                var result = await VerifyRemoteM3uAsync(Url.Trim(), cancellationToken);
                return (result.Health, result.Latency, result.Error);
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

    private async Task<(ConnectionHealth Health, long Latency, string? Error)> VerifyRemoteM3uAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var count = 0;

        try
        {
            await foreach (var _ in _m3uParser.ParseFromUrlStreamAsync(url, cancellationToken).WithCancellation(cancellationToken))
            {
                count++;
                if (count >= 1)
                {
                    break;
                }
            }

            stopwatch.Stop();                return count > 0
                    ? (ClassifyLatency(stopwatch.ElapsedMilliseconds), stopwatch.ElapsedMilliseconds, null)
                    : (ConnectionHealth.Critical, stopwatch.ElapsedMilliseconds, _localizationService.GetString("Playlist.Error.EmptyNoDelete"));
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

    private async Task<(ConnectionHealth Health, int? StatusCode, long? Latency, string? Error)> PerformHealthCheckAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new System.Net.Http.HttpClientHandler();
            using var client = new System.Net.Http.HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15); // 15s timeout
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Try HEAD first for lightweight check, fallback to GET
            System.Net.Http.HttpResponseMessage response;
            try
            {
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                response = await client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    response.Dispose();
                    stopwatch.Restart();
                    response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
            }
            catch (System.Net.Http.HttpRequestException)
            {
                // Some servers block HEAD, try GET
                stopwatch.Restart();
                response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
        // Cancel any running connection analysis before proceeding
        CancelActiveAnalysis();

        HasError = false;
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
        }

        // Mark submitted so all validators run unconditionally
        IsSubmitted = true;
        ValidateProfileName();
        ValidateUrlField();
        ValidateUsernameField();
        ValidatePasswordField();
        IsPinCodeTouched = true;
        ValidatePinCodeField();
        if (PinError == null)
        {
            IsPinConfirmTouched = true;
            ValidatePinConfirmField();
        }

        if (ProfileNameError != null || ServerUrlError != null || UsernameError != null ||
            PasswordError != null || MacAddressError != null || PinError != null || PinConfirmationError != null)
        {
            if (ProfileNameError != null) ValidationErrorOccurred?.Invoke(this, "ProfileName");
            else if (ServerUrlError != null) ValidationErrorOccurred?.Invoke(this, "Url");
            else if (UsernameError != null || MacAddressError != null) ValidationErrorOccurred?.Invoke(this, "Username");
            else if (PasswordError != null) ValidationErrorOccurred?.Invoke(this, "Password");
            else if (PinError != null) ValidationErrorOccurred?.Invoke(this, "PinCode");
            else if (PinConfirmationError != null) ValidationErrorOccurred?.Invoke(this, "PinConfirm");

            return;
        }

        // Zayıf PIN uyarısı — PIN yasaklanmaz, yalnızca kullanıcı onayı istenir.
        // Edit modunda yeni kod girilmediyse (mevcut PIN korunuyor) kontrol atlanır.
        if (HasPin && PinCode.Length == 4 && PinWeaknessEvaluator.IsWeak(PinCode))
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                _localizationService.GetString("AddProfile.Pin.Weak.Title"),
                _localizationService.GetString("AddProfile.Pin.Weak.Message"));
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            // Show saving indicator
            SetStatus(_localizationService.GetString("AddProfile.Status.Saving"), FormStatusKind.Progress);
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
                SetStatus(_localizationService.GetString("AddProfile.Status.Analyzing"), FormStatusKind.Progress);

                var providerValidation = await VerifyProviderAsync(CancellationToken.None);
                if (providerValidation.Health == ConnectionHealth.Critical)
                {
                    var errorMsg = string.IsNullOrWhiteSpace(providerValidation.Error)
                        ? _localizationService.GetString("AddProfile.Error.NewProviderValidationFailed")
                        : providerValidation.Error;
                    SetStatus(errorMsg, FormStatusKind.Error);
                    ConnectionHealth = ConnectionHealth.Critical;
                    DetailedStatus = errorMsg;
                    return;
                }

                if (providerValidation.Health == ConnectionHealth.Unknown)
                {
                    var preview = await BuildImportPreviewAsync();
                    if (!preview.IsValid || preview.TotalChannels <= 0)
                    {
                        var errorMsg = string.IsNullOrWhiteSpace(preview.ErrorMessage)
                            ? _localizationService.GetString("AddProfile.Error.NewProviderValidationFailed")
                            : preview.ErrorMessage;
                        SetStatus(errorMsg, FormStatusKind.Error);
                        ConnectionHealth = ConnectionHealth.Critical;
                        DetailedStatus = errorMsg;
                        return;
                    }
                }
            }

            var existingPinHash = EditingProfile?.PinHash;
            var effectivePinHash = !_licenseService.IsPremium && existingPinHash != null
                ? existingPinHash  // Premium expired: keep the existing PIN untouched
                : HasPin && PinCode.Length == 4
                    ? _securityService.HashPin(PinCode)
                    : HasPin && existingPinHash != null
                        ? existingPinHash  // Keep existing PIN if toggle is on but no new code entered
                        : null;            // PIN disabled or removed

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
                PinHash = effectivePinHash,
                ExistingIds = EditingProfile != null
                    ? new ExistingProfileIds(EditingProfile.Id, EditingProfile.ProviderAccountId)
                    : null,
                AccessGrant = AccessGrant
            };

            var result = await _profileService.SaveProfileAsync(request);

            if (result == null)
            {
                // License limit exceeded
                await _dialogService.ShowUpsellAsync();
                return;
            }

            // Silinme geri sayımındaki bir profil PIN doğrulamasıyla düzenlendiyse
            // (yönetim modu) üç günlük silme otomatik iptal edilir — normal giriş
            // dalında olduğu gibi; aksi halde kullanıcı profili "kurtuldu" sanıp
            // üç gün sonra silinmesini izleyebilir.
            if (EditingProfile is { IsPendingDeletion: true })
            {
                await _profileService.CancelProfileDeletionAsync(EditingProfile.Id);
            }

            // Success feedback
            SetStatus(_localizationService.GetString("AddProfile.Status.Saved"), FormStatusKind.Success);
            await Task.Delay(400);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SetStatus(UserFriendlyErrorMessage.WithPrefix(_localizationService.GetString("Common.ErrorPrefix"), ex), FormStatusKind.Error);
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


