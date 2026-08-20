namespace Noctra.Tests;

public sealed class DesktopImageDispatchPriorityContractTests
{
    [Fact]
    public void DesktopRemoteImage_DoesNotPostCompletionsAtRenderPriority()
    {
        var source = ReadProjectFile("Noctra.Avalonia", "Controls", "RemoteImage.cs");

        Assert.DoesNotContain("DispatcherPriority.Render", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", source, StringComparison.Ordinal);
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
