namespace Noctra.Performance;

public static class PerformanceCommandLine
{
    public static PerformanceCommandLineOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "help";
        var count = 10_000;
        var port = 18_765;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--count" when index + 1 < args.Length:
                    count = ParsePositiveInt(args[++index], "--count");
                    break;
                case "--port" when index + 1 < args.Length:
                    port = ParsePositiveInt(args[++index], "--port");
                    if (port > 65_535)
                    {
                        throw new ArgumentOutOfRangeException(nameof(args), "--port must be at most 65535.");
                    }
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}", nameof(args));
            }
        }

        return new PerformanceCommandLineOptions(command, count, port);
    }

    private static int ParsePositiveInt(string value, string option)
        => int.TryParse(
               value,
               System.Globalization.NumberStyles.None,
               System.Globalization.CultureInfo.InvariantCulture,
               out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{option} requires a positive integer.", nameof(value));
}

public sealed record PerformanceCommandLineOptions(
    string Command,
    int Count,
    int Port);
