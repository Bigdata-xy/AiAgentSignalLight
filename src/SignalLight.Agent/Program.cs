using System.Text;
using System.Text.Json;
using SignalLight.Core.Events;
using SignalLight.Core.Sessions;
using SignalLight.Core.State;
using SignalLight.Storage;
using SignalLight.Storage.Json;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintHelp();
    return 0;
}

if (string.Equals(args[0], "hook", StringComparison.OrdinalIgnoreCase))
{
    return RunCodexHook(ParseOptions(args.Skip(1).ToArray()));
}

if (!string.Equals(args[0], "emit", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Unknown command. Use: emit or hook");
    return 2;
}

var options = ParseOptions(args.Skip(1).ToArray());
var emittedEventType = MapState(Get(options, "state"));
if (emittedEventType == SignalEventType.TaskCompleted)
{
    return 0;
}

var signalEvent = new SignalEvent
{
    EventType = emittedEventType,
    SessionId = Get(options, "session"),
    Source = Get(options, "source", "generic"),
    Adapter = Get(options, "adapter", "generic-cli"),
    Workspace = Get(options, "workspace"),
    Title = Get(options, "title"),
    CreatedAt = DateTimeOffset.Now
};

SaveSignalEvent(new SignalLightPaths(Get(options, "root")), signalEvent);
return 0;

static int RunCodexHook(IReadOnlyDictionary<string, string> options)
{
    var eventName = Get(options, "event");
    var rawPayload = Console.In.ReadToEnd().TrimStart('\uFEFF');
    using var payload = ParsePayload(rawPayload, out var payloadParseError);
    var cancellationSignal = GetPayloadText(
        payload?.RootElement,
        "tool_response",
        "toolResponse",
        "message",
        "status",
        "decision",
        "permission_resolution",
        "permissionResolution");
    var cancelledPayload = IsCancellationPayload(cancellationSignal);
    var state = MapCodexEventState(eventName, cancelledPayload);
    var root = Get(options, "root");
    if (string.IsNullOrWhiteSpace(root))
    {
        root = Environment.GetEnvironmentVariable("SIGNAL_LIGHT_ROOT") ?? string.Empty;
    }

    var paths = new SignalLightPaths(root);

    var session = GetPayloadText(
        payload?.RootElement,
        "session_id",
        "sessionId",
        "session",
        "conversation_id",
        "conversationId",
        "conversation",
        "codex_session_id",
        "codexSessionId",
        "thread_id",
        "threadId",
        "terminal_id",
        "terminalId",
        "terminal",
        "process_id",
        "processId",
        "pid");
    var workspace = GetPayloadText(payload?.RootElement, "cwd", "workspace", "workdir", "current_working_directory");
    var title = GetPayloadText(payload?.RootElement, "prompt", "user_prompt", "userPrompt", "message", "text", "input");

    WriteHookDiagnostics(paths, new
    {
        schemaVersion = 1,
        receivedAt = DateTimeOffset.Now.ToString("o"),
        eventName,
        mappedState = state,
        toolRoot = AppContext.BaseDirectory,
        signalRoot = paths.RootDirectory,
        agentPath = Environment.ProcessPath ?? "dotnet",
        agentFound = true,
        session,
        workspace,
        title,
        cancelledPayload,
        cancellationSignal,
        payloadParseError,
        rawPayload
    });

    if (string.Equals(state, "ignored", StringComparison.OrdinalIgnoreCase))
    {
        return 0;
    }

    SaveSignalEvent(paths, new SignalEvent
    {
        EventType = MapState(state),
        SessionId = session,
        Source = Get(options, "source", "codex-cli"),
        Adapter = Get(options, "adapter", "codex-hooks"),
        Workspace = workspace,
        Title = title,
        CreatedAt = DateTimeOffset.Now
    }, allowStartedMatch: IsToolResumeEvent(eventName));

    return 0;
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

static string MapCodexEventState(string eventName, bool cancelledPayload = false)
{
    if (string.Equals(eventName, "PostToolUse", StringComparison.OrdinalIgnoreCase) && cancelledPayload)
    {
        return "completed";
    }

    return eventName switch
    {
        "UserPromptSubmit" => "running",
        "PreToolUse" => "running",
        "PostToolUse" => "running",
        "PermissionRequest" => "waiting",
        "Stop" => "completed",
        "SessionStart" => "ignored",
        _ => "unknown"
    };
}

static bool IsCancellationPayload(string rawPayload)
{
    if (string.IsNullOrWhiteSpace(rawPayload))
    {
        return false;
    }

    var text = rawPayload.ToLowerInvariant();
    return text.Contains("you canceled the request", StringComparison.Ordinal)
        || text.Contains("you cancelled the request", StringComparison.Ordinal)
        || text.Contains("conversation interrupted", StringComparison.Ordinal)
        || text.Contains("<turn_aborted>", StringComparison.Ordinal)
        || text.Contains("turn_aborted", StringComparison.Ordinal)
        || text.Contains("turn aborted", StringComparison.Ordinal)
        || text.Contains("the user interrupted the previous turn on purpose", StringComparison.Ordinal);
}

static bool IsToolResumeEvent(string eventName)
{
    return string.Equals(eventName, "PreToolUse", StringComparison.OrdinalIgnoreCase)
        || string.Equals(eventName, "PostToolUse", StringComparison.OrdinalIgnoreCase);
}

static JsonDocument? ParsePayload(string rawPayload, out string error)
{
    error = string.Empty;
    if (string.IsNullOrWhiteSpace(rawPayload))
    {
        return null;
    }

    try
    {
        return JsonDocument.Parse(rawPayload);
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return null;
    }
}

static string GetPayloadText(JsonElement? root, params string[] names)
{
    return root is null ? string.Empty : FindPayloadText(root.Value, names);
}

static string FindPayloadText(JsonElement element, IReadOnlyList<string> names)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                var text = GetElementText(property);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            var text = FindPayloadText(property.Value, names);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
    }

    if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray())
        {
            var text = FindPayloadText(item, names);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
    }

    return string.Empty;
}

