namespace Noctra.Tests;

public sealed class ImageFailureCacheContractTests
{
    [Fact]
    public void ImagePipelines_BoundAndSweepFailedUrlCooldowns()
    {
        var mobile = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");
        var desktop = ReadProjectFile("Noctra.Avalonia", "Controls", "RemoteImage.cs");

        foreach (var source in new[] { mobile, desktop })
        {
            Assert.Contains("MaxFailedImageEntries", source, StringComparison.Ordinal);
            Assert.Contains("TrimFailedUntilUtc", source, StringComparison.Ordinal);
            Assert.Contains("FailedUntilUtc.Count", source, StringComparison.Ordinal);
            Assert.Contains("ICollection<KeyValuePair<string, DateTime>>", source, StringComparison.Ordinal);
            Assert.Contains("TryRemove", source, StringComparison.Ordinal);
        }
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
