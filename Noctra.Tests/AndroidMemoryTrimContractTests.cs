namespace Noctra.Tests;

public sealed class AndroidMemoryTrimContractTests
{
    [Fact]
    public void AndroidActivity_TrimsMobileImageCacheOnMemoryPressure()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");
        var image = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");

        Assert.Contains("OnTrimMemory", activity, StringComparison.Ordinal);
        Assert.Contains("TrimImageCaches", activity, StringComparison.Ordinal);
        Assert.Contains("public static void TrimImageCaches", image, StringComparison.Ordinal);
        Assert.Contains("Cache.Clear()", image, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindSolutionRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "NoctraPlayer.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate NoctraPlayer.sln.");
    }
}
