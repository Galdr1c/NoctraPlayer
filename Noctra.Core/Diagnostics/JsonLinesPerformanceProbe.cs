using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Noctra.Diagnostics;

public sealed class JsonLinesPerformanceProbe : IPerformanceProbe, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly TextWriter _writer;
    private bool _disposed;
    private int _eventsSinceFlush;

    public JsonLinesPerformanceProbe(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public JsonLinesPerformanceProbe(string path)
        : this(CreateWriter(path))
    {
    }

    public bool IsEnabled => !_disposed;

    public void Mark(string name, long value = 0, string? scope = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A performance mark must have a name.", nameof(name));
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var snapshot = new PerformanceEvent(
                name,
                value,
                scope,
                Stopwatch.GetTimestamp(),
                Stopwatch.Frequency,
                Environment.CurrentManagedThreadId,
                GC.GetTotalMemory(forceFullCollection: false),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));

            _writer.WriteLine(JsonSerializer.Serialize(snapshot, JsonOptions));
            _eventsSinceFlush++;
            if (_eventsSinceFlush >= 32 ||
                name.EndsWith(".start", StringComparison.Ordinal) ||
                name.EndsWith(".complete", StringComparison.Ordinal) ||
                name.EndsWith(".destroy", StringComparison.Ordinal))
            {
                _writer.Flush();
                _eventsSinceFlush = 0;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    private static TextWriter CreateWriter(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private sealed record PerformanceEvent(
        string Name,
        long Value,
        string? Scope,
        long TimestampTicks,
        long TimestampFrequency,
        int ThreadId,
        long ManagedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections);
}
