using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Noctra.Services.Interfaces;

namespace Noctra.Core.Services;

public sealed class PerformanceTraceService : IPerformanceTraceService, IDisposable
{
    private static readonly Stopwatch ProcessClock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private readonly string _logFilePath;
    private int _activeOperations;
    private bool _disposed;

    public static IPerformanceTraceService? Shared { get; private set; }

    public PerformanceTraceService()
    {
        var logDir = ResolveLogDirectory();

        Directory.CreateDirectory(logDir);
        _logFilePath = Path.Combine(logDir, $"noctra-perf-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writerTask = Task.Run(WriteLoopAsync);
        Shared = this;

        Event("TRACE", "START", $"file={_logFilePath}");
    }

    public string LogFilePath => _logFilePath;

    public IDisposable BeginOperation(string area, string name, string? detail = null)
    {
        var active = Interlocked.Increment(ref _activeOperations);
        var started = Stopwatch.GetTimestamp();
        Write(area, "BEGIN", name, 0, active, detail);
        return new Scope(this, area, name, started);
    }

    public void Event(string area, string name, string? detail = null)
    {
        Write(area, "EVENT", name, 0, Volatile.Read(ref _activeOperations), detail);
    }

    public void Counter(string area, string name, long value, string? detail = null)
    {
        Write(area, "COUNT", name, value, Volatile.Read(ref _activeOperations), detail);
    }

    private void EndOperation(string area, string name, long started)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var active = Math.Max(0, Interlocked.Decrement(ref _activeOperations));
        Write(area, "END", name, elapsedMs, active, null);
    }

    private void Write(string area, string kind, string name, double value, int active, string? detail)
    {
        if (_disposed)
        {
            return;
        }

        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd HH:mm:ss.fff} | +{1,8:F3}s | T{2,2} | active={3,2} | {4,-10} | {5,-5} | {6,-42} | value={7,8:F2} | {8}",
            DateTime.Now,
            ProcessClock.Elapsed.TotalSeconds,
            Environment.CurrentManagedThreadId,
            active,
            area,
            kind,
            name,
            value,
            Sanitize(detail));
        _queue.Enqueue(line);
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await using var stream = new FileStream(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream) { AutoFlush = true };

            while (!_cts.IsCancellationRequested)
            {
                await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
                while (_queue.TryDequeue(out var line))
                {
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Tracing must never crash the app.
        }
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string ResolveLogDirectory()
    {
        var configuredDir = Environment.GetEnvironmentVariable("NOCTRA_PERF_TRACE_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            return configuredDir;
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Directory.GetCurrentDirectory());
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            return Path.Combine(repoRoot, "artifacts", "perf-trace");
        }

        return AppPaths.PerfTraceDirectory;
    }

    private static string? FindRepoRoot(string startPath)
    {
        try
        {
            var directory = Directory.Exists(startPath)
                ? new DirectoryInfo(startPath)
                : new FileInfo(startPath).Directory;

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }
        catch
        {
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Event("TRACE", "STOP");
        _disposed = true;
        Shared = null;
        var spinUntil = DateTime.UtcNow.AddMilliseconds(250);
        while (!_queue.IsEmpty && DateTime.UtcNow < spinUntil)
        {
            Thread.Sleep(10);
        }

        _cts.Cancel();
        _cts.Dispose();
        _signal.Dispose();
    }

    private sealed class Scope : IDisposable
    {
        private readonly PerformanceTraceService _owner;
        private readonly string _area;
        private readonly string _name;
        private readonly long _started;
        private int _disposed;

        public Scope(PerformanceTraceService owner, string area, string name, long started)
        {
            _owner = owner;
            _area = area;
            _name = name;
            _started = started;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _owner.EndOperation(_area, _name, _started);
        }
    }
}
