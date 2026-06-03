using SignalLight.Core.Events;
using SignalLight.Core.Sessions;

namespace SignalLight.Core.State;

public sealed class SignalStateEngine
{
    public SignalSession Apply(SignalEvent signalEvent, SignalSession? previous = null)
    {
        var state = signalEvent.EventType switch
        {
            SignalEventType.TaskStarted => SignalSessionState.Thinking,
            SignalEventType.UserActionRequired => SignalSessionState.Waiting,
            SignalEventType.TaskCompleted => SignalSessionState.Completed,
            SignalEventType.TaskFailed => SignalSessionState.Failed,
            SignalEventType.SessionEnded => SignalSessionState.Completed,
            SignalEventType.Heartbeat => previous?.State ?? SignalSessionState.Thinking,
            _ => SignalSessionState.Unknown
        };

        return new SignalSession
        {
            SessionId = NormalizeSessionId(signalEvent),
            DisplayName = BuildDisplayName(signalEvent, previous),
            Source = signalEvent.Source,
            Adapter = signalEvent.Adapter,
            Workspace = signalEvent.Workspace,
            State = state,
            LastEventType = signalEvent.EventType,
            UpdatedAt = signalEvent.CreatedAt
        };
    }

    public SignalSnapshot BuildSnapshot(IEnumerable<SignalSession> sessions, DateTimeOffset? now = null)
    {
        var visible = sessions.ToList();
        return new SignalSnapshot
        {
            AggregateState = Aggregate(visible),
            Sessions = visible
                .OrderBy(GetPriority)
                .ThenByDescending(session => session.UpdatedAt)
                .ToArray(),
            UpdatedAt = now ?? DateTimeOffset.Now
        };
    }

    public static SignalSessionState Aggregate(IEnumerable<SignalSession> sessions)
    {
        var list = sessions.ToList();
        if (list.Count == 0)
        {
            return SignalSessionState.Idle;
        }

        if (list.Any(session => session.State == SignalSessionState.Waiting))
        {
            return SignalSessionState.Waiting;
        }

        if (list.Any(session => session.State == SignalSessionState.Failed))
        {
            return SignalSessionState.Failed;
        }

        if (list.Any(session => session.State == SignalSessionState.Thinking))
        {
            return SignalSessionState.Thinking;
        }

        if (list.Any(session => session.State == SignalSessionState.Completed))
        {
            return SignalSessionState.Completed;
        }

        return SignalSessionState.Unknown;
    }

    private static int GetPriority(SignalSession session)
    {
        return session.State switch
        {
            SignalSessionState.Waiting => 0,
            SignalSessionState.Failed => 1,
            SignalSessionState.Thinking => 2,
            SignalSessionState.Completed => 3,
            SignalSessionState.Idle => 4,
            SignalSessionState.Stale => 5,
            _ => 6
        };
    }

    private static string NormalizeSessionId(SignalEvent signalEvent)
    {
        if (!string.IsNullOrWhiteSpace(signalEvent.SessionId))
        {
            return Sanitize(signalEvent.SessionId);
        }

        var fallback = $"{signalEvent.Source}-{signalEvent.Adapter}-{signalEvent.Workspace}-{signalEvent.Title}";
        return Sanitize(fallback);
    }

    private static string BuildDisplayName(SignalEvent signalEvent, SignalSession? previous)
    {
        if (!string.IsNullOrWhiteSpace(signalEvent.Title))
        {
            return signalEvent.Title.Length <= 48 ? signalEvent.Title : signalEvent.Title[..48];
        }

        if (!string.IsNullOrWhiteSpace(previous?.DisplayName))
        {
            return previous.DisplayName;
        }

        return string.IsNullOrWhiteSpace(signalEvent.Source) ? "AI Task" : signalEvent.Source;
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length == 0 ? Guid.NewGuid().ToString("N") : new string(chars);
    }
}
