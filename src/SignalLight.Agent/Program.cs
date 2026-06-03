using SignalLight.Core.Events;
using SignalLight.Core.State;
using SignalLight.Storage;
using SignalLight.Storage.Json;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintHelp();
    return 0;
}

if (!string.Equals(args[0], "emit", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Unknown command. Use: emit");
    return 2;
}

var options = ParseOptions(args.Skip(1).ToArray());
var signalEvent = new SignalEvent
{
    EventType = MapState(Get(options, "state")),
    SessionId = Get(options, "session"),
    Source = Get(options, "source", "generic"),
    Adapter = Get(options, "adapter", "generic-cli"),
    Workspace = Get(options, "workspace"),
    Title = Get(options, "title"),
    CreatedAt = DateTimeOffset.Now
};

var paths = new SignalLightPaths(Get(options, "root"));
var store = new JsonSignalStore(paths);
var engine = new SignalStateEngine();
var previous = store.LoadSessions().FirstOrDefault(session => session.SessionId == signalEvent.SessionId);
var sessionState = engine.Apply(signalEvent, previous);
store.SaveEvent(signalEvent);
store.SaveSession(sessionState);
store.SaveSnapshot(engine.BuildSnapshot(store.LoadSessions()));

return 0;

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

static string Get(IReadOnlyDictionary<string, string> options, string key, string fallback = "")
{
    return options.TryGetValue(key, out var value) ? value : fallback;
}

static SignalEventType MapState(string state)
{
    return state.ToLowerInvariant() switch
    {
        "running" or "thinking" or "red" => SignalEventType.TaskStarted,
        "waiting" or "yellow" => SignalEventType.UserActionRequired,
        "completed" or "done" or "idle" or "green" => SignalEventType.TaskCompleted,
        "failed" or "error" => SignalEventType.TaskFailed,
        "heartbeat" => SignalEventType.Heartbeat,
        _ => SignalEventType.Unknown
    };
}

static void PrintHelp()
{
    Console.WriteLine("SignalLight.Agent");
    Console.WriteLine("Usage:");
    Console.WriteLine("  SignalLight.Agent emit --state running --source codex-cli --adapter codex-hooks --session demo --title \"Demo\"");
}
