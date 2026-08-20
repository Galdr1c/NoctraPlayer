namespace Noctra.Tests;

public sealed class AndroidMainThreadHandlerContractTests
{
    [Fact]
    public void AndroidVideoPlayer_UsesCachedMainHandlerForPosts()
    {
        var source = ReadProjectFile("Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("static readonly Handler MainHandler", source, StringComparison.Ordinal);
        Assert.Contains("MainHandler.Post(action)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Handler(Looper.MainLooper!).Post(action)", source, StringComparison.Ordinal);
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
