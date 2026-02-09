using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Data;

namespace IPTVPlayer.ViewModels;

public partial class AddProfileViewModel : ObservableObject
{
    private readonly AppDbContext _context;
    private readonly IDispatcherService _dispatcherService;
    private readonly IAvatarService _avatarService;
    private readonly IDialogService _dialogService;

    // Simplified Account Details
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    private bool _isUpdatingUrl = false;

    partial void OnUrlChanged(string value)
    {
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

            // Extract credentials
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

                // Extract base URL without triggering recursion
                if (lower.Contains("get.php"))
                {
                    var parts = value.Split('?');
                    if (parts.Length > 0)
                    {
                        var baseUrl = parts[0].Replace("/get.php", "");
                        if (baseUrl != value)
                        {
                            // Use field directly to avoid triggering OnChanged
                            _url = baseUrl;
                            OnPropertyChanged(nameof(Url));
                        }
                    }
                    
                    if (!IsXtream) IsXtream = true;
                }
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
            StatusMessage = $"URL parse hatası: {ex.Message}";
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
            StatusMessage = $"URL oluşturma hatası: {ex.Message}";
            HasError = true;
        }
    }

    [ObservableProperty]
    private string _username = string.Empty;

    partial void OnUsernameChanged(string value)
    {
        if (_isUpdatingUrl) return;
        
        if (IsM3U && !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Password))
        {
            ConvertXtreamToM3UUrl();
        }
    }

    [ObservableProperty]
    private string _password = string.Empty;

    partial void OnPasswordChanged(string value)
    {
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
        if (_isUpdatingUrl) return;
        
        if (value)
        {
            IsM3U = false;
            
            // M3U URL'den Xtream'e geçiş - Parse et
            if (!string.IsNullOrWhiteSpace(Url) && Url.Contains("get.php"))
            {
                ParseCredentialsFromUrl(Url);
                ConvertM3UUrlToXtream(Url);
            }
        }
    }

    [ObservableProperty]
    private bool _isM3U = false;

    partial void OnIsM3UChanged(bool value)
    {
        if (_isUpdatingUrl) return;
        
        if (value)
        {
            IsXtream = false;
            
            // Xtream'den M3U'ya geçiş - Rebuild URL
            if (!string.IsNullOrWhiteSpace(Url) && 
                !string.IsNullOrWhiteSpace(Username) && 
                !string.IsNullOrWhiteSpace(Password))
            {
                ConvertXtreamToM3UUrl();
            }
        }
    }
    
    // Profile Details
    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string? _profileNameError;
    
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
    private bool _isSaving;

    [ObservableProperty]
    private string? _urlError;

    [ObservableProperty]
    private Profile? _editingProfile;

    public event EventHandler? RequestClose;
    public event EventHandler? RequestAvatarPicker;

    public AddProfileViewModel(AppDbContext context, IDispatcherService dispatcherService, IAvatarService avatarService, IDialogService dialogService)
    {
        _context = context;
        _dispatcherService = dispatcherService;
        _avatarService = avatarService;
        _dialogService = dialogService;

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
        
        // Use a background task to set account
        _ = InitializeEditAsync(profile);
    }

    private async Task InitializeEditAsync(Profile profile)
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

    private bool ValidateUrl()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            UrlError = "URL gereklidir";
            return false;
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

        if (IsXtream && (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)))
        {
            UrlError = "Xtream için kullanıcı adı ve şifre gereklidir";
            return false;
        }

        UrlError = null;
        return true;
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
            var dbProfile = await _context.Profiles
                .Include(p => p.ProviderAccount)
                .FirstOrDefaultAsync(p => p.Id == EditingProfile.Id);

            if (dbProfile != null)
            {
                // Delete the account first if it's uniquely linked
                if (dbProfile.ProviderAccount != null)
                {
                    _context.ProviderAccounts.Remove(dbProfile.ProviderAccount);
                }
                
                _context.Profiles.Remove(dbProfile);
                await _context.SaveChangesAsync();
            }
            
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Hata", "Profil silinirken bir hata oluştu.", ex);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ProfileNameError = null;
        UrlError = null;
        HasError = false;

        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            ProfileNameError = "Profil adı gereklidir";
            return;
        }

        if (!ValidateUrl())
        {
            return;
        }

        try
        {
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
                 selectedAccount.Type = IsXtream ? ProfileType.XtreamCodes : ProfileType.M3U;
                 
                 _context.Entry(selectedAccount).State = EntityState.Modified;
                 account = selectedAccount;
            }
            else
            {
                account = new ProviderAccount
                {
                    Name = ProfileName + " Hesabı",
                    Type = IsXtream ? ProfileType.XtreamCodes : ProfileType.M3U,
                    Url = Url,
                    Username = Username,
                    Password = Password
                };
                _context.ProviderAccounts.Add(account);
            }

            if (EditingProfile != null)
            {
                // Update existing profile
                
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

            StatusMessage = "✓ Profil kaydedildi";
            await Task.Delay(500);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            HasError = true;
            string detail = ex.InnerException?.Message ?? ex.Message;
            StatusMessage = $"Hata: {detail}";
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
