using SignalLight.Core.Events;
using SignalLight.Storage;
using SignalLight.Storage.Json;
using Xunit;

namespace SignalLight.Storage.Tests;

public sealed class JsonSignalStoreTests
{
    [Fact]
    public void Save_event_creates_event_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "SignalLightTests", Guid.NewGuid().ToString("N"));
        var paths = new SignalLightPaths(root);
        var store = new JsonSignalStore(paths);

        store.SaveEvent(new SignalEvent { EventType = SignalEventType.TaskStarted, SessionId = "demo" });

        Assert.NotEmpty(Directory.GetFiles(paths.EventsDirectory, "*.json"));
    }
}
