namespace SignalLight.Storage;

public sealed class SignalLightPaths
{
    public SignalLightPaths(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SignalLight")
            : rootDirectory;

        SnapshotPath = Path.Combine(RootDirectory, "snapshot.json");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
        EventsDirectory = Path.Combine(RootDirectory, "events");
        SessionsDirectory = Path.Combine(RootDirectory, "sessions");
        DiagnosticsDirectory = Path.Combine(RootDirectory, "diagnostics");
    }

    public string RootDirectory { get; }
    public string SnapshotPath { get; }
    public string SettingsPath { get; }
    public string EventsDirectory { get; }
    public string SessionsDirectory { get; }
    public string DiagnosticsDirectory { get; }

    public void EnsureAll()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(EventsDirectory);
        Directory.CreateDirectory(SessionsDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }
}
