using System.Reflection;
using Noctra.Core.Services;

namespace Noctra.Tests;

public class CacheServiceTests
{
    [Fact]
    public void CacheDirectories_ShouldNotIncludeSettingsDirectory()
    {
        var field = typeof(CacheService).GetField("CacheDirectories", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        var directories = Assert.IsType<string[]>(field!.GetValue(null));

        Assert.DoesNotContain("Settings", directories, StringComparer.OrdinalIgnoreCase);
    }
}
