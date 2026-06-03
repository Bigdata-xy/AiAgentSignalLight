using SignalLight.Core.Sessions;

namespace SignalLight.Core.State;

public sealed record SignalSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public SignalSessionState AggregateState { get; init; } = SignalSessionState.Unknown;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyList<SignalSession> Sessions { get; init; } = Array.Empty<SignalSession>();

    public int CompletedCount => Sessions.Count(session => session.State is SignalSessionState.Completed or SignalSessionState.Idle);
    public int TotalCount => Sessions.Count;
}
