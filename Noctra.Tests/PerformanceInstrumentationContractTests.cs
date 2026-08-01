using System.Collections;
using System.Reflection;
using System.Text.Json;
using Noctra.Diagnostics;
using Noctra.Models;
using Noctra.Data;
using Microsoft.EntityFrameworkCore;

namespace Noctra.Tests;

public sealed class PerformanceInstrumentationContractTests
{
    [Fact]
    public void NullPerformanceProbe_IsDisabledAndAcceptsMarks()
    {
        var coreAssembly = typeof(Channel).Assembly;
        var probeType = coreAssembly.GetType("Noctra.Diagnostics.NullPerformanceProbe");

        Assert.NotNull(probeType);

        var instance = probeType!.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        Assert.NotNull(instance);
        Assert.False((bool)probeType.GetProperty("IsEnabled")!.GetValue(instance)!);

        var mark = probeType.GetMethod("Mark", [typeof(string), typeof(long), typeof(string)]);
        Assert.NotNull(mark);
        mark!.Invoke(instance, ["test.mark", 42L, "scope-1"]);
    }

    [Fact]
    public void SyntheticXtreamPlan_ProducesDeterministicTenThousandItemMix()
    {
        var performanceAssembly = Assembly.Load("Noctra.Performance");
        var planType = performanceAssembly.GetType("Noctra.Performance.SyntheticXtreamPlan");

        Assert.NotNull(planType);

        var plan = Activator.CreateInstance(planType!, [10_000, 100]);
        Assert.NotNull(plan);
        Assert.Equal(4_000, ReadInt(planType!, plan!, "LiveCount"));
        Assert.Equal(3_000, ReadInt(planType!, plan!, "VodCount"));
        Assert.Equal(3_000, ReadInt(planType!, plan!, "SeriesCount"));

        var categories = Assert.IsAssignableFrom<IEnumerable>(
            planType!.GetMethod("GetCategories")!.Invoke(plan, null));
        Assert.Equal(100, categories.Cast<object>().Count());

        var createItem = planType.GetMethod("CreateItem");
        Assert.NotNull(createItem);
        var first = createItem!.Invoke(plan, ["live", 0]);
        var repeated = createItem.Invoke(plan, ["live", 0]);
        Assert.Equal(first, repeated);
    }

    [Fact]
    public void SyntheticXtreamResponses_MatchTheApplicationContract()
    {
        var assembly = Assembly.Load("Noctra.Performance");
        var planType = assembly.GetType("Noctra.Performance.SyntheticXtreamPlan");
        var factoryType = assembly.GetType("Noctra.Performance.SyntheticXtreamResponseFactory");
        Assert.NotNull(planType);
        Assert.NotNull(factoryType);

        var plan = Activator.CreateInstance(planType!, [10_000, 100]);
        var factory = Activator.CreateInstance(factoryType!, [plan]);
        Assert.NotNull(factory);

        var authPayload = factoryType!.GetMethod("CreateAuthPayload")!.Invoke(factory, null);
        using var authJson = JsonDocument.Parse(JsonSerializer.Serialize(authPayload));
        Assert.Equal(
            "Active",
            authJson.RootElement.GetProperty("user_info").GetProperty("status").GetString());

        var liveCategories = Assert.IsAssignableFrom<IEnumerable>(
            factoryType.GetMethod("CreateCategories")!.Invoke(factory, ["live"]));
        Assert.Equal(40, liveCategories.Cast<object>().Count());

        var seriesItems = Assert.IsAssignableFrom<IEnumerable>(
            factoryType.GetMethod("CreateItems")!.Invoke(factory, ["series", "71"]));
        var materialized = seriesItems.Cast<object>().ToList();
        Assert.Equal(100, materialized.Count);

        using var itemJson = JsonDocument.Parse(JsonSerializer.Serialize(materialized[0]));
        Assert.True(itemJson.RootElement.TryGetProperty("series_id", out _));
        Assert.Equal("71", itemJson.RootElement.GetProperty("category_id").GetString());
    }

    [Fact]
    public async Task SyntheticXtreamServer_ServesAuthenticationCategoriesAndItems()
    {
        var assembly = Assembly.Load("Noctra.Performance");
        var serverType = assembly.GetType("Noctra.Performance.SyntheticXtreamServer");
        Assert.NotNull(serverType);

        var server = Activator.CreateInstance(serverType!, [10_000, 0]);
        Assert.NotNull(server);
        await Assert.IsAssignableFrom<Task>(
            serverType!.GetMethod("StartAsync")!.Invoke(server, null));

        try
        {
            var baseAddress = Assert.IsType<Uri>(
                serverType.GetProperty("BaseAddress")!.GetValue(server));
            using var client = new HttpClient { BaseAddress = baseAddress };

            using var auth = JsonDocument.Parse(await client.GetStringAsync(
                "player_api.php?username=benchmark&password=benchmark"));
            Assert.Equal(
                "Active",
                auth.RootElement.GetProperty("user_info").GetProperty("status").GetString());

            using var categories = JsonDocument.Parse(await client.GetStringAsync(
                "player_api.php?username=benchmark&password=benchmark&action=get_series_categories"));
            Assert.Equal(30, categories.RootElement.GetArrayLength());

            using var items = JsonDocument.Parse(await client.GetStringAsync(
                "player_api.php?username=benchmark&password=benchmark&action=get_series&category_id=71"));
            Assert.Equal(100, items.RootElement.GetArrayLength());
        }
        finally
        {
            await Assert.IsAssignableFrom<IAsyncDisposable>(server).DisposeAsync();
        }
    }

