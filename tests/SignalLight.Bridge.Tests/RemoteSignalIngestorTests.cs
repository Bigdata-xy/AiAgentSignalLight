using SignalLight.Bridge;
using SignalLight.Core.Sessions;
using SignalLight.Storage;
using SignalLight.Storage.Json;
using Xunit;

namespace SignalLight.Bridge.Tests;

public sealed class RemoteSignalIngestorTests
{
    [Fact]
    public void Remote_user_prompt_maps_to_thinking_snapshot()
    {
        using var fixture = new BridgeFixture();

        var result = fixture.Ingestor.Ingest(new RemoteSignalRequest
        {
            CodexEvent = "UserPromptSubmit",
            RemoteHost = "dev-server",
            RemoteUser = "ubuntu",
            Workspace = "/home/ubuntu/project",
            SessionId = "session-1",
            Title = "remote prompt"
        });

        var snapshot = fixture.Store.LoadSnapshot();
        Assert.True(result.ChangedState);
        Assert.Equal(SignalSessionState.Thinking, snapshot.AggregateState);
        Assert.Single(snapshot.Sessions);
        Assert.Equal("remote prompt", snapshot.Sessions.Single().DisplayName);
    }

    [Fact]
    public void Remote_permission_maps_to_waiting_snapshot()
    {
        using var fixture = new BridgeFixture();

        fixture.Ingestor.Ingest(new RemoteSignalRequest
        {
            CodexEvent = "PermissionRequest",
            RemoteHost = "dev-server",
            RemoteUser = "ubuntu",
            Workspace = "/home/ubuntu/project",
            SessionId = "session-1",
            Title = "remote prompt"
        });

        var snapshot = fixture.Store.LoadSnapshot();
        Assert.Equal(SignalSessionState.Waiting, snapshot.AggregateState);
    }

    [Fact]
    public void Authenticated_remote_stop_can_complete_matching_session()
    {
        using var fixture = new BridgeFixture();

        fixture.Ingestor.Ingest(new RemoteSignalRequest
        {
            CodexEvent = "UserPromptSubmit",
            RemoteHost = "dev-server",
            RemoteUser = "ubuntu",
            Workspace = "/home/ubuntu/project",
            SessionId = "session-1",
            Title = "remote prompt"
        });
        fixture.Ingestor.Ingest(new RemoteSignalRequest
        {
            CodexEvent = "Stop",
            RemoteHost = "dev-server",
            RemoteUser = "ubuntu",
            Workspace = "/home/ubuntu/project",
            SessionId = "session-1"
        });

        var snapshot = fixture.Store.LoadSnapshot();
        Assert.Equal(SignalSessionState.Completed, snapshot.AggregateState);
        Assert.Equal("remote prompt", snapshot.Sessions.Single().DisplayName);
    }

    [Fact]
    public void Unknown_remote_event_is_ignored_without_changing_snapshot()
    {
        using var fixture = new BridgeFixture();

        var result = fixture.Ingestor.Ingest(new RemoteSignalRequest
        {
            CodexEvent = "UnexpectedEvent",
            RemoteHost = "dev-server",
            RemoteUser = "ubuntu",
            Workspace = "/home/ubuntu/project",
            SessionId = "session-1"
        });

        var snapshot = fixture.Store.LoadSnapshot();
        Assert.False(result.ChangedState);
        Assert.Equal(SignalSessionState.Unknown, snapshot.AggregateState);
        Assert.Empty(snapshot.Sessions);
    }

    [Fact]
    public void Different_remote_hosts_create_separate_sessions()
    {
        using var fixture = new BridgeFixture();

        fixture.Ingestor.Ingest(new RemoteSignalRequest
        {
            CodexEvent = "UserPromptSubmit",
            RemoteHost = "dev-a",
            RemoteUser = "ubuntu",
            Workspace = "/repo",
            SessionId = "same-session",
            Title = "task a"
        });
        fixture.Ingestor.Ingest(new RemoteSignalRequest
        {
            CodexEvent = "UserPromptSubmit",
            RemoteHost = "dev-b",
            RemoteUser = "ubuntu",
            Workspace = "/repo",
            SessionId = "same-session",
            Title = "task b"
        });

        var snapshot = fixture.Store.LoadSnapshot();
        Assert.Equal(2, snapshot.Sessions.Count);
        Assert.Contains(snapshot.Sessions, session => session.DisplayName == "task a");
        Assert.Contains(snapshot.Sessions, session => session.DisplayName == "task b");
    }

    private sealed class BridgeFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SignalLightBridgeTests", Guid.NewGuid().ToString("N"));

        public BridgeFixture()
        {
            Paths = new SignalLightPaths(_root);
            Store = new JsonSignalStore(Paths);
            Ingestor = new RemoteSignalIngestor(Paths);
        }

        public SignalLightPaths Paths { get; }
        public JsonSignalStore Store { get; }
        public RemoteSignalIngestor Ingestor { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
