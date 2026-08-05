using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// <see cref="IProfilePinService"/> — hızlı SHA-256 PIN verifier'ını servis
/// katmanına bağlar. Platform bağımlılığı yoktur; masaüstü ve Android aynı
/// doğrulayıcıyı kullanır.
/// </summary>
public sealed class ProfilePinService : IProfilePinService
{
    public string CreateVerifier(string pin) => ProfilePinVerifier.Create(pin);

    public bool Verify(string pin, string storedVerifier) => ProfilePinVerifier.Verify(pin, storedVerifier);
}
