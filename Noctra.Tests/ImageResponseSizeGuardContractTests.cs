namespace Noctra.Tests;

public sealed class ImageResponseSizeGuardContractTests
{
    [Fact]
    public void ImagePipelines_RejectOversizedResponsesBeforeDecode()
    {
        var mobile = ReadProjectFile("Noctra.Mobile", "Controls", "MobileRemoteImage.cs");
        var desktop = ReadProjectFile("Noctra.Avalonia", "Controls", "RemoteImage.cs");

        foreach (var source in new[] { mobile, desktop })
        {
            Assert.Contains("MaxImageResponseBytes", source, StringComparison.Ordinal);
            Assert.Contains("ContentLength", source, StringComparison.Ordinal);
            Assert.Contains("BoundedResponseReader.CopyToAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("stream.CopyToAsync(memory, bodyCts.Token)", source, StringComparison.Ordinal);
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
