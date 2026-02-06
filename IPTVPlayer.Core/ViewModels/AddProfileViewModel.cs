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

    partial void OnUrlChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        // Auto-detect M3U
        var lower = value.ToLower();
        if (lower.Contains(".m3u") || lower.Contains(".m3u8") || lower.Contains("get.php"))
        {
            if (!IsM3U)
            {
                IsM3U = true; 
            }
        }

        // Automatic Credential Extraction
        try
        {
            if (value.Contains("?"))
            {
                var uri = new Uri(value);
                var query = uri.Query;
                
                // Parse Query manually to be more flexible (some use & but some might have USERNAMEpassword=...)
                // Standard case
                var usernameMatch = System.Text.RegularExpressions.Regex.Match(query, @"[?&]username=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var passwordMatch = System.Text.RegularExpressions.Regex.Match(query, @"[?&]password=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (usernameMatch.Success) Username = Uri.UnescapeDataString(usernameMatch.Groups[1].Value);
                if (passwordMatch.Success) Password = Uri.UnescapeDataString(passwordMatch.Groups[1].Value);

                // If it's a get.php style link, extract the base server URL
                if (lower.Contains("get.php"))
                {
                    var baseUrl = value.Split('?')[0].Replace("/get.php", "");
                    Url = baseUrl; // This will trigger OnUrlChanged again but with no query, so it's safe
                    
                    if (!IsXtream) IsXtream = true; // Usually get.php implies Xtream server backend
                }
            }
        }
        catch 
        {
            // Invalid URI format, skip auto-extraction
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

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (EditingProfile == null) return;

        var confirmed = await _context.Profiles.AnyAsync(p => p.Id == EditingProfile.Id);
        if (!confirmed) return;

        _context.Profiles.Remove(EditingProfile);
        await _context.SaveChangesAsync();
        
        RequestClose?.Invoke(this, EventArgs.Empty);
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
                 var lowerUrl = Url.ToLower();
                 bool isValidM3U = lowerUrl.Contains(".m3u") || lowerUrl.Contains(".m3u8") || lowerUrl.Contains("get.php");
                 
                 if (!isValidM3U)
                 {
                     StatusMessage = "Geçerli bir M3U adresi girin (m3u, m3u8 veya get.php içermeli)";
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
                // Fix: Check if already tracked to avoid collision
                var tracked = _context.ProviderAccounts.Local.FirstOrDefault(a => a.Id == SelectedAccount.Id);
                if (tracked != null && tracked != SelectedAccount)
                {
                    // Update the tracked instance instead
                    tracked.Url = Url;
                    tracked.Username = Username;
                    tracked.Password = Password;
                    tracked.Type = IsXtream ? ProfileType.XtreamCodes : ProfileType.M3U;
                    account = tracked;
                }
                else
                {
                    SelectedAccount.Url = Url;
                    SelectedAccount.Username = Username;
                    SelectedAccount.Password = Password;
                    SelectedAccount.Type = IsXtream ? ProfileType.XtreamCodes : ProfileType.M3U;
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
                // Update existing
                EditingProfile.Name = ProfileName;
                EditingProfile.Avatar = SelectedAvatar;
                EditingProfile.ProviderAccountId = account.Id;
                _context.Profiles.Update(EditingProfile);
            }
            else
            {
                // Create new
                var profile = new Profile
                {
                    Name = ProfileName,
                    ProviderAccountId = account.Id,
                    Avatar = SelectedAvatar,
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
