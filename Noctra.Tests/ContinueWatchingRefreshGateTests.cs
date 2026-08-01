using System.Reflection;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class ContinueWatchingRefreshGateTests
{
    [Fact]
    public void Playback_suppresses_rail_refresh_until_session_ends()
    {
        var gateType = typeof(MainViewModel).Assembly.GetType(
            "Noctra.ViewModels.ContinueWatchingRefreshGate");

        Assert.NotNull(gateType);

        var gate = Activator.CreateInstance(gateType!);
        Assert.NotNull(gate);

        var begin = gateType!.GetMethod(
            "BeginPlayback",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var request = gateType.GetMethod(
            "RequestRefresh",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var end = gateType.GetMethod(
            "EndPlayback",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(begin);
        Assert.NotNull(request);
        Assert.NotNull(end);

        begin!.Invoke(gate, null);

        Assert.False((bool)request!.Invoke(gate, null)!);
        Assert.True((bool)end!.Invoke(gate, null)!);
        Assert.True((bool)request.Invoke(gate, null)!);
    }
}
