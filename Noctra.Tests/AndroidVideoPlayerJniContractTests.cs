namespace Noctra.Tests;

public sealed class AndroidVideoPlayerJniContractTests
{
    [Fact]
    public void FindClassResults_AreReleasedAsGlobalReferences()
    {
        var source = ReadProjectFile(
            "Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("JNIEnv.DeleteGlobalRef(classPtr);", source, StringComparison.Ordinal);
        Assert.Contains("JNIEnv.DeleteGlobalRef(listClassPtr);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JNIEnv.DeleteLocalRef(classPtr);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JNIEnv.DeleteLocalRef(listClassPtr);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackAndCueElements_PromoteLocalReferencesBeforeReturningWrappers()
    {
        var source = ReadProjectFile(
            "Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("var globalPtr = JNIEnv.NewGlobalRef(itemPtr);", source, StringComparison.Ordinal);
        Assert.Contains("JniHandleOwnership.TransferGlobalRef", source, StringComparison.Ordinal);
        Assert.Contains("JNIEnv.DeleteLocalRef(itemPtr);", source, StringComparison.Ordinal);
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
