namespace SignalLight.Core.Events;

public enum SignalEventType
{
    Unknown = 0,
    TaskStarted,
    UserActionRequired,
    TaskCompleted,
    TaskFailed,
    Heartbeat,
    SessionEnded
}
