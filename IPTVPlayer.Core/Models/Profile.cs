using System.ComponentModel.DataAnnotations;

using CommunityToolkit.Mvvm.ComponentModel;

namespace IPTVPlayer.Models;

public enum ProfileType
{
    M3U,
    XtreamCodes,
    StalkerPortal
}

public partial class Profile : ObservableObject
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int ProviderAccountId { get; set; }
    
    private ProviderAccount? _providerAccount;
    public ProviderAccount? ProviderAccount 
    {
        get => _providerAccount;
        set => SetProperty(ref _providerAccount, value);
    }

    // Visuals
    public string Avatar { get; set; } = "default"; // Asset name or path
    public string Color { get; set; } = "#FF6B00"; // Default accent color

    public bool IsChild { get; set; }

    public DateTime LastUsed { get; set; } = DateTime.MinValue;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Initials => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?";
}
