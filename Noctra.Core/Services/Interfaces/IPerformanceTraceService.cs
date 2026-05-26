namespace Noctra.Services.Interfaces;

public interface IPerformanceTraceService
{
    string LogFilePath { get; }

    IDisposable BeginOperation(string area, string name, string? detail = null);

    void Event(string area, string name, string? detail = null);

    void Counter(string area, string name, long value, string? detail = null);
}
