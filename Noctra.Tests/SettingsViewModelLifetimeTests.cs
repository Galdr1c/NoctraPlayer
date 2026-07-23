using Microsoft.Extensions.DependencyInjection;
using Noctra.Services;

namespace Noctra.Tests;

public sealed class SettingsViewModelLifetimeTests
{
    [Fact]
    public void ScopedServiceLease_DisposesResolvedTransientWithItsChildScope()
    {
        var services = new ServiceCollection();
        services.AddTransient<DisposableProbe>();
        using var provider = services.BuildServiceProvider();

        var lease = ScopedServiceLease<DisposableProbe>.Create(provider);
        var probe = lease.Service;

        lease.Dispose();

        Assert.True(probe.IsDisposed);
    }

    [Fact]
    public void SettingsSurfaces_OwnAndReleaseChildScopes()
    {
        var desktopMain = ReadProjectFile("Noctra.Avalonia", "MainWindow.axaml.cs");
        var mobileResolver = ReadProjectFile("Noctra.Mobile", "Services", "MobileViewModelResolver.cs");
        var mobileMain = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        Assert.Contains("CreateScope()", desktopMain, StringComparison.Ordinal);
        Assert.Contains("scope.ServiceProvider.GetRequiredService<Views.SettingsWindow>()", desktopMain, StringComparison.Ordinal);

        Assert.Contains("ScopedServiceLease<SettingsViewModel>", mobileResolver, StringComparison.Ordinal);
        Assert.Contains("CreateSettingsViewModelScope", mobileResolver, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSettingsViewModel()", mobileResolver, StringComparison.Ordinal);

        Assert.Contains("ReleaseSettingsViewModel();", mobileMain, StringComparison.Ordinal);
        Assert.Contains("CreateSettingsViewModelScope()", mobileMain, StringComparison.Ordinal);
        Assert.Contains("DisposeAsync()", mobileMain, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
