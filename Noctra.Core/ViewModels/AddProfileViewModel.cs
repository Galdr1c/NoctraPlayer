using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

            StatusMessage = "M3U linki Xtream formatına dönüştürüldü";
            HasError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("URL parse hatası", ex);
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
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("URL oluşturma hatası", ex);
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

        if (!string.Equals(UrlError, "MAC adresi geçersiz. Örnek: 00:1A:79:AA:BB:CC", StringComparison.Ordinal) &&
            !string.Equals(UrlError, "Stalker Portal için MAC adresi gereklidir", StringComparison.Ordinal))
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
            UrlError = "Geçersiz URL formatı";
            return;
        }

        if (IsM3U)
        {
            var lower = normalizedUrl.ToLowerInvariant();
            if (!lower.Contains(".m3u") && !lower.Contains(".m3u8") && !lower.Contains("get.php"))
            {
                UrlError = "M3U URL'i .m3u, .m3u8 veya get.php içermelidir";
                return;
            }
        }

        if (IsXtream && (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)))
        {
            UrlError = "Xtream için kullanıcı adı ve şifre gereklidir";
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
            UrlError = "Stalker Portal için MAC adresi gereklidir";
            return;
        }

        UrlError = StalkerMacRegex().IsMatch(currentUsername.Trim())
            ? null
            : "MAC adresi geçersiz. Örnek: 00:1A:79:AA:BB:CC";
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
        ISecurityService securityService)
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
            Password = _securityService.Decrypt(profile.ProviderAccount.Password) ?? string.Empty;
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
            $"'{profile.Name}' profilini silmek istediğinize emin misiniz?");
        if (!confirmed) return;

        try
        {
            IsSaving = true;
            StatusMessage = "Profil siliniyor...";

            await _profileService.DeleteProfileAsync(
                EditingProfile.Id,
                EditingProfile.ProviderAccountId);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Hata", "Profil silinirken bir hata oluştu.", ex);
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
            UrlError = "Geçersiz URL formatı";
            return false;
        }

        if (IsM3U)
        {
            var lower = Url.ToLowerInvariant();
            if (!lower.Contains(".m3u") && !lower.Contains(".m3u8") && !lower.Contains("get.php"))
            {
                UrlError = "M3U URL'i .m3u, .m3u8 veya get.php içermelidir";
                return false;
            }
        }

        if (IsXtream)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                UrlError = "Xtream için kullanıcı adı ve şifre gereklidir";
                return false;
            }
        }

        if (IsStalker)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                UrlError = "Stalker Portal için MAC adresi gereklidir";
                return false;
            }

            if (!StalkerMacRegex().IsMatch(Username.Trim()))
            {
                UrlError = "MAC adresi geçersiz. Örnek: 00:1A:79:AA:BB:CC";
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
            StatusMessage = "URL gereklidir";
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
            // Username/password yok, sadece base sunucuyu kontrol et
            var uri = new Uri(urlToCheck);
            urlToCheck = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
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
            StatusMessage = "Geçersiz URL formatı";
            return;
        }

        IsAnalyzingConnection = true;
        HasError = false;
        StatusMessage = "Bağlantı analiz ediliyor...";
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
                StatusMessage = "Bağlantı analizi başarısız";
                return;
            }
            
            HasError = false;
            StatusMessage = "Bağlantı analizi tamamlandı";
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = UserFriendlyErrorMessage.WithPrefix("Analiz hatası", ex);
            ConnectionHealth = ConnectionHealth.Critical;
            DetailedStatus = "Beklenmeyen Hata";
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

        var statusText = statusCode.HasValue ? $"{statusCode} {GetReasonPhrase(statusCode.Value)}" : "Bilinmiyor";
        var latencyText = latency.HasValue ? $"{latency}ms" : "";
        
        return $"{statusText} - {latencyText}".Trim(' ', '-');
    }

    private string GetReasonPhrase(int statusCode)
    {
        return statusCode switch
        {
            200 => "Tamam",
            301 => "Kalıcı Yönlendirme",
            302 => "Geçici Yönlendirme",
            400 => "Geçersiz İstek",
            401 => "Yetkisiz (Kullanıcı Adı/Şifre)",
            403 => "Yasaklı (Erişim Reddedildi)",
            404 => "Bulunamadı (URL Hatalı)",
            500 => "Sunucu Hatası",
            502 or 503 or 504 => "Ağ Geçidi Hatası (Sunucu erişilebilir fakat endpoint yanıt vermiyor — bağlantı çalışıyor olabilir)",
            >= 500 => "Sunucu Hatası (Sağlayıcı kaynaklı geçici sorun olabilir)",
            _ => $"HTTP Hatası {statusCode}"
        };
    }

    private async Task<(ConnectionHealth Health, int? StatusCode, long? Latency, string? Error)> PerformHealthCheckAsync(string url)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10); // 10s timeout

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Try HEAD first for lightweight check, fallback to GET
            System.Net.Http.HttpResponseMessage response;
            try
            {
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                response = await client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
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
                var health = latency < 300 ? ConnectionHealth.Good : 
                             latency < 1000 ? ConnectionHealth.Weak : 
                             ConnectionHealth.Bad;
                
                return (health, code, latency, null);
            }
            else
            {
                var error = code switch
                {
                    401 or 403 => "Yetkisiz Erişim (Kullanıcı adı/Şifre hatalı olabilir)",
                    404 => "URL Bulunamadı (Link bozuk veya kanal silinmiş)",
                    >= 500 => "Sunucu Hatası (Sağlayıcı kaynaklı sorun)",
                    _ => $"HTTP Hatası {code}"
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

            var existingAccountDuplicate = await _profileService.CheckDuplicateAccountAsync(
                excludedProviderAccountId, selectedType, Url, Username, Password);

            IReadOnlyCollection<Channel> channels;
            if (IsStalker)
            {
                channels = await _stalkerPortalService.GetChannelsAsync(
                    Url,
                    Username,
                    includeVod: true);

                return PlaylistImportPreview.FromChannels(channels, "Stalker Portal", existingAccountDuplicate, health, latency, statusCode);
            }

            if (IsXtream)
            {
                channels = await _xtreamCodesService.GetChannelsAsync(
                    Url,
                    Username,
                    Password,
                    includeSeriesEpisodes: false);

                return PlaylistImportPreview.FromChannels(channels, "Xtream", existingAccountDuplicate, health, latency, statusCode);
            }

            channels = await _m3uParser.ParseFromUrlAsync(Url);
            return PlaylistImportPreview.FromChannels(channels, "M3U", existingAccountDuplicate, health, latency, statusCode);
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
                ProfileNameError = "Profil adı gereklidir";
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

            var request = new ProfileSaveRequest
            {
                ProfileName = ProfileName,
                Avatar = SelectedAvatar,
                IsChild = IsChild,
                Url = Url,
                Username = Username,
                EncryptedPassword = _securityService.Encrypt(Password) ?? string.Empty,
                AccountType = IsStalker
                    ? ProfileType.StalkerPortal
                    : IsXtream
                        ? ProfileType.XtreamCodes
                        : ProfileType.M3U,
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


