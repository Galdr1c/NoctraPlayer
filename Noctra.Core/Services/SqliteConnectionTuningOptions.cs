namespace Noctra.Services;

public sealed record SqliteConnectionTuningOptions(
    int CacheSizePragmaValue,
    long MemoryMappedIoBytes,
    string ProfileName)
{
    public static SqliteConnectionTuningOptions Desktop { get; } =
        new(-64000, 268435456, "desktop");

    public static SqliteConnectionTuningOptions Mobile { get; } =
        new(-32000, 134217728, "mobile");
}