static string GetElementText(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Number)
    {
        return element.ToString();
    }

    if (element.ValueKind != JsonValueKind.String)
    {
        return string.Empty;
    }

    var text = element.GetString() ?? string.Empty;
    return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
        ? string.Empty
        : text;
}

static void WriteHookDiagnostics(SignalLightPaths paths, object diagnostics)
{
    paths.EnsureAll();
    var path = Path.Combine(paths.DiagnosticsDirectory, "latest-hook-context.json");
    var content = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true });
    WriteTextAtomic(path, content);
}

static void WriteTextAtomic(string path, string content)
{
    Exception? lastError = null;
    for (var attempt = 1; attempt <= 5; attempt++)
    {
        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, content, Encoding.UTF8);
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

static void TryDelete(string path)
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

static void SaveSignalEvent(SignalLightPaths paths, SignalEvent signalEvent, bool allowStartedMatch = false)
{
    var store = new JsonSignalStore(paths);
    var engine = new SignalStateEngine();
    var sessions = store.LoadSessions();
    var previous = FindPreviousSession(sessions, signalEvent, allowStartedMatch);
    if (signalEvent.EventType == SignalEventType.TaskCompleted
        && previous is null
        && string.IsNullOrWhiteSpace(signalEvent.Title))
    {
        return;
    }

    var sessionState = engine.Apply(signalEvent, previous);
    store.SaveEvent(signalEvent);
    store.SaveSession(sessionState);
    store.SaveSnapshot(engine.BuildSnapshot(store.LoadSessions()));
}

static SignalSession? FindPreviousSession(IReadOnlyList<SignalSession> sessions, SignalEvent signalEvent, bool allowStartedMatch)
{
    if (allowStartedMatch)
    {
        var waitingMatch = sessions
            .Where(session => SameValue(session.Source, signalEvent.Source))
            .Where(session => SameValue(session.Adapter, signalEvent.Adapter))
            .OrderByDescending(session => session.UpdatedAt)
            .FirstOrDefault(session => session.State == SignalSessionState.Waiting);
        if (waitingMatch is not null)
        {
            return waitingMatch;
        }
    }

    if (!string.IsNullOrWhiteSpace(signalEvent.SessionId))
    {
        var sanitized = Sanitize(signalEvent.SessionId);
        var sessionMatch = sessions.FirstOrDefault(session =>
            string.Equals(session.SessionId, signalEvent.SessionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(session.SessionId, sanitized, StringComparison.OrdinalIgnoreCase));
        if (sessionMatch is not null)
        {
            return sessionMatch;
        }

        if (signalEvent.EventType == SignalEventType.TaskStarted && !allowStartedMatch)
        {
            return null;
        }
    }

    if (signalEvent.EventType == SignalEventType.TaskStarted && !allowStartedMatch)
    {
        return null;
    }

    var candidates = sessions
        .Where(session => SameValue(session.Source, signalEvent.Source))
        .Where(session => SameValue(session.Adapter, signalEvent.Adapter))
        .Where(session => string.IsNullOrWhiteSpace(signalEvent.Workspace) || SameValue(session.Workspace, signalEvent.Workspace))
        .OrderByDescending(session => session.UpdatedAt)
        .ToArray();

    if (allowStartedMatch)
    {
        var waitingMatch = candidates.FirstOrDefault(session => session.State == SignalSessionState.Waiting)
            ?? sessions
                .Where(session => SameValue(session.Source, signalEvent.Source))
                .Where(session => SameValue(session.Adapter, signalEvent.Adapter))
                .OrderByDescending(session => session.UpdatedAt)
                .FirstOrDefault(session => session.State == SignalSessionState.Waiting);
        if (waitingMatch is not null)
        {
            return waitingMatch;
        }
    }

    if (!string.IsNullOrWhiteSpace(signalEvent.Title))
    {
        var titleMatch = candidates.FirstOrDefault(session => SameValue(session.DisplayName, signalEvent.Title));
        if (titleMatch is not null)
        {
            return titleMatch;
        }
    }

    var activeCandidates = candidates
        .Where(session => session.State is SignalSessionState.Thinking or SignalSessionState.Waiting or SignalSessionState.Stale)
        .ToArray();

    if (activeCandidates.Length == 1)
    {
        return activeCandidates[0];
    }

    return activeCandidates.Length == 0 ? candidates.FirstOrDefault() : null;
}

static bool SameValue(string left, string right)
{
    return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}

static string Sanitize(string value)
{
    var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
    return chars.Length == 0 ? string.Empty : new string(chars);
}

static void PrintHelp()
{
    Console.WriteLine("SignalLight.Agent");
    Console.WriteLine("Usage:");
    Console.WriteLine("  SignalLight.Agent emit --state running --source codex-cli --adapter codex-hooks --session demo --title \"Demo\"");
    Console.WriteLine("  SignalLight.Agent hook --event UserPromptSubmit");
}
