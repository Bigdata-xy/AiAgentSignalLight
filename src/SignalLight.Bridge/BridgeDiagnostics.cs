using System.Text.Json;
using SignalLight.Storage;

namespace SignalLight.Bridge;

public sealed class BridgeDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public BridgeDiagnostics(SignalLightPaths paths)
    {
        _directory = Path.Combine(paths.DiagnosticsDirectory, "remote-bridge");
        Directory.CreateDirectory(_directory);
    }

    public void WriteLatest(string name, object value)
    {
        var path = Path.Combine(_directory, name);
        var content = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(path, content);
    }
}
