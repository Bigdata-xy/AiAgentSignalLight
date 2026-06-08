using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SignalLight.Bridge;
using SignalLight.Storage;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var options = ParseOptions(args);
var root = Get(options, "root", Environment.GetEnvironmentVariable("SIGNAL_LIGHT_ROOT"));
var paths = new SignalLightPaths(root);
paths.EnsureAll();

var settings = RemoteBridgeSettings.LoadOrCreate(options);
var diagnostics = new BridgeDiagnostics(paths);
var ingestor = new RemoteSignalIngestor(paths);
using var listener = new TcpListener(IPAddress.Parse(settings.Bind), settings.Port);

diagnostics.WriteLatest("latest-status.json", new
{
    time = DateTimeOffset.Now.ToString("o"),
    status = "starting",
    bind = settings.Bind,
    settings.Port,
    root = paths.RootDirectory
});

listener.Start();
diagnostics.WriteLatest("latest-status.json", new
{
    time = DateTimeOffset.Now.ToString("o"),
    status = "running",
    bind = settings.Bind,
    settings.Port,
    root = paths.RootDirectory
});

Console.WriteLine($"SignalLight RemoteBridge listening on http://{settings.Bind}:{settings.Port}");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        try
        {
            await HandleClientAsync(client, settings, diagnostics, ingestor, paths);
        }
        catch (Exception ex)
        {
            diagnostics.WriteLatest("latest-error.json", new
            {
                time = DateTimeOffset.Now.ToString("o"),
                error = ex.Message
            });
        }
        finally
        {
            client.Dispose();
        }
    });
}

static async Task HandleClientAsync(
    TcpClient client,
    RemoteBridgeSettings settings,
    BridgeDiagnostics diagnostics,
    RemoteSignalIngestor ingestor,
    SignalLightPaths paths)
{
    using var stream = client.GetStream();
    var request = await ReadHttpRequestAsync(stream);
    if (request is null)
    {
        await WriteJsonAsync(stream, 400, new { error = "invalid request" });
        return;
    }

    if (request.Method == "GET" && request.Path is "/health" or "/api/health")
    {
        await WriteJsonAsync(stream, 200, new
        {
            status = "ok",
            bind = settings.Bind,
            settings.Port,
            root = paths.RootDirectory
        });
        return;
    }

    if (request.Method != "POST" || request.Path != "/api/events")
    {
        await WriteJsonAsync(stream, 404, new { error = "not found" });
        return;
    }

    if (!IsAuthorized(request, settings.Token))
    {
        diagnostics.WriteLatest("latest-rejected-request.json", new
        {
            time = DateTimeOffset.Now.ToString("o"),
            reason = "invalid authorization",
            remote = client.Client.RemoteEndPoint?.ToString() ?? string.Empty
        });
        await WriteJsonAsync(stream, 401, new { error = "unauthorized" });
        return;
    }

    RemoteSignalRequest? remoteEvent;
    try
    {
        remoteEvent = JsonSerializer.Deserialize<RemoteSignalRequest>(
            request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (JsonException ex)
    {
        diagnostics.WriteLatest("latest-error.json", new
        {
            time = DateTimeOffset.Now.ToString("o"),
            error = ex.Message
        });
        await WriteJsonAsync(stream, 400, new { error = "invalid json" });
        return;
    }

    if (remoteEvent is null)
    {
        await WriteJsonAsync(stream, 400, new { error = "empty request" });
        return;
    }

    var result = ingestor.Ingest(remoteEvent);
    diagnostics.WriteLatest("latest-request.json", new
    {
        time = DateTimeOffset.Now.ToString("o"),
        result,
        remoteEvent
    });

    await WriteJsonAsync(stream, result.ChangedState ? 200 : 202, result);
}

static async Task<HttpRequestData?> ReadHttpRequestAsync(NetworkStream stream)
{
    var buffer = new byte[8192];
    var data = new List<byte>();
    var headerEnd = -1;

    while (headerEnd < 0)
    {
        var read = await stream.ReadAsync(buffer);
        if (read <= 0)
        {
            return null;
        }

        data.AddRange(buffer.AsSpan(0, read).ToArray());
        headerEnd = IndexOfHeaderEnd(data);
        if (data.Count > 1024 * 1024)
        {
            return null;
        }
    }

    var headerText = Encoding.UTF8.GetString(data.Take(headerEnd).ToArray());
    var lines = headerText.Split("\r\n", StringSplitOptions.None);
    if (lines.Length == 0)
    {
        return null;
    }

    var first = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
    if (first.Length < 2)
    {
        return null;
    }

    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in lines.Skip(1))
    {
        var index = line.IndexOf(':', StringComparison.Ordinal);
        if (index <= 0)
        {
            continue;
        }

        headers[line[..index].Trim()] = line[(index + 1)..].Trim();
    }

    var bodyStart = headerEnd + 4;
    var contentLength = headers.TryGetValue("Content-Length", out var value) && int.TryParse(value, out var parsed)
        ? parsed
        : 0;

    while (data.Count - bodyStart < contentLength)
    {
        var read = await stream.ReadAsync(buffer);
        if (read <= 0)
        {
            break;
        }

        data.AddRange(buffer.AsSpan(0, read).ToArray());
    }

    var bodyBytes = data.Skip(bodyStart).Take(contentLength).ToArray();
    var body = Encoding.UTF8.GetString(bodyBytes);
    return new HttpRequestData(first[0], first[1], headers, body);
}

static int IndexOfHeaderEnd(IReadOnlyList<byte> data)
{
    for (var i = 0; i <= data.Count - 4; i++)
    {
        if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
        {
            return i;
        }
    }

    return -1;
}

static bool IsAuthorized(HttpRequestData request, string token)
{
    if (string.IsNullOrWhiteSpace(token)
        || !request.Headers.TryGetValue("Authorization", out var header)
        || string.IsNullOrWhiteSpace(header))
    {
        return false;
    }

    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && string.Equals(header[prefix.Length..].Trim(), token, StringComparison.Ordinal);
}

static async Task WriteJsonAsync(NetworkStream stream, int statusCode, object value)
{
    var body = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    var bodyBytes = Encoding.UTF8.GetBytes(body);
    var statusText = statusCode switch
    {
        200 => "OK",
        202 => "Accepted",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        _ => "OK"
    };
    var header = Encoding.UTF8.GetBytes(
        $"HTTP/1.1 {statusCode} {statusText}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");

    await stream.WriteAsync(header);
    await stream.WriteAsync(bodyBytes);
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < values.Length; i++)
    {
        var key = values[i];
        if (!key.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var value = i + 1 < values.Length && !values[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? values[++i]
            : "true";
        result[key[2..]] = value;
    }

    return result;
}

static string Get(IReadOnlyDictionary<string, string> options, string key, string? fallback = "")
{
    return options.TryGetValue(key, out var value) ? value : fallback ?? string.Empty;
}

internal sealed record HttpRequestData(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string Body);
