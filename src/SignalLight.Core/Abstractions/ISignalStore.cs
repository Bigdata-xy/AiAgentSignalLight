using SignalLight.Core.Events;
using SignalLight.Core.Sessions;
using SignalLight.Core.State;

namespace SignalLight.Core.Abstractions;

public interface ISignalStore
{
    SignalSnapshot LoadSnapshot();
    IReadOnlyList<SignalSession> LoadSessions();
    void SaveEvent(SignalEvent signalEvent);
    void SaveSession(SignalSession session);
    void SaveSnapshot(SignalSnapshot snapshot);
}
