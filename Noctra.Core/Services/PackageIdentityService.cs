using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public sealed class PackageIdentityService : IPackageIdentityService
{
    private const int AppModelErrorNoPackage = 15700;

    public PackageIdentityService()
    {
        PackageFullName = TryGetPackageValue(GetCurrentPackageFullName);
        PackageFamilyName = TryGetPackageValue(GetCurrentPackageFamilyName);
        IsPackaged = !string.IsNullOrWhiteSpace(PackageFullName);
        RuntimeMode = IsPackaged ? "Packaged" : "Unpackaged";
    }

    public bool IsPackaged { get; }

    public string RuntimeMode { get; }

    public string? PackageFullName { get; }

    public string? PackageFamilyName { get; }

    private static string? TryGetPackageValue(PackageQueryDelegate queryDelegate)
    {
        try
        {
            var length = 0;
            var result = queryDelegate(ref length, null);
            if (result == AppModelErrorNoPackage)
            {
                return null;
            }

            if (result != 122 && result != 0)
            {
                throw new Win32Exception(result);
            }

            if (length <= 0)
            {
                return null;
            }

            var builder = new StringBuilder(length);
            result = queryDelegate(ref length, builder);
            if (result == AppModelErrorNoPackage)
            {
                return null;
            }

            if (result != 0)
            {
                throw new Win32Exception(result);
            }

            return builder.ToString();
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, StringBuilder? packageFamilyName);

    private delegate int PackageQueryDelegate(ref int valueLength, StringBuilder? value);
}
