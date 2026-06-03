namespace SignalLight.Adapters.Browser;

public sealed record BrowserSignalContract
{
    public string TabId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string State { get; init; } = "unknown";
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.Now;
}
