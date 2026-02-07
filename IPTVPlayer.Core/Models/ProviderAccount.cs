using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
    // Encryption Logic
    [Column("Password")]
    public string? EncryptedPassword { get; set; }

    [NotMapped]
    public string? Password 
    { 
        get
        {
            if (string.IsNullOrEmpty(EncryptedPassword)) return null;
            try
            {
                var bytes = Convert.FromBase64String(EncryptedPassword);
#pragma warning disable CA1416 // Windows only
                var plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                return System.Text.Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // Fallback for legacy plain-text passwords
                return EncryptedPassword;
            }
        }
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                EncryptedPassword = null;
                return;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
#pragma warning disable CA1416 // Windows only
            var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            EncryptedPassword = Convert.ToBase64String(encrypted);
        }
    }

    public ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}
