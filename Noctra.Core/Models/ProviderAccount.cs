using System.ComponentModel.DataAnnotations;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Noctra.Models;

public class ProviderAccount
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public ProfileType Type { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? Username { get; set; }

    public string? Password { get; set; }
    
    public DateTime? ExpirationDate { get; set; }

    public ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}

