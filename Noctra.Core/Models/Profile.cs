using System.ComponentModel.DataAnnotations;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Noctra.Models;

public enum ProfileType
{
    M3U,
    XtreamCodes,
    StalkerPortal
}

public class Profile
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int ProviderAccountId { get; set; }
    public ProviderAccount? ProviderAccount { get; set; }

    // Visuals
    public string Avatar { get; set; } = "default"; // Asset name or path
    public string Color { get; set; } = "#FF6B00"; // Default accent color

    public bool IsChild { get; set; }

    public DateTime LastUsed { get; set; } = DateTime.MinValue;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string Initials => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?";
}

