using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public sealed class ProfileAccessService : IProfileAccessService
{
    public async Task<ProfileAccessGrant?> TryAcquireAsync(
        Profile profile,
        ProfileAccessPurpose purpose,
        Func<Profile, ProfileAccessPurpose, Task<bool>> verifyPin)
    {
        if (profile == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(profile.PinHash))
        {
            if (verifyPin == null)
            {
                return null;
            }

            if (!await verifyPin(profile, purpose))
            {
                return null;
            }
        }

        return ProfileAccessGrant.Create(profile.Id, purpose);
    }

    public void ValidateOrThrow(ProfileAccessGrant? grant, int profileId, ProfileAccessPurpose purpose)
    {
        if (grant == null || !grant.Authorizes(profileId, purpose))
        {
            throw new ProfileAccessDeniedException(
                $"Profil {profileId} için '{purpose}' yetkisi geçerli değil.");
        }
    }
}
