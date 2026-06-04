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
            SessionId = NormalizeSessionId(signalEvent, previous),
            DisplayName = BuildDisplayName(signalEvent, previous),
            Source = signalEvent.Source,
            Adapter = signalEvent.Adapter,
            Workspace = BuildWorkspace(signalEvent, previous),
            State = state,
            LastEventType = signalEvent.EventType,
            StartedAt = GetStartedAt(signalEvent, previous),
            UpdatedAt = signalEvent.CreatedAt
        };
    }

    public SignalSnapshot BuildSnapshot(
        IEnumerable<SignalSession> sessions,
        DateTimeOffset? now = null,
        TimeSpan? staleAfter = null,
        TimeSpan? completedRetention = null)
    {
        var referenceTime = now ?? DateTimeOffset.Now;
        var staleThreshold = staleAfter ?? TimeSpan.FromMinutes(30);
        var completedThreshold = completedRetention ?? TimeSpan.FromHours(8);
        var visible = sessions
            .Select(session => ApplyExpiry(session, referenceTime, staleThreshold))
            .Where(session => ShouldKeep(session, referenceTime, completedThreshold))
            .ToList();

        return new SignalSnapshot
        {
            AggregateState = Aggregate(visible),
            Sessions = visible
                .OrderBy(GetPriority)
                .ThenByDescending(session => session.UpdatedAt)
                .ToArray(),
            UpdatedAt = referenceTime
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

    private static SignalSession ApplyExpiry(SignalSession session, DateTimeOffset now, TimeSpan staleAfter)
    {
        if (session.State is not (SignalSessionState.Thinking or SignalSessionState.Waiting))
        {
            return session;
        }

        return now - session.UpdatedAt > staleAfter
            ? session with { State = SignalSessionState.Stale }
            : session;
    }

    private static bool ShouldKeep(SignalSession session, DateTimeOffset now, TimeSpan completedRetention)
    {
        if (session.State is SignalSessionState.Completed or SignalSessionState.Idle)
        {
            return now - session.UpdatedAt <= completedRetention;
        }

        return true;
    }

    private static string NormalizeSessionId(SignalEvent signalEvent, SignalSession? previous)
    {
        if (!string.IsNullOrWhiteSpace(previous?.SessionId))
        {
            return previous.SessionId;
        }

        if (!string.IsNullOrWhiteSpace(signalEvent.SessionId))
        {
            return Sanitize(signalEvent.SessionId);
        }

        var fallback = !string.IsNullOrWhiteSpace(signalEvent.Title)
            ? $"{signalEvent.Source}-{signalEvent.Adapter}-{signalEvent.Workspace}-{signalEvent.Title}"
            : $"{signalEvent.Source}-{signalEvent.Adapter}-{signalEvent.Workspace}";
        return Sanitize(fallback);
    }

    private static string BuildDisplayName(SignalEvent signalEvent, SignalSession? previous)
    {
        if (!string.IsNullOrWhiteSpace(signalEvent.Title) && !IsPlaceholderTitle(signalEvent.Title))
        {
            return signalEvent.Title.Length <= 48 ? signalEvent.Title : signalEvent.Title[..48];
        }

        if (!string.IsNullOrWhiteSpace(previous?.DisplayName))
        {
            return previous.DisplayName;
        }

        return "AI Task";
    }

    private static string BuildWorkspace(SignalEvent signalEvent, SignalSession? previous)
    {
        if (previous is not null
            && string.IsNullOrWhiteSpace(signalEvent.Title)
            && !string.IsNullOrWhiteSpace(previous.Workspace))
        {
            return previous.Workspace;
        }

        return signalEvent.Workspace;
    }

    private static DateTimeOffset GetStartedAt(SignalEvent signalEvent, SignalSession? previous)
    {
        if (previous?.StartedAt > DateTimeOffset.MinValue)
        {
            return previous.StartedAt;
        }

        if (previous?.UpdatedAt > DateTimeOffset.MinValue)
        {
            return previous.UpdatedAt;
        }

        return signalEvent.CreatedAt;
    }

    private static bool IsPlaceholderTitle(string title)
    {
        var normalized = title.Trim();
        return string.Equals(normalized, "Codex", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "True", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "False", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "AI Task", StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length == 0 ? Guid.NewGuid().ToString("N") : new string(chars);
    }
}
