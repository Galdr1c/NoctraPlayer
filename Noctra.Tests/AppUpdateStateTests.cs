using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class AppUpdateStateTests
{
    [Fact]
    public void SharedPublisher_PreservesOrderedTransitionsAndPayloads()
    {
        var publisher = new UpdateStatePublisher();
        var sender = new object();
        var observed = new List<(object? Sender, UpdateStateChangedEventArgs Args)>();
        publisher.UpdateStateChanged +=
            (eventSender, args) => observed.Add((eventSender, args));

        publisher.Publish(sender, UpdateCheckStatus.Downloading, progressPercent: 25d);
        publisher.Publish(sender, UpdateCheckStatus.Downloaded);
        publisher.Publish(sender, UpdateCheckStatus.UpToDate);

        Assert.Equal(
            new[]
            {
                UpdateCheckStatus.Downloading,
                UpdateCheckStatus.Downloaded,
                UpdateCheckStatus.UpToDate
            },
            observed.Select(item => item.Args.Status));
        Assert.All(observed, item => Assert.Same(sender, item.Sender));
        Assert.Equal(25d, observed[0].Args.ProgressPercent);
        Assert.Null(observed[1].Args.ProgressPercent);
    }

    [Theory]
    [InlineData(UpdateCheckStatus.Canceled, null)]
    [InlineData(UpdateCheckStatus.Error, "network failed")]
    public void SharedPublisher_PublishesTerminalStatePayload(
        UpdateCheckStatus status,
        string? errorMessage)
    {
        var publisher = new UpdateStatePublisher();
        UpdateStateChangedEventArgs? observed = null;
        publisher.UpdateStateChanged += (_, args) => observed = args;

        publisher.Publish(this, status, errorMessage: errorMessage);

        Assert.NotNull(observed);
        Assert.Equal(status, observed!.Status);
        Assert.Equal(errorMessage, observed.ErrorMessage);
    }

    [Fact]
    public void ByteProgressEvent_ComputesDisplayPercentage()
    {
        var args = new UpdateStateChangedEventArgs(bytesDownloaded: 25, totalBytes: 40);

        Assert.Equal(UpdateCheckStatus.Downloading, args.Status);
        Assert.Equal(25, args.BytesDownloaded);
        Assert.Equal(40, args.TotalBytes);
        Assert.Equal(62.5d, args.ProgressPercent);
    }

    [Fact]
    public async Task NoOpService_AlwaysReportsUnsupportedWithoutStateTransitions()
    {
        var service = new NoOpUpdateService();
        var transitions = new List<UpdateCheckStatus>();
        service.UpdateStateChanged += (_, args) => transitions.Add(args.Status);

        Assert.Equal(UpdateCheckStatus.Unsupported, (await service.CheckAsync()).Status);
        Assert.False(await service.StartUpdateAsync());
        Assert.False(await service.CompleteUpdateAsync());
        Assert.Equal(UpdateCheckStatus.Unsupported, (await service.CheckPendingUpdateAsync()).Status);
        Assert.Empty(transitions);
    }

    [Fact]
    public void DesktopPlatformService_UsesTheSharedStatePublisher()
    {
        var windows = ReadProjectFile("Noctra.Avalonia", "Services", "MicrosoftStoreUpdateService.cs");

        Assert.Contains("UpdateStatePublisher", windows, StringComparison.Ordinal);
        Assert.Contains("_statePublisher.Publish(this", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateStateChanged?.Invoke", windows, StringComparison.Ordinal);
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
}
