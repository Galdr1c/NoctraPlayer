namespace Noctra.Tests;

public sealed class MobileSettingsStartupPerformanceTests
{
    [Theory]
    [InlineData("ScanChannelListStatsCoreAsync")]
    [InlineData("ScanEpgStatsCoreAsync")]
    public void InitialSettingsStatistics_MoveSqliteWorkOffTheCallerThread(
        string methodName)
    {
        var source = ReadProjectFile(
            "Noctra.Core",
            "ViewModels",
            "SettingsViewModel.cs");
        var method = ExtractMethod(source, methodName);

        var taskRunIndex = method.IndexOf("Task.Run", StringComparison.Ordinal);
        var databaseIndex = method.IndexOf(
            "CreateDbContextAsync",
            StringComparison.Ordinal);

        Assert.True(
            taskRunIndex >= 0,
            $"{methodName} must offload SQLite work with Task.Run.");
        Assert.True(
            databaseIndex > taskRunIndex,
            $"{methodName} must create and query its DbContext inside the background task.");
    }

    [Fact]
    public void StatisticsScans_AreSerializedAndDrainedDuringDisposal()
    {
        var source = ReadProjectFile(
            "Noctra.Core",
            "ViewModels",
            "SettingsViewModel.cs");
        var channelScan = ExtractMethod(source, "ScanChannelListStatsCoreAsync");
        var epgScan = ExtractMethod(source, "ScanEpgStatsCoreAsync");
        var dispose = ExtractMethodByDeclaration(
            source,
            "public async ValueTask DisposeAsync");

        Assert.Contains(
            "_initialStatisticsTask = LoadInitialStatisticsAsync();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_statisticsScanGate.WaitAsync",
            channelScan,
            StringComparison.Ordinal);
        Assert.Contains(
            "_statisticsScanGate.WaitAsync",
            epgScan,
            StringComparison.Ordinal);
        Assert.Contains(
            "await _initialStatisticsTask",
            dispose,
            StringComparison.Ordinal);
        Assert.Contains(
            "await _statisticsScanGate.WaitAsync",
            dispose,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ScanChannelListStatsCoreAsync")]
    [InlineData("ScanEpgStatsCoreAsync")]
    public void StatisticsScans_DropResultsWhenTheProfileSelectionChanges(
        string methodName)
    {
        var source = ReadProjectFile(
            "Noctra.Core",
            "ViewModels",
            "SettingsViewModel.cs");
        var method = ExtractMethod(source, methodName);

        Assert.Contains(
            "SelectionStillMatches",
            method,
            StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var declaration = $"private async Task {methodName}";
        return ExtractMethodByDeclaration(source, declaration);
    }

    private static string ExtractMethodByDeclaration(
        string source,
        string declaration)
    {
        var nameIndex = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {declaration}.");

        var openingBrace = source.IndexOf('{', nameIndex);
        Assert.True(
            openingBrace >= 0,
            $"Could not find the body of {declaration}.");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (depth == 0)
            {
                return source[openingBrace..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not parse {declaration}.");
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
