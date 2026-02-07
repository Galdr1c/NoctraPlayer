using System.ComponentModel.DataAnnotations;

namespace IPTVPlayer.Models;

public class ProviderAccount
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

    public ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}