    [Fact]
    public void PerformanceCommandLine_ParsesServeOptions()
    {
        var assembly = Assembly.Load("Noctra.Performance");
        var parserType = assembly.GetType("Noctra.Performance.PerformanceCommandLine");
        Assert.NotNull(parserType);

        var options = parserType!.GetMethod("Parse")!.Invoke(
            null,
            [new[] { "serve", "--count", "50000", "--port", "18765" }]);
        Assert.NotNull(options);
        var optionsType = options!.GetType();
        Assert.Equal("serve", optionsType.GetProperty("Command")!.GetValue(options));
        Assert.Equal(50_000, optionsType.GetProperty("Count")!.GetValue(options));
        Assert.Equal(18_765, optionsType.GetProperty("Port")!.GetValue(options));
    }

    [Fact]
    public void PerformanceTrace_ForwardsMarksToTheInstalledProbe()
    {
        var traceType = typeof(IPerformanceProbe).Assembly.GetType("Noctra.Diagnostics.PerformanceTrace");
        Assert.NotNull(traceType);
        var recorder = new RecordingPerformanceProbe();
        var probeProperty = traceType!.GetProperty("Probe", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(probeProperty);

        probeProperty!.SetValue(null, recorder);
        try
        {
            traceType.GetMethod("Mark")!.Invoke(null, ["profile.tap", 17L, "profile-3"]);
            // Other integration tests may emit their own marks while the
            // process-wide probe is installed. Verify forwarding of this
            // event without assuming it is the only concurrent event.
            Assert.Contains(("profile.tap", 17L, "profile-3"), recorder.Events);
        }
        finally
        {
            probeProperty.SetValue(null, NullPerformanceProbe.Instance);
        }
    }

    [Fact]
    public void JsonLinesPerformanceProbe_WritesTimestampThreadAndGcSnapshot()
    {
        var probeType = typeof(IPerformanceProbe).Assembly.GetType(
            "Noctra.Diagnostics.JsonLinesPerformanceProbe");
        Assert.NotNull(probeType);

        using var output = new StringWriter();
        using var probe = Assert.IsAssignableFrom<IDisposable>(
            Activator.CreateInstance(probeType!, [output]));

        probeType!.GetMethod("Mark")!.Invoke(probe, ["sqlite.batch.commit", 500L, "playlist-7"]);

        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("sqlite.batch.commit", root.GetProperty("name").GetString());
        Assert.Equal(500, root.GetProperty("value").GetInt64());
        Assert.Equal("playlist-7", root.GetProperty("scope").GetString());
        Assert.True(root.GetProperty("timestampTicks").GetInt64() > 0);
        Assert.True(root.GetProperty("timestampFrequency").GetInt64() > 0);
        Assert.True(root.GetProperty("threadId").GetInt32() > 0);
        Assert.True(root.GetProperty("managedBytes").GetInt64() >= 0);
        Assert.True(root.GetProperty("gen0Collections").GetInt32() >= 0);
        Assert.True(root.GetProperty("gen1Collections").GetInt32() >= 0);
        Assert.True(root.GetProperty("gen2Collections").GetInt32() >= 0);
    }

    [Fact]
    public void AppDbContext_HasPlaylistStreamUrlAndSeriesPlaylistIndexes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new AppDbContext(options);

        var channelIndexes = context.Model.FindEntityType(typeof(Channel))!.GetIndexes();
        Assert.Contains(channelIndexes, index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Channel.PlaylistId), nameof(Channel.StreamUrl) }));

        var seriesIndexes = context.Model.FindEntityType(typeof(Series))!.GetIndexes();
        Assert.Contains(seriesIndexes, index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Series.PlaylistId) }));
    }

    private static int ReadInt(Type type, object instance, string propertyName)
        => (int)type.GetProperty(propertyName)!.GetValue(instance)!;

    private sealed class RecordingPerformanceProbe : IPerformanceProbe
    {
        public bool IsEnabled => true;
        public List<(string Name, long Value, string? Scope)> Events { get; } = [];

        public void Mark(string name, long value = 0, string? scope = null)
            => Events.Add((name, value, scope));
    }
}
