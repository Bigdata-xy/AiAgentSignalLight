namespace SignalLight.Core.Events;

public sealed record SignalEvent
{
    public int SchemaVersion { get; init; } = 1;
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public SignalEventType EventType { get; init; } = SignalEventType.Unknown;
    public string SessionId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string Source { get; init; } = "unknown";
    public string Adapter { get; init; } = "generic";
    public string Workspace { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
    public DateTimeOffset? ProcessStartTime { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyDictionary<string, string> Payload { get; init; } = new Dictionary<string, string>();
}
