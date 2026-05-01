namespace Noctra.Services.Interfaces;

public interface IPackageIdentityService
{
    bool IsPackaged { get; }

    string RuntimeMode { get; }

    string? PackageFullName { get; }

    string? PackageFamilyName { get; }
}
