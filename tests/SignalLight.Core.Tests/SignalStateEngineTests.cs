using SignalLight.Core.Events;
using SignalLight.Core.Sessions;
using SignalLight.Core.State;
using Xunit;

namespace SignalLight.Core.Tests;

public sealed class SignalStateEngineTests
{
    [Fact]
    public void Waiting_has_highest_user_attention_priority()
    {
        var sessions = new[]
        {
            new SignalSession { SessionId = "running", State = SignalSessionState.Thinking },
            new SignalSession { SessionId = "waiting", State = SignalSessionState.Waiting }
        };

        Assert.Equal(SignalSessionState.Waiting, SignalStateEngine.Aggregate(sessions));
    }

    [Fact]
    public void Task_started_maps_to_thinking()
    {
        var engine = new SignalStateEngine();
        var session = engine.Apply(new SignalEvent { EventType = SignalEventType.TaskStarted, SessionId = "demo" });

        Assert.Equal(SignalSessionState.Thinking, session.State);
    }
}
