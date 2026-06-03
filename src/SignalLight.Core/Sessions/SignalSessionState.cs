namespace SignalLight.Core.Sessions;

public enum SignalSessionState
{
    Unknown = 0,
    Idle,
    Thinking,
    Waiting,
    Completed,
    Failed,
    Stale
}
