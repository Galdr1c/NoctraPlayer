using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Noctra.Core.Advertising;

namespace Noctra.Tests.Advertising;

public sealed class RemoteAdvertisingConfigRefreshTests
{
    [Fact]
    public async Task RefreshAsync_RaisesOptionsChanged_WhenConfigParses()
    {
        var previous = Environment.GetEnvironmentVariable("NOCTRA_ADVERTISING_CONFIG_URL");
        try
        {
            var port = GetFreePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            const string json = """{ "movies": { "enabled": false, "spacing": 14, "max": 2 } }""";
            _ = Task.Run(() =>
            {
                var context = listener.GetContext();
                var body = Encoding.UTF8.GetBytes(json);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                context.Response.OutputStream.Write(body, 0, body.Length);
                context.Response.OutputStream.Close();
            });

            Environment.SetEnvironmentVariable(
                "NOCTRA_ADVERTISING_CONFIG_URL",
                $"http://127.0.0.1:{port}/config.json");

            var service = new RemoteAdvertisingConfigService(new HttpClient());
            var changedCount = 0;
            service.OptionsChanged += (_, _) => changedCount++;

            await service.RefreshAsync();

            Assert.Equal(1, changedCount);
            Assert.False(service.CurrentOptions.Movies.Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "NOCTRA_ADVERTISING_CONFIG_URL",
                previous);
        }
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}