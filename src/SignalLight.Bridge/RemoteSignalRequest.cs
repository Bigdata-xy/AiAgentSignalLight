namespace SignalLight.Bridge;

public sealed record RemoteSignalRequest
{
    public int SchemaVersion { get; init; } = 1;
    public string EventType { get; init; } = string.Empty;
    public string CodexEvent { get; init; } = string.Empty;
    public string Source { get; init; } = "remote-ssh";
    public string Adapter { get; init; } = "codex-remote-hooks";
    public string RemoteHost { get; init; } = string.Empty;
    public string RemoteUser { get; init; } = string.Empty;
    public string Workspace { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
    public DateTimeOffset? ProcessStartTime { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public IReadOnlyDictionary<string, string> Payload { get; init; } = new Dictionary<string, string>();
}
