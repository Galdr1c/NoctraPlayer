using System.Collections.Generic;
using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

public interface IAvatarService
{
    Dictionary<string, List<string>> GetAvatarsByCategory(); // Category -> List of Asset Names
    string GetAvatarPath(string avatarName);
    Task<string> CacheAvatarAsync(string sourceUrl); // Optional for external
}

public class AvatarService : IAvatarService
{
    // Flat list of avatar identifiers matching the files in Assets/Avatars
    private readonly List<string> _avatars = new()
    {
        "avatar_1", "avatar_2", "avatar_3", "avatar_4",
        "avatar_5", "avatar_6", "avatar_7", "avatar_8",
        "avatar_9", "avatar_10", "avatar_11", "avatar_12",
        "avatar_13", "avatar_14", "avatar_15", "avatar_16",
        "avatar_17", "avatar_18", "avatar_19", "avatar_20",
        "avatar_21", "avatar_22", "avatar_23", "avatar_24"
    };

    public Dictionary<string, List<string>> GetAvatarsByCategory()
    {
        // For compatibility, return everything in one "All" category
        return new Dictionary<string, List<string>> { { "All", _avatars } };
    }

    public string GetAvatarPath(string avatarName)
    {
        // Map to physical file in Assets/Avatars
        return $"/Assets/Avatars/{avatarName}.png";
    }

    public Task<string> CacheAvatarAsync(string sourceUrl)
    {
        return Task.FromResult(sourceUrl);
    }
}

