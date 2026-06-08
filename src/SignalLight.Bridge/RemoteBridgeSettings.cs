using System.Security.Cryptography;
using System.Text.Json;
using SignalLight.Storage;

namespace SignalLight.Bridge;

public sealed record RemoteBridgeSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int SchemaVersion { get; init; } = 1;
    public string Bind { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 37631;
    public string Token { get; init; } = string.Empty;

    public static RemoteBridgeSettings LoadOrCreate(IReadOnlyDictionary<string, string> options)
    {
        var paths = new SignalLightPaths(Get(options, "root"));
        paths.EnsureAll();

        var path = Path.Combine(paths.RootDirectory, "remote-bridge.json");
        var settings = ReadSettings(path) ?? new RemoteBridgeSettings();

        var bind = Get(options, "bind", Environment.GetEnvironmentVariable("SIGNAL_LIGHT_BRIDGE_BIND"));
        var portText = Get(options, "port", Environment.GetEnvironmentVariable("SIGNAL_LIGHT_BRIDGE_PORT"));
        var token = Get(options, "token", Environment.GetEnvironmentVariable("SIGNAL_LIGHT_BRIDGE_TOKEN"));

        if (!string.IsNullOrWhiteSpace(bind))
        {
            settings = settings with { Bind = bind };
        }

        if (int.TryParse(portText, out var port) && port > 0 && port < 65536)
        {
            settings = settings with { Port = port };
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            settings = settings with { Token = token };
        }

        if (string.IsNullOrWhiteSpace(settings.Token))
        {
            settings = settings with { Token = GenerateToken() };
        }

        WriteSettings(path, settings);
        return settings;
    }

    private static RemoteBridgeSettings? ReadSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<RemoteBridgeSettings>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteSettings(string path, RemoteBridgeSettings settings)
    {
        var content = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, content);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Get(IReadOnlyDictionary<string, string> options, string key, string? fallback = "")
    {
        return options.TryGetValue(key, out var value) ? value : fallback ?? string.Empty;
    }
}
