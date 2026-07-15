using System.Text.Json;

namespace Noctra.Performance;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        PerformanceCommandLineOptions options;
        try
        {
            options = PerformanceCommandLine.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            WriteUsage();
            return 2;
        }

        if (options.Command == "describe")
        {
            var plan = new SyntheticXtreamPlan(options.Count, 100);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                plan.TotalCount,
                plan.LiveCount,
                plan.VodCount,
                plan.SeriesCount,
                plan.CategoryCount
            }));
            return 0;
        }

        if (options.Command != "serve")
        {
            WriteUsage();
            return options.Command == "help" ? 0 : 2;
        }

        await using var server = new SyntheticXtreamServer(options.Count, options.Port);
        await server.StartAsync().ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "ready",
            url = server.BaseAddress,
            count = options.Count
        }));

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        return 0;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Noctra.Performance serve --count 10000 --port 18765");
        Console.WriteLine("  Noctra.Performance describe --count 10000");
    }
}
