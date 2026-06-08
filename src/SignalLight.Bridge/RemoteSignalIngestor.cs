using SignalLight.Core.Events;
using SignalLight.Core.Sessions;
using SignalLight.Core.State;
using SignalLight.Storage;
using SignalLight.Storage.Json;

namespace SignalLight.Bridge;

public sealed class RemoteSignalIngestor
{
    private readonly SignalLightPaths _paths;
    private readonly JsonSignalStore _store;
    private readonly SignalStateEngine _engine = new();

    public RemoteSignalIngestor(SignalLightPaths paths)
    {
        _paths = paths;
        _store = new JsonSignalStore(paths);
    }

    public RemoteSignalIngestResult Ingest(RemoteSignalRequest request)
    {
        var eventType = ResolveEventType(request);
        if (eventType == SignalEventType.Unknown)
        {
            return RemoteSignalIngestResult.Ignored("unknown event type");
        }

        var signalEvent = new SignalEvent
        {
            EventType = eventType,
            SessionId = BuildRemoteSessionId(request),
            ConversationId = request.ConversationId,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "remote-ssh" : request.Source,
            Adapter = string.IsNullOrWhiteSpace(request.Adapter) ? "codex-remote-hooks" : request.Adapter,
            Workspace = request.Workspace,
            Title = request.Title,
            ProcessId = request.ProcessId,
            ProcessStartTime = request.ProcessStartTime,
            CreatedAt = request.CreatedAt ?? DateTimeOffset.Now,
            Payload = BuildPayload(request)
        };

        var previous = FindPreviousSession(_store.LoadSessions(), signalEvent);
        var session = _engine.Apply(signalEvent, previous);
        _store.SaveEvent(signalEvent);
        _store.SaveSession(session);
        _store.SaveSnapshot(_engine.BuildSnapshot(_store.LoadSessions()));

        return RemoteSignalIngestResult.Accepted(session.SessionId, session.State.ToString());
    }

    private static SignalEventType ResolveEventType(RemoteSignalRequest request)
    {
        if (Enum.TryParse<SignalEventType>(request.EventType, ignoreCase: true, out var direct)
            && direct != SignalEventType.Unknown)
        {
            return direct;
        }

        var codexEvent = request.CodexEvent;
        if (string.IsNullOrWhiteSpace(codexEvent)
            && request.Payload.TryGetValue("codexEvent", out var payloadEvent))
        {
            codexEvent = payloadEvent;
        }

        if (IsCancellationPayload(request))
        {
            return SignalEventType.TaskCompleted;
        }

        return codexEvent switch
        {
            "UserPromptSubmit" => SignalEventType.TaskStarted,
            "PreToolUse" => SignalEventType.TaskStarted,
            "PostToolUse" => SignalEventType.TaskStarted,
            "PermissionRequest" => SignalEventType.UserActionRequired,
            "Stop" => SignalEventType.TaskCompleted,
            _ => SignalEventType.Unknown
        };
    }

    private static bool IsCancellationPayload(RemoteSignalRequest request)
    {
        foreach (var value in request.Payload.Values)
        {
            var text = value.ToLowerInvariant();
            if (text.Contains("you canceled the request", StringComparison.Ordinal)
                || text.Contains("you cancelled the request", StringComparison.Ordinal)
                || text.Contains("conversation interrupted", StringComparison.Ordinal)
                || text.Contains("<turn_aborted>", StringComparison.Ordinal)
                || text.Contains("turn_aborted", StringComparison.Ordinal)
                || text.Contains("turn aborted", StringComparison.Ordinal)
                || text.Contains("the user interrupted the previous turn on purpose", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> BuildPayload(RemoteSignalRequest request)
    {
        var payload = new Dictionary<string, string>(request.Payload, StringComparer.OrdinalIgnoreCase)
        {
            ["remoteHost"] = request.RemoteHost,
            ["remoteUser"] = request.RemoteUser
        };

        if (!string.IsNullOrWhiteSpace(request.CodexEvent))
        {
            payload["codexEvent"] = request.CodexEvent;
        }

        return payload;
    }

    private static string BuildRemoteSessionId(RemoteSignalRequest request)
    {
        var fallbackSession = !string.IsNullOrWhiteSpace(request.SessionId)
            ? request.SessionId
            : request.ProcessId?.ToString() ?? request.Title;
        var raw = $"remote:{request.RemoteHost}:{request.RemoteUser}:{request.Workspace}:{fallbackSession}";
        return Sanitize(raw);
    }

    private static SignalSession? FindPreviousSession(IReadOnlyList<SignalSession> sessions, SignalEvent signalEvent)
    {
        if (!string.IsNullOrWhiteSpace(signalEvent.SessionId))
        {
            var exact = sessions.FirstOrDefault(session =>
                string.Equals(session.SessionId, signalEvent.SessionId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        return null;
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length == 0 ? Guid.NewGuid().ToString("N") : new string(chars);
    }
}

public sealed record RemoteSignalIngestResult(bool ChangedState, string Message, string SessionId = "", string State = "")
{
    public static RemoteSignalIngestResult Accepted(string sessionId, string state)
    {
        return new RemoteSignalIngestResult(true, "accepted", sessionId, state);
    }

    public static RemoteSignalIngestResult Ignored(string message)
    {
        return new RemoteSignalIngestResult(false, message);
    }
}
