namespace Noctra.Tests;

public sealed class PooledDbContextFactoryRegistrationContractTests
{
    [Fact]
    public void CoreServices_RegisterBoundedPooledDbContextFactory()
    {
        var source = ReadProjectFile(
            "Noctra.Core",
            "DependencyInjection",
            "ServiceCollectionExtensions.cs");

        Assert.Contains(
            "AddPooledDbContextFactory<AppDbContext>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("poolSize:", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "services.AddDbContextFactory<AppDbContext>",
            source,
            StringComparison.Ordinal);
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
