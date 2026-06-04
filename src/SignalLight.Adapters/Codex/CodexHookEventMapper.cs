using SignalLight.Core.Events;

namespace SignalLight.Adapters.Codex;

public static class CodexHookEventMapper
{
    public static SignalEventType Map(string codexEventName)
    {
        return codexEventName switch
        {
            "UserPromptSubmit" => SignalEventType.TaskStarted,
            "PreToolUse" => SignalEventType.TaskStarted,
            "PostToolUse" => SignalEventType.TaskStarted,
            "PermissionRequest" => SignalEventType.UserActionRequired,
            "Stop" => SignalEventType.TaskCompleted,
            "SessionStart" => SignalEventType.Unknown,
            _ => SignalEventType.Unknown
        };
    }
}
