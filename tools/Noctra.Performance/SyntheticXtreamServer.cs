using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Noctra.Performance;

public sealed class SyntheticXtreamServer : IAsyncDisposable
{
    private readonly int _totalCount;
    private readonly int _requestedPort;
    private TcpListener? _listener;
    private CancellationTokenSource? _serverCancellation;
    private Task? _serverLoop;

    public SyntheticXtreamServer(int totalCount, int port)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalCount);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        _totalCount = totalCount;
        _requestedPort = port;
    }

    public Uri? BaseAddress { get; private set; }

    public Task StartAsync()
    {
        if (_listener is not null)
        {
            return Task.CompletedTask;
        }

        var listener = new TcpListener(IPAddress.Loopback, _requestedPort);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var plan = new SyntheticXtreamPlan(_totalCount, 100);
        var responses = new SyntheticXtreamResponseFactory(plan);
        var cancellation = new CancellationTokenSource();

        _listener = listener;
        _serverCancellation = cancellation;
        _serverLoop = RunServerLoopAsync(listener, plan, responses, cancellation.Token);
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener is null)
        {
            return;
        }

        _serverCancellation?.Cancel();
        _listener.Stop();
        if (_serverLoop is not null)
        {
            try
            {
                await _serverLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
        }

        _serverCancellation?.Dispose();
        _listener = null;
        _serverCancellation = null;
        _serverLoop = null;
        BaseAddress = null;
    }

    private static async Task RunServerLoopAsync(
        TcpListener listener,
        SyntheticXtreamPlan plan,
        SyntheticXtreamResponseFactory responses,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = ServeClientAsync(client, plan, responses, cancellationToken);
        }
    }

    private static async Task ServeClientAsync(
        TcpClient client,
        SyntheticXtreamPlan plan,
        SyntheticXtreamResponseFactory responses,
        CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            try
            {
                var requestTarget = await ReadRequestTargetAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                var requestUri = new Uri("http://localhost" + requestTarget);
                var query = ParseQuery(requestUri.Query);
                var payload = CreatePayload(requestUri.AbsolutePath, query, plan, responses);
                var body = JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType());
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }

    private static object CreatePayload(
        string path,
        IReadOnlyDictionary<string, string> query,
        SyntheticXtreamPlan plan,
        SyntheticXtreamResponseFactory responses)
    {
        if (string.Equals(path, "/health", StringComparison.Ordinal))
        {
            return new { status = "ready", total_count = plan.TotalCount };
        }

        if (!query.TryGetValue("username", out var username) ||
            !string.Equals(username, "benchmark", StringComparison.Ordinal) ||
            !query.TryGetValue("password", out var password) ||
            !string.Equals(password, "benchmark", StringComparison.Ordinal))
        {
            return new { user_info = new { status = "Disabled" } };
        }

        query.TryGetValue("action", out var action);
        query.TryGetValue("category_id", out var categoryId);
        return action switch
        {
            "get_live_categories" => responses.CreateCategories("live"),
            "get_vod_categories" => responses.CreateCategories("vod"),
            "get_series_categories" => responses.CreateCategories("series"),
            "get_live_streams" => responses.CreateItems("live", categoryId ?? string.Empty),
            "get_vod_streams" => responses.CreateItems("vod", categoryId ?? string.Empty),
            "get_series" => responses.CreateItems("series", categoryId ?? string.Empty),
            _ => responses.CreateAuthPayload()
        };
    }

    private static async Task<string> ReadRequestTargetAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("HTTP request line is missing.");
        string? header;
        do
        {
            header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        while (!string.IsNullOrEmpty(header));

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new IOException("HTTP request line is invalid.");
        }

        return parts[1];
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = separator < 0 ? part : part[..separator];
            var value = separator < 0 ? string.Empty : part[(separator + 1)..];
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }
}
