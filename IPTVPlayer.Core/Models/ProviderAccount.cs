using System.ComponentModel.DataAnnotations;

using CommunityToolkit.Mvvm.ComponentModel;

namespace IPTVPlayer.Models;

public partial class ProviderAccount : ObservableObject
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty; // User friendly name e.g. "My Xtream Sub"

    public ProfileType Type { get; set; }

    // Connection Details
    public string Url { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    
    // Explicit backing field for notification
    private DateTime? _expirationDate;
    public DateTime? ExpirationDate 
    {
        get => _expirationDate;
        set => SetProperty(ref _expirationDate, value);
    }

    public ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}
