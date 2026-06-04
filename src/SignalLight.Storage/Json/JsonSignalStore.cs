using System.Text.Json;
using SignalLight.Core.Abstractions;
using SignalLight.Core.Events;
using SignalLight.Core.Sessions;
using SignalLight.Core.State;

namespace SignalLight.Storage.Json;

public sealed class JsonSignalStore : ISignalStore
{
    private static readonly JsonSerializerOptions JsonOptions = JsonOptionsFactory.Create();
    private readonly SignalLightPaths _paths;

    public JsonSignalStore(SignalLightPaths paths)
    {
        _paths = paths;
    }

    public SignalSnapshot LoadSnapshot()
    {
        try
        {
            if (!File.Exists(_paths.SnapshotPath))
            {
                return new SignalSnapshot();
            }

            return JsonSerializer.Deserialize<SignalSnapshot>(File.ReadAllText(_paths.SnapshotPath), JsonOptions) ?? new SignalSnapshot();
        }
        catch
        {
            return new SignalSnapshot { AggregateState = SignalSessionState.Unknown };
        }
    }

    public IReadOnlyList<SignalSession> LoadSessions()
    {
        if (!Directory.Exists(_paths.SessionsDirectory))
        {
            return Array.Empty<SignalSession>();
        }

        var sessions = new List<SignalSession>();
        foreach (var path in Directory.EnumerateFiles(_paths.SessionsDirectory, "*.json"))
        {
            try
            {
                var session = JsonSerializer.Deserialize<SignalSession>(File.ReadAllText(path), JsonOptions);
                if (session is not null && !string.IsNullOrWhiteSpace(session.SessionId))
                {
                    sessions.Add(session);
                }
            }
            catch
            {
                // Ignore one damaged session file; diagnostics will cover store-level health later.
            }
        }

        return sessions;
    }

    public void SaveEvent(SignalEvent signalEvent)
    {
        _paths.EnsureAll();
        var fileName = $"{signalEvent.CreatedAt:yyyyMMddHHmmssfff}-{Sanitize(signalEvent.EventId)}.json";
        WriteAtomic(Path.Combine(_paths.EventsDirectory, fileName), signalEvent);
    }

    public void SaveSession(SignalSession session)
    {
        _paths.EnsureAll();
        WriteAtomic(Path.Combine(_paths.SessionsDirectory, Sanitize(session.SessionId) + ".json"), session);
    }

    public void SaveSnapshot(SignalSnapshot snapshot)
    {
        _paths.EnsureAll();
        WriteAtomic(_paths.SnapshotPath, snapshot);
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        var content = JsonSerializer.Serialize(value, JsonOptions);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, content);
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                TryDelete(tempPath);
                Thread.Sleep(TimeSpan.FromMilliseconds(20 * attempt));
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
                TryDelete(tempPath);
                Thread.Sleep(TimeSpan.FromMilliseconds(20 * attempt));
            }
        }

        throw lastError ?? new IOException($"Failed to write {path}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup for failed concurrent writes.
        }
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }
}
