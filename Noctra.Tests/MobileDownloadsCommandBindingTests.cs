namespace Noctra.Tests;

public sealed class MobileDownloadsCommandBindingTests
{
    [Fact]
    public void DownloadActionsResolveCommandsFromDownloadsViewModel()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileDownloadsView.axaml");

        Assert.Contains(
            "{Binding $parent[UserControl].((vm:MainViewModel)DataContext).TogglePauseDownloadCommand}",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "{Binding $parent[UserControl].((vm:MainViewModel)DataContext).CancelDownloadCommand}",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "$parent[ItemsControl].((vm:MainViewModel)DataContext).TogglePauseDownloadCommand",
            view,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
