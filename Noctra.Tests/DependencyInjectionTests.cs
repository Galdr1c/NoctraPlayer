using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Noctra.Core.DependencyInjection;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddNoctraCoreServices_ResolvesSharedServiceGraph()
    {
        var root = Path.Combine(Path.GetTempPath(), "Noctra.Tests", Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IAppPathService>(new DesktopAppPathService(root, root));
        services.AddSingleton(new HttpClient());
        services.AddSingleton<ILicenseService, TestLicenseService>();
        services.AddSingleton<IDispatcherService, TestDispatcherService>();

        services.AddNoctraCoreServices();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<IDbContextFactory<AppDbContext>>());
        Assert.NotNull(provider.GetRequiredService<IProfileService>());
        Assert.NotNull(provider.GetRequiredService<IPlaylistService>());
        Assert.NotNull(provider.GetRequiredService<IEpgService>());
        Assert.NotNull(provider.GetRequiredService<IMetadataService>());
        Assert.NotNull(provider.GetRequiredService<ISettingsService>());
        Assert.Same(
            provider.GetRequiredService<IDatabaseWorkScheduler>(),
            provider.GetRequiredService<IDatabaseWorkScheduler>());
        Assert.Same(
            DatabaseWorkScheduler.Shared,
            provider.GetRequiredService<IDatabaseWorkScheduler>());
        Assert.NotNull(provider.GetRequiredService<IContentQueryService>());
    }

    [Fact]
    public void DesktopExit_StopsDatabaseSchedulerBeforeDisposingServiceProvider()
    {
        var appSource = File.ReadAllText(ProjectSource("Noctra.Avalonia", "App.axaml.cs"));
        var exitHandler = appSource[appSource.IndexOf("desktop.Exit +=", StringComparison.Ordinal)..];
        var schedulerDispose = exitHandler.IndexOf("databaseWorkScheduler.Dispose()", StringComparison.Ordinal);
        var providerDispose = exitHandler.IndexOf("disposableServices.Dispose()", StringComparison.Ordinal);

        Assert.True(schedulerDispose >= 0, "Desktop exit must stop the process database scheduler.");
        Assert.True(
            providerDispose > schedulerDispose,
            "Database scheduler must stop before dependent services are disposed.");
    }

    [Fact]
    public void AndroidRegistration_SelectsMobileSqliteProfileBeforeCoreServices()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Android",
            "DependencyInjection",
            "AndroidServiceCollectionExtensions.cs"));
        var mobileProfile = source.IndexOf(
            "services.AddSingleton(SqliteConnectionTuningOptions.Mobile)",
            StringComparison.Ordinal);
        var coreServices = source.IndexOf("services.AddNoctraCoreServices()", StringComparison.Ordinal);

        Assert.True(mobileProfile >= 0, "Android DI must register the mobile SQLite profile.");
        Assert.True(
            coreServices > mobileProfile,
            "The mobile SQLite profile must be registered before core services add the desktop default.");
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[]
            {
                AppContext.BaseDirectory,
                "..", "..", "..", ".."
            }.Concat(segments).ToArray()));

    private sealed class TestDispatcherService : IDispatcherService
    {
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
        public Task InvokeAsync(Func<Task> func) => func();
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
    }

    private sealed class TestLicenseService : ILicenseService
    {
        public bool IsPremium => true;
        public bool CanUpgradeToPremium => false;
        public bool IsEditionLockedPremium => false;
        public SubscriptionTier CurrentTier => SubscriptionTier.Premium;
        public event Action? SubscriptionChanged;

        public void ActivatePremium() => SubscriptionChanged?.Invoke();
        public void DeactivatePremium() => SubscriptionChanged?.Invoke();
        public SubscriptionInfo GetCurrentSubscription() => new() { Tier = CurrentTier };
        public bool IsFeatureAvailable(string featureName) => true;
        public bool IsWithinLimit(string limitName, int currentCount) => true;
        public int GetLimit(string limitName) => int.MaxValue;
        public Task<bool> StartPurchaseFlowAsync(SubscriptionTier targetTier) => Task.FromResult(false);
        public Task RefreshSubscriptionStatusAsync() => Task.CompletedTask;
        public void SetTierForTesting(SubscriptionTier tier) { }
    }
}
