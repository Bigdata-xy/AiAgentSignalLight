using SignalLight.Core.Events;

namespace SignalLight.Core.Sessions;

public sealed record SignalSession
{
    public string SessionId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Source { get; init; } = "unknown";
    public string Adapter { get; init; } = "generic";
    public string Workspace { get; init; } = string.Empty;
    public SignalSessionState State { get; init; } = SignalSessionState.Unknown;
    public SignalEventType LastEventType { get; init; } = SignalEventType.Unknown;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}
