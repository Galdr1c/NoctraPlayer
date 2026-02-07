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

    // Accounts
    [ObservableProperty]
    private List<ProviderAccount> _existingAccounts = new();

    [ObservableProperty]
    private ProviderAccount? _selectedAccount;

    [ObservableProperty]
    private bool _isNewAccount = true;

    // New Account Details
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    private bool _isUpdatingUrl;

    partial void OnUrlChanged(string value)
    {
        if (string.IsNullOrEmpty(value) || _isUpdatingUrl) return;

        try
        {
            _isUpdatingUrl = true;
            var lower = value.ToLower();

            // 1. Auto-detect Type
            if (lower.Contains(".m3u") || lower.Contains(".m3u8") || lower.Contains("get.php"))
            {
                 // Only auto-switch to M3U if it looks like a file list and NOT a get.php API call
                 if ((lower.Contains(".m3u") || lower.Contains(".m3u8")) && !lower.Contains("get.php"))
                 {
                     if (!IsM3U) IsM3U = true;
                 }
                 else if (lower.Contains("get.php"))
                 {
                     // get.php is typically Xtream
                     if (!IsXtream) IsXtream = true;
                 }
            }

            // 2. Credential Extraction
            if (value.Contains("?"))
            {
                var uri = new Uri(value);
                
                // Fallback to manual parsing if HttpUtility is not available in Core
                string? user = null, pass = null;

                // Try regex for common patterns
                var uMatch = System.Text.RegularExpressions.Regex.Match(uri.Query, @"[?&](username|user|u)=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (uMatch.Success) user = Uri.UnescapeDataString(uMatch.Groups[2].Value);

                var pMatch = System.Text.RegularExpressions.Regex.Match(uri.Query, @"[?&](password|pass|p)=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (pMatch.Success) pass = Uri.UnescapeDataString(pMatch.Groups[2].Value);

                if (!string.IsNullOrEmpty(user)) Username = user;
                if (!string.IsNullOrEmpty(pass)) Password = pass;

                // Clean URL for Xtream
                if (lower.Contains("get.php"))
                {
                    // For Xtream, we often want just the base domain + port
                    // e.g. http://server:8080/get.php... -> http://server:8080
                    var cleanUrl = value.Split("/get.php")[0];
                    if (Url != cleanUrl) 
                    {
                        Url = cleanUrl; // This will re-trigger, so return
                        return;
                    }
                }
            }
        }
        catch 
        {
            // Ignore parsing errors
        }
        finally
        {
            _isUpdatingUrl = false;
        }
    }

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;
    
    [ObservableProperty]
    private bool _isXtream = true;

    [ObservableProperty]
    private bool _isM3U = false;
    
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
    private bool _isEditingAccount;

    [ObservableProperty]
    private Profile? _editingProfile;

    public event EventHandler? RequestClose;
    public event EventHandler? RequestAvatarPicker;

    public Task InitializationTask { get; private set; }

    public AddProfileViewModel(AppDbContext context, IDispatcherService dispatcherService, IAvatarService avatarService)
    {
        _context = context;
        _dispatcherService = dispatcherService;
        _avatarService = avatarService;

        // Initialize with default avatar
        var avatars = _avatarService.GetAvatarsByCategory().Values.FirstOrDefault();
        if (avatars != null && avatars.Any()) SelectedAvatar = avatars.First();

        InitializationTask = LoadAccountsAsync();
    }

    public void InitializeForEdit(Profile profile)
    {
        EditingProfile = profile;
        ProfileName = profile.Name;
        SelectedAvatar = profile.Avatar ?? "default";
        IsChild = profile.IsChild;
        
        // Use a background task to wait for initialization and then set account
        _ = InitializeEditAsync(profile);
    }

    private async Task InitializeEditAsync(Profile profile)
    {
        await InitializationTask;

        if (profile.ProviderAccount != null)
        {
            IsNewAccount = false;
            SelectedAccount = ExistingAccounts.FirstOrDefault(a => a.Id == profile.ProviderAccountId) ?? profile.ProviderAccount;
            
            // Pre-populate fields for potential editing
            Url = profile.ProviderAccount.Url;
            Username = profile.ProviderAccount.Username ?? string.Empty;
            Password = profile.ProviderAccount.Password ?? string.Empty;
            IsXtream = profile.ProviderAccount.Type == ProfileType.XtreamCodes;
            IsM3U = profile.ProviderAccount.Type == ProfileType.M3U;
        }
    }

    private async Task LoadAccountsAsync()
    {
        try 
        {
            ExistingAccounts = await _context.ProviderAccounts.ToListAsync();
            
            if (EditingProfile != null)
            {
                SelectedAccount = ExistingAccounts.FirstOrDefault(a => a.Id == EditingProfile.ProviderAccountId);
                IsNewAccount = false;
            }
            else if (ExistingAccounts.Any() && SelectedAccount == null)
            {
                IsNewAccount = false;
                SelectedAccount = ExistingAccounts.First();
            }
        }
        catch { /* Handle DB error */ }
    }

    partial void OnIsXtreamChanged(bool value)
    {
        if (value) IsM3U = false;
    }

    partial void OnIsM3UChanged(bool value)
    {
        if (value) IsXtream = false;
    }

    [RelayCommand]
    private void OpenAvatarPicker()
    {
        RequestAvatarPicker?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleEditAccount()
    {
        if (SelectedAccount != null)
        {
            IsEditingAccount = !IsEditingAccount;
            if (IsEditingAccount)
            {
                // Fill from selected
                Url = SelectedAccount.Url;
                Username = SelectedAccount.Username ?? string.Empty;
                Password = SelectedAccount.Password ?? string.Empty;
                IsXtream = SelectedAccount.Type == ProfileType.XtreamCodes;
                IsM3U = SelectedAccount.Type == ProfileType.M3U;
            }
        }
    }
    
    public void SetAvatar(string avatar)
    {
        SelectedAvatar = avatar;
    }

    private async Task DeleteProfileAsync()
    {
        if (EditingProfile == null) return;

        try
        {
            // Fetch fresh entity to avoid tracking issues
            var profileToDelete = await _context.Profiles.FindAsync(EditingProfile.Id);
            if (profileToDelete != null)
            {
                _context.Profiles.Remove(profileToDelete);
                await _context.SaveChangesAsync();
            }
            
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Silme hatası: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Validation logic
        if (IsNewAccount || IsEditingAccount)
        {
             if (string.IsNullOrWhiteSpace(Url) || (IsXtream && (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))))
             {
                 StatusMessage = "Lütfen hesap bilgilerini eksiksiz girin";
                 HasError = true;
                 return;
             }

             // URL Validation for M3U as requested
             if (IsM3U)
             {
                 bool isValidM3U = Uri.TryCreate(Url, UriKind.Absolute, out var uri) &&
                                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                                   (uri.AbsolutePath.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || 
                                    uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                                    (uri.Query != null && uri.Query.Contains("get.php")));
                 
                 if (!isValidM3U)
                 {
                     StatusMessage = "Geçerli bir M3U adresi girin (http/s ve .m3u/.m3u8 veya get.php)";
                     HasError = true;
                     return;
                 }
             }
        }
        else if (SelectedAccount == null)
        {
            StatusMessage = "Lütfen bir hesap seçin";
            HasError = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            StatusMessage = "Profil adı gereklidir";
            HasError = true;
            return;
        }

        try
        {
            HasError = false;
            StatusMessage = "Kaydediliyor...";

            ProviderAccount account;

            if (IsNewAccount)
            {
                account = new ProviderAccount
                {
                    Name = string.IsNullOrEmpty(Name) ? ProfileName + " Hesabı" : Name,
                    Type = IsXtream ? ProfileType.XtreamCodes : ProfileType.M3U,
                    Url = Url,
                    Username = Username,
                    Password = Password
                };
                _context.ProviderAccounts.Add(account);
                await _context.SaveChangesAsync();
            }
            else if (IsEditingAccount && SelectedAccount != null)
            {
                // Fix: Fetch by ID to ensure we are updating the tracked entity in THIS context
                var existingAccount = await _context.ProviderAccounts.FindAsync(SelectedAccount.Id);
                
                if (existingAccount != null)
                {
                    // Update the found entity
                    existingAccount.Url = Url;
                    existingAccount.Username = Username;
                    existingAccount.Password = Password;
                    existingAccount.Type = IsXtream ? ProfileType.XtreamCodes : ProfileType.M3U;
                    
                    // Use the tracked entity moving forward
                    account = existingAccount;
                }
                else
                {
                    // Should not happen if ID exists, but fallback
                    _context.ProviderAccounts.Update(SelectedAccount);
                    account = SelectedAccount;
                }
                await _context.SaveChangesAsync();
            }
            else
            {
                account = SelectedAccount!;
            }

            if (EditingProfile != null)
            {
                // Update existing - Fetch fresh to avoid tracking conflict
                var profileToUpdate = await _context.Profiles.FindAsync(EditingProfile.Id);
                if (profileToUpdate != null)
                {
                    profileToUpdate.Name = ProfileName;
                    profileToUpdate.Avatar = SelectedAvatar;
                    profileToUpdate.ProviderAccountId = account.Id;
                    profileToUpdate.IsChild = IsChild;
                    _context.Profiles.Update(profileToUpdate);
                }
            }
            else
            {
                // Create new
                var profile = new Profile
                {
                    Name = ProfileName,
                    ProviderAccountId = account.Id,
                    Avatar = SelectedAvatar,
                    IsChild = IsChild,
                    LastUsed = DateTime.Now
                };
                _context.Profiles.Add(profile);
            }

            await _context.SaveChangesAsync();

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            HasError = true;
            string detail = ex.InnerException?.Message ?? ex.Message;
            StatusMessage = $"Hata: {detail}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
